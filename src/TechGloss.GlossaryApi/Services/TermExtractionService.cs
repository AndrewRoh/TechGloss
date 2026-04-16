using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechGloss.GlossaryApi.Services;

/// <summary>
/// 번역 원문과 번역 결과를 문장 단위로 나누어 Ollama LLM에 순차 전송하고,
/// 각 문장 쌍에서 IT 기술 용어(EN, KO, 카테고리, 정의)를 추출한다.
/// </summary>
public sealed class TermExtractionService
{
    private static readonly string[] AllowedCategoryNames =
    [
        "General", "Cloud", "Frontend", "Backend", "Dotnet",
        "Database", "DevOps", "Security", "Network", "AI", "Mobile", "Testing"
    ];

    /// <summary>문장 분할 기준: 문장 종결 부호 뒤 공백, 또는 줄바꿈</summary>
    private static readonly Regex SentenceSplitRegex = new(
        @"(?<=[.!?。！？])\s+|\r?\n+",
        RegexOptions.Compiled);

    /// <summary>한 번 실행에서 LLM을 호출할 최대 문장 쌍 수</summary>
    private const int MaxPairs = 20;

    /// <summary>너무 짧은 문장 조각은 무시 (코드 토큰, 구두점 등)</summary>
    private const int MinSentenceLength = 10;

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly ILogger<TermExtractionService> _logger;

    public TermExtractionService(HttpClient http, IConfiguration config,
        ILogger<TermExtractionService> logger)
    {
        _http    = http;
        _baseUrl = config["TechGloss:Ollama:BaseUrl"] ?? "http://172.20.64.76:11434";
        _model   = config["TechGloss:Ollama:Model"]  ?? "gemma4:latest";
        _logger  = logger;
    }

    public record RawTerm(string TermEn, string TermKo, string CategoryName, string DefinitionKo);

    /// <summary>
    /// 텍스트를 문장 단위로 분할 → 쌍(pair)으로 묶어 LLM 순차 호출 → 중복 제거 후 반환.
    /// </summary>
    public async Task<IReadOnlyList<RawTerm>> ExtractAsync(
        string sourceText, string translatedText,
        string sourceLang, string targetLang,
        CancellationToken ct = default)
    {
        var (enText, koText) = sourceLang.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? (sourceText, translatedText)
            : (translatedText, sourceText);

        var enSentences = SplitSentences(enText);
        var koSentences = SplitSentences(koText);

        var pairCount = Math.Min(Math.Min(enSentences.Count, koSentences.Count), MaxPairs);
        if (pairCount == 0)
        {
            _logger.LogDebug("문장 분할 결과 쌍이 없어 용어 추출 생략");
            return [];
        }

        _logger.LogDebug("문장 쌍 {Count}개에서 용어 추출 시작", pairCount);

        // 문장 쌍마다 LLM 호출 → TermEn 소문자 기준 중복 제거
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged  = new List<RawTerm>();

        for (int i = 0; i < pairCount; i++)
        {
            if (ct.IsCancellationRequested) break;

            var terms = await ExtractFromPairAsync(enSentences[i], koSentences[i], i, ct);
            foreach (var t in terms)
            {
                if (seen.Add(t.TermEn.ToLowerInvariant()))
                    merged.Add(t);
            }
        }

        _logger.LogDebug("용어 추출 완료 — 총 {Count}건 (중복 제거 후)", merged.Count);
        return merged;
    }

    // ── 내부 메서드 ────────────────────────────────────────────────────────────

    /// <summary>문장 하나씩 LLM 호출. 실패 시 빈 목록 반환 (비치명적).</summary>
    private async Task<IReadOnlyList<RawTerm>> ExtractFromPairAsync(
        string enSentence, string koSentence, int index, CancellationToken ct)
    {
        var content = await CallOllamaAsync(enSentence, koSentence, ct);
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogDebug("문장 {Index}: LLM 응답 없음 — 건너뜀", index);
            return [];
        }
        return ParseTermArray(content, index);
    }

    /// <summary>Ollama /api/chat 호출 (stream: false) → assistant content 문자열 반환.</summary>
    private async Task<string?> CallOllamaAsync(
        string enSentence, string koSentence, CancellationToken ct)
    {
        var requestBody = new
        {
            model    = _model,
            stream   = false,
            messages = new[]
            {
                new
                {
                    role    = "system",
                    content = "You are a precise IT terminology extractor. " +
                              "Output ONLY a valid JSON array — no markdown, no explanation."
                },
                new { role = "user", content = BuildPrompt(enSentence, koSentence) }
            }
        };

        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsJsonAsync(
                $"{_baseUrl.TrimEnd('/')}/api/chat", requestBody, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama 용어 추출 요청 실패");
            return null;
        }

        try
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentProp))
            {
                return contentProp.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama 응답 파싱 실패");
        }
        return null;
    }

    /// <summary>텍스트를 문장 단위로 분할하고 너무 짧은 조각은 제거.</summary>
    private static List<string> SplitSentences(string text)
    {
        var parts = SentenceSplitRegex
            .Split(text.Trim())
            .Select(s => s.Trim())
            .Where(s => s.Length >= MinSentenceLength)
            .ToList();

        // 분할 결과가 없으면 원문 전체를 단일 문장으로 취급
        return parts.Count > 0 ? parts : [text.Trim()];
    }

    private static string BuildPrompt(string enSentence, string koSentence)
    {
        var categories = string.Join(", ", AllowedCategoryNames);
        return
            "Extract IT technical term pairs from the sentence below.\n" +
            "Return ONLY a JSON array. If no meaningful IT terms exist, return [].\n\n" +
            "Rules:\n" +
            "- Include only meaningful IT terms (not common English/Korean words)\n" +
            $"- category_name must be exactly one of: {categories}\n" +
            "- Use \"General\" when unsure of the category\n" +
            "- definition_ko: concise Korean technical definition (1-2 sentences)\n\n" +
            "Format:\n" +
            "[{\"term_en\":\"...\",\"term_ko\":\"...\",\"category_name\":\"...\",\"definition_ko\":\"...\"}]\n\n" +
            $"English sentence:\n{enSentence}\n\n" +
            $"Korean sentence:\n{koSentence}";
    }

    private IReadOnlyList<RawTerm> ParseTermArray(string content, int sentenceIndex)
    {
        var start = content.IndexOf('[');
        var end   = content.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            _logger.LogDebug("문장 {Index}: JSON 배열 없음 — 건너뜀", sentenceIndex);
            return [];
        }

        var jsonFragment = content[start..(end + 1)];
        try
        {
            using var doc = JsonDocument.Parse(jsonFragment);
            var results = new List<RawTerm>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var termEn = element.TryGetProperty("term_en", out var enProp)
                    ? enProp.GetString() ?? "" : "";
                var termKo = element.TryGetProperty("term_ko", out var koProp)
                    ? koProp.GetString() ?? "" : "";
                var categoryName = element.TryGetProperty("category_name", out var catProp)
                    ? catProp.GetString() ?? "General" : "General";
                var definitionKo = element.TryGetProperty("definition_ko", out var defProp)
                    ? defProp.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(termEn) || string.IsNullOrWhiteSpace(termKo))
                    continue;

                var matched = AllowedCategoryNames.FirstOrDefault(
                    n => n.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                    ?? "General";

                results.Add(new RawTerm(termEn.Trim(), termKo.Trim(), matched, definitionKo.Trim()));
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "문장 {Index}: JSON 파싱 실패 — {Fragment}", sentenceIndex, jsonFragment);
            return [];
        }
    }
}
