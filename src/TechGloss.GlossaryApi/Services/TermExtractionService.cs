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

        // var prompt =
        //     "다음 EN/KO 텍스트 쌍에서 IT 기술 용어 쌍을 JSON 배열로 추출하세요.\n" +
        //     "출력 형식: [{\"term_en\":\"...\",\"term_ko\":\"...\",\"category\":\"...\",\"definition_ko\":\"...\"}]\n" +
        //     "카테고리 허용값: General,Cloud,Frontend,Backend,Dotnet,Database,DevOps,Security,Network,AI,Mobile,Testing\n" +
        //     "번역되지 않은 코드 식별자·고유명사는 제외하세요.\n\n" +
        //     $"[원문({req.SourceLang})]\n{req.SourceText}\n\n" +
        //     $"[번역({req.TargetLang})]\n{req.TranslatedText}\n\n" +
        //     "JSON 배열만 출력 (마크다운 블록 없이):";
        var prompt =
            "Your response MUST be a raw JSON array and nothing else — no explanation, no markdown, no headers.\n" +
            "Extract every word or phrase that appears translated between the two texts below.\n" +
            "Include simple common words (e.g. Italian→이탈리아, game→게임, originally→원래, owned→소유).\n" +
            "Exclude untranslated proper nouns (person names, product names left as-is).\n" +
            "JSON format (output the array only, starting with [ and ending with ]):\n" +
            "[{\"term_en\":\"...\",\"term_ko\":\"...\",\"category\":\"...\",\"definition_ko\":\"...\"}]\n\n" +
            $"[Source({req.SourceLang})]\n{req.SourceText}\n\n" +
            $"[Translation({req.TargetLang})]\n{req.TranslatedText}\n\n" +
            "[";

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

        // 마크다운 코드 블록 제거
        if (jsonText.StartsWith("```"))
        {
            var start = jsonText.IndexOf('\n') + 1;
            var end   = jsonText.LastIndexOf("```");
            jsonText  = end > start ? jsonText[start..end].Trim() : "[]";
        }

        // LLM이 설명문과 함께 반환했을 때 JSON 배열 부분만 추출
        if (!jsonText.StartsWith("["))
        {
            var arrayStart = jsonText.IndexOf('[');
            var arrayEnd   = jsonText.LastIndexOf(']');
            jsonText = arrayStart >= 0 && arrayEnd > arrayStart
                ? jsonText[arrayStart..(arrayEnd + 1)]
                : "[]";
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

        var results       = new List<ExtractedTermRow>();
        var newEntries    = new List<(GlossaryEntry entry, string categoryName)>();
        var categoryCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term.TermEn) || string.IsNullOrWhiteSpace(term.TermKo))
                continue;

            var categoryName = string.IsNullOrWhiteSpace(term.Category) ? "General" : term.Category!.Trim();

            var enNorm   = term.TermEn.Trim().ToLowerInvariant();
            var existing = await _db.Entries.AsNoTracking()
                .Where(e => e.TermEnNormalized == enNorm)
                .FirstOrDefaultAsync(ct);

            bool isNew;
            GlossaryEntry entry;

            if (existing is null)
            {
                if (!categoryCache.TryGetValue(categoryName, out var categoryId))
                {
                    var norm = NormalizeCategory(categoryName);
                    var cat  = await _db.Categories
                        .FirstOrDefaultAsync(c => c.NameEnNormalized == norm, ct);
                    if (cat is null)
                    {
                        cat = new TechGloss.Core.Models.GlossaryCategory
                        {
                            Id               = Guid.NewGuid(),
                            Name             = categoryName,
                            NameEnNormalized = norm,
                        };
                        _db.Categories.Add(cat);
                        await _db.SaveChangesAsync(ct);
                        _logger.LogInformation("새 카테고리 생성: {Category}", categoryName);
                    }
                    categoryId = cat.Id;
                    categoryCache[categoryName] = categoryId;
                }

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
                    CategoryId       = categoryCache[categoryName],
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

    private static string NormalizeCategory(string s) =>
        s.Normalize(System.Text.NormalizationForm.FormKC).Trim().ToLowerInvariant();

    private sealed record RawTerm(
        [property: System.Text.Json.Serialization.JsonPropertyName("term_en")]       string? TermEn,
        [property: System.Text.Json.Serialization.JsonPropertyName("term_ko")]       string? TermKo,
        [property: System.Text.Json.Serialization.JsonPropertyName("category")]      string? Category,
        [property: System.Text.Json.Serialization.JsonPropertyName("definition_ko")] string? DefinitionKo);
}
