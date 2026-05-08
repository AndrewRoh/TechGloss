using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechGloss.Core.Contracts;
using TechGloss.Core.Models;
using TechGloss.GlossaryApi.Data;
using TechGloss.GlossaryApi.Models;

namespace TechGloss.GlossaryApi.Services;

// 번역 완료 후 Ollama로 IT 용어 쌍 추출 → GlossaryEntry published 즉시 저장 + Qdrant 인덱싱
// 추출 실패(파싱 오류·Ollama 미응답)는 경고 로그만 남기고 빈 배열 반환
public sealed class TermExtractionService
{
    private readonly HttpClient _http;
    private readonly GlossaryDbContext _db;
    private readonly ILogger<TermExtractionService> _logger;
    private readonly EmbeddingService? _embedder;
    private readonly QdrantService? _qdrant;
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
        EmbeddingService? embedder = null,
        QdrantService? qdrant = null)
    {
        _http    = http;
        _db      = db;
        _logger  = logger;
        _embedder = embedder;
        _qdrant   = qdrant;
        _baseUrl = config["TechGloss:Ollama:BaseUrl"] ?? "http://172.20.64.76:11434";
        _model   = config["TechGloss:Ollama:Model"]  ?? "gemma4:latest";
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

        var results = new List<ExtractedTermRow>();

        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term.TermEn) || string.IsNullOrWhiteSpace(term.TermKo))
                continue;

            var categoryName = AllowedCategories.Contains(term.Category ?? "")
                ? term.Category! : "General";

            var enNorm = term.TermEn.Trim().ToLowerInvariant();
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
                    Status           = "published",   // 번역 추출 → 즉시 published
                    CreatedAt        = DateTimeOffset.UtcNow,
                    UpdatedAt        = DateTimeOffset.UtcNow,
                };
                _db.Entries.Add(entry);
                isNew = true;
            }
            else
            {
                entry = existing;
                isNew = false;

                var updated = false;
                if (string.IsNullOrWhiteSpace(existing.TermKo))
                {
                    existing.TermKo           = term.TermKo.Trim();
                    existing.TermKoNormalized = term.TermKo.Trim()
                        .Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
                    updated = true;
                }
                // draft 상태인 기존 항목도 이번 번역에서 재등장했으면 published로 승격
                if (existing.Status == "draft")
                {
                    existing.Status = "published";
                    updated = true;
                }
                if (updated) existing.UpdatedAt = DateTimeOffset.UtcNow;
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

        // Qdrant 인덱싱 — EmbeddingService·QdrantService 둘 다 등록된 경우에만 실행
        if (_embedder is not null && _qdrant is not null)
        {
            foreach (var row in results)
            {
                await IndexToQdrantAsync(row.EntryId, row.CategoryName, ct);
            }
        }

        _logger.LogInformation(
            "용어 자동 추출 완료: 총 {Total}건 (신규 {New}건, Qdrant={Qdrant})",
            results.Count,
            results.Count(r => r.IsNew),
            _qdrant is not null ? "활성" : "비활성");

        return results;
    }

    private async Task IndexToQdrantAsync(
        Guid entryId, string categoryName, CancellationToken ct)
    {
        try
        {
            var entry = await _db.Entries.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == entryId, ct);
            if (entry is null) return;

            var embedText = EmbeddingService.BuildEmbedText(
                categoryName, entry.TermEn, entry.TermKo, entry.DefinitionKo);
            var vector = await _embedder!.EmbedAsync(embedText, ct);

            await _qdrant!.UpsertPointAsync(
                entry.Id, vector, entry.TermEn, entry.TermKo, categoryName, ct);

            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(embedText)))[..16];

            var state = await _db.EmbeddingStates.FindAsync(new object[] { entry.Id }, ct);
            if (state is null)
            {
                _db.EmbeddingStates.Add(new GlossaryEmbeddingState
                {
                    EntryId        = entry.Id,
                    EmbedModel     = "nomic-embed-text",
                    EmbedDimension = vector.Length,
                    EmbedTextHash  = hash,
                    VectorStore    = "qdrant",
                    VectorPointId  = entry.Id.ToString(),
                    LastEmbeddedAt = DateTimeOffset.UtcNow.ToString("O"),
                });
            }
            else
            {
                state.EmbedTextHash  = hash;
                state.VectorStore    = "qdrant";
                state.LastEmbeddedAt = DateTimeOffset.UtcNow.ToString("O");
                state.LastError      = null;
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant 인덱싱 실패: EntryId={EntryId}", entryId);
        }
    }

    private sealed record RawTerm(
        string? TermEn,
        string? TermKo,
        string? Category,
        string? DefinitionKo);
}
