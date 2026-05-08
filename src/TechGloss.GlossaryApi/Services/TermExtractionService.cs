using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Contracts;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;

namespace TechGloss.GlossaryApi.Services;

// 번역 완료 후 Ollama로 IT 용어 쌍 추출 → GlossaryEntry published 즉시 저장 + pgvector 임베딩
// 추출 실패(파싱 오류·Ollama 미응답)는 경고 로그만 남기고 빈 배열 반환
public sealed class TermExtractionService
{
    private readonly HttpClient _http;
    private readonly GlossaryDbContext _db;
    private readonly ILogger<TermExtractionService> _logger;
    private readonly EmbeddingService? _embedder;
    private readonly string _baseUrl;
    private readonly string _model;

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "General", "Cloud", "Frontend", "Backend", "Dotnet",
        "Database", "DevOps", "Security", "Network", "AI", "Mobile", "Testing"
    };

    public TermExtractionService(
        HttpClient http,
        GlossaryDbContext db,
        ILogger<TermExtractionService> logger,
        IConfiguration config,
        EmbeddingService? embedder = null)
    {
        _http     = http;
        _db       = db;
        _logger   = logger;
        _embedder = embedder;
        _baseUrl  = config["TechGloss:Ollama:BaseUrl"] ?? "http://172.20.64.76:11434";
        _model    = config["TechGloss:Ollama:Model"]   ?? "gemma4:latest";
    }

    public async Task<List<ExtractedTermRow>> ExtractAsync(
        ExtractTermsRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SourceText) ||
            string.IsNullOrWhiteSpace(req.TranslatedText))
            return new List<ExtractedTermRow>();

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

        var results    = new List<ExtractedTermRow>();
        var newEntries = new List<(GlossaryEntry entry, string categoryName)>();

        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term.TermEn) || string.IsNullOrWhiteSpace(term.TermKo))
                continue;

            var categoryName = AllowedCategories.Contains(term.Category ?? "")
                ? term.Category! : "General";

            var enNorm   = term.TermEn.Trim().ToLowerInvariant();
            var existing = await _db.Entries.AsNoTracking()
                .Where(e => e.TermEnNormalized == enNorm)
                .FirstOrDefaultAsync(ct);

            bool isNew;
            GlossaryEntry entry;

            if (existing is null)
            {
                var cat = await _db.Categories.AsNoTracking()
                    .Where(c => c.Name == categoryName)
                    .FirstOrDefaultAsync(ct);

                entry = new GlossaryEntry
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
                    Status           = "published",
                    CreatedAt        = DateTimeOffset.UtcNow,
                    UpdatedAt        = DateTimeOffset.UtcNow,
                };
                _db.Entries.Add(entry);
                isNew = true;
                newEntries.Add((entry, categoryName));
            }
            else
            {
                entry = existing;
                isNew = false;
            }

            results.Add(new ExtractedTermRow
            {
                EntryId      = entry.Id,
                TermEn       = entry.TermEn,
                TermKo       = entry.TermKo,
                CategoryName = categoryName,
                IsNew        = isNew,
            });
        }

        await _db.SaveChangesAsync(ct);

        // pgvector 임베딩 — EmbeddingService 등록된 경우에만 신규 항목에 대해 실행
        if (_embedder is not null)
        {
            foreach (var (entry, catName) in newEntries)
            {
                try
                {
                    var embedText = EmbeddingService.BuildEmbedText(
                        catName, entry.TermEn, entry.TermKo, entry.DefinitionKo);
                    entry.Embedding = await _embedder.EmbedAsync(embedText, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "임베딩 실패: EntryId={EntryId}", entry.Id);
                }
            }

            if (newEntries.Count > 0)
                await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "용어 자동 추출 완료: 총 {Total}건 (신규 {New}건, 임베딩={Embed})",
            results.Count,
            results.Count(r => r.IsNew),
            _embedder is not null ? "활성" : "비활성");

        return results;
    }

    private sealed record RawTerm(
        string? TermEn,
        string? TermKo,
        string? Category,
        string? DefinitionKo);
}
