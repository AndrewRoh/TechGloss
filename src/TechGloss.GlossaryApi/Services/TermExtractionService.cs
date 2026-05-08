using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Contracts;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;

namespace TechGloss.GlossaryApi.Services;

// CLAUDE.md ExtractTerms 흐름 구현
// Ollama로 IT 용어 쌍 추출 → GlossaryEntry draft upsert
// 추출 실패(파싱 오류·Ollama 미응답)는 경고 로그만 남기고 빈 배열 반환
public sealed class TermExtractionService
{
    private readonly HttpClient _http;
    private readonly GlossaryDbContext _db;
    private readonly ILogger<TermExtractionService> _logger;
    private readonly string _baseUrl;
    private readonly string _model;

    // 카테고리 허용 목록 (CLAUDE.md 명시) — 대소문자 무관 매칭, 불일치 시 "General" 대체
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "General", "Cloud", "Frontend", "Backend", "Dotnet",
        "Database", "DevOps", "Security", "Network", "AI", "Mobile", "Testing"
    };

    public TermExtractionService(
        HttpClient http, GlossaryDbContext db,
        ILogger<TermExtractionService> logger, IConfiguration config)
    {
        _http    = http;
        _db      = db;
        _logger  = logger;
        _baseUrl = config["TechGloss:Ollama:BaseUrl"] ?? "http://172.20.64.76:11434";
        _model   = config["TechGloss:Ollama:Model"]  ?? "gemma4:latest";
    }

    public async Task<List<ExtractedTermRow>> ExtractAsync(
        ExtractTermsRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SourceText) ||
            string.IsNullOrWhiteSpace(req.TranslatedText))
            return new List<ExtractedTermRow>();

        // Ollama에게 JSON 배열 형식으로 용어 쌍 추출 요청 (stream: false — 단일 응답 필요)
        var prompt =
            "다음 EN/KO 텍스트 쌍에서 IT 기술 용어 쌍을 JSON 배열로 추출하세요.\n" +
            "출력 형식: [{\"term_en\":\"...\",\"term_ko\":\"...\",\"category\":\"...\",\"definition_ko\":\"...\"}]\n" +
            "카테고리 허용값: General,Cloud,Frontend,Backend,Dotnet,Database,DevOps,Security,Network,AI,Mobile,Testing\n" +
            "번역되지 않은 코드 식별자·고유명사는 제외하세요.\n\n" +
            $"[원문({req.SourceLang})]\n{req.SourceText}\n\n" +
            $"[번역({req.TargetLang})]\n{req.TranslatedText}\n\n" +
            "JSON 배열만 출력 (마크다운 블록 없이):";

        string rawJson;
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"{_baseUrl.TrimEnd('/')}/api/chat",
                new
                {
                    model    = _model,
                    stream   = false,
                    messages = new[] { new { role = "user", content = prompt } }
                }, ct);
            resp.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(
                await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            // Ollama /api/chat non-stream 응답: { "message": { "content": "..." } }
            rawJson = doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "[]";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExtractTerms: Ollama 호출 실패 — 빈 배열 반환");
            return new List<ExtractedTermRow>();
        }

        // LLM 출력에서 마크다운 코드 블록 제거 후 JSON 파싱
        var jsonText = rawJson.Trim();
        if (jsonText.StartsWith("```"))
        {
            var start = jsonText.IndexOf('\n') + 1;
            var end   = jsonText.LastIndexOf("```");
            jsonText  = end > start ? jsonText[start..end].Trim() : "[]";
        }

        List<RawTerm>? terms;
        try
        {
            terms = JsonSerializer.Deserialize<List<RawTerm>>(jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExtractTerms: JSON 파싱 실패 raw={Raw}",
                jsonText[..Math.Min(200, jsonText.Length)]);
            return new List<ExtractedTermRow>();
        }

        if (terms is null or { Count: 0 }) return new List<ExtractedTermRow>();

        var results = new List<ExtractedTermRow>();

        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term.TermEn) || string.IsNullOrWhiteSpace(term.TermKo))
                continue;

            // 카테고리 허용 목록 검증 — 불일치 시 "General" 대체
            var categoryName = AllowedCategories.Contains(term.Category ?? "")
                ? term.Category! : "General";

            // TermEnNormalized 기준 중복 확인 (CLAUDE.md 명시)
            var enNorm = term.TermEn.Trim().ToLowerInvariant();
            var existing = await _db.Entries.AsNoTracking()
                .Where(e => e.TermEnNormalized == enNorm)
                .FirstOrDefaultAsync(ct);

            bool isNew;
            Guid entryId;

            if (existing is null)
            {
                // 신규: GlossaryEntry INSERT (status=draft)
                var cat = await _db.Categories.AsNoTracking()
                    .Where(c => c.Name == categoryName)
                    .FirstOrDefaultAsync(ct);

                var entry = new GlossaryEntry
                {
                    Id               = Guid.NewGuid(),
                    TermEn           = term.TermEn.Trim(),
                    TermKo           = term.TermKo.Trim(),
                    TermEnNormalized = enNorm,
                    TermKoNormalized = term.TermKo.Trim()
                                           .Normalize(System.Text.NormalizationForm.FormKC)
                                           .ToLowerInvariant(),
                    DefinitionKo     = term.DefinitionKo ?? "",
                    CategoryId       = cat?.Id,
                    Status           = "draft",
                    CreatedAt        = DateTimeOffset.UtcNow,
                    UpdatedAt        = DateTimeOffset.UtcNow,
                };
                _db.Entries.Add(entry);
                entryId = entry.Id;
                isNew   = true;
            }
            else
            {
                // 기존: TermKo가 비어있을 때만 보완 (CLAUDE.md 규칙)
                entryId = existing.Id;
                isNew   = false;
                if (string.IsNullOrWhiteSpace(existing.TermKo))
                {
                    await _db.Entries
                        .Where(e => e.Id == existing.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(e => e.TermKo, term.TermKo.Trim())
                            .SetProperty(e => e.UpdatedAt, DateTimeOffset.UtcNow), ct);
                }
            }

            results.Add(new ExtractedTermRow
            {
                EntryId      = entryId,
                TermEn       = term.TermEn.Trim(),
                TermKo       = term.TermKo.Trim(),
                CategoryName = categoryName,
                IsNew        = isNew,
            });
        }

        await _db.SaveChangesAsync(ct);
        return results;
    }

    // Ollama 응답 JSON 파싱용 내부 레코드 — camelCase 매핑
    private sealed record RawTerm(
        string? TermEn,
        string? TermKo,
        string? Category,
        string? DefinitionKo);
}
