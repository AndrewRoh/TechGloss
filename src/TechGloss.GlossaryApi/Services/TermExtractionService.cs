using System.Text;
using System.Text.Json;

namespace TechGloss.GlossaryApi.Services;

/// <summary>
/// 번역 원문과 번역 결과를 Ollama LLM으로 전송해 IT 기술 용어 쌍(EN, KO, 카테고리)을 추출한다.
/// stream: false 로 단일 응답을 받아 JSON 배열로 파싱한다.
/// </summary>
public sealed class TermExtractionService
{
    private static readonly string[] AllowedCategoryNames =
    [
        "General", "Cloud", "Frontend", "Backend", "Dotnet",
        "Database", "DevOps", "Security", "Network", "AI", "Mobile", "Testing"
    ];

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

    public record RawTerm(string TermEn, string TermKo, string CategoryName);

    public async Task<IReadOnlyList<RawTerm>> ExtractAsync(
        string sourceText, string translatedText,
        string sourceLang, string targetLang,
        CancellationToken ct = default)
    {
        var (enText, koText) = sourceLang.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? (sourceText, translatedText)
            : (translatedText, sourceText);

        var userMessage = BuildPrompt(enText, koText);

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
                new { role = "user", content = userMessage }
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
            return [];
        }

        var content = await ParseOllamaContentAsync(resp, ct);
        if (string.IsNullOrWhiteSpace(content))
            return [];

        return ParseTermArray(content);
    }

    private static string BuildPrompt(string enText, string koText)
    {
        var categories = string.Join(", ", AllowedCategoryNames);
        return
            "Extract IT technical term pairs from the texts below.\n" +
            "Return ONLY a JSON array. Max 10 pairs.\n\n" +
            "Rules:\n" +
            "- Include only meaningful IT terms (not common English/Korean words)\n" +
            $"- category_name must be exactly one of: {categories}\n" +
            "- Use \"General\" when unsure of the category\n\n" +
            "Format:\n" +
            "[{\"term_en\":\"...\",\"term_ko\":\"...\",\"category_name\":\"...\"}]\n\n" +
            $"English:\n{enText}\n\n" +
            $"Korean:\n{koText}";
    }

    private async Task<string?> ParseOllamaContentAsync(
        HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            // Ollama non-streaming: {"message":{"role":"assistant","content":"..."}}
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

    private IReadOnlyList<RawTerm> ParseTermArray(string content)
    {
        // LLM이 마크다운 코드 블록으로 감싸는 경우 처리
        var start = content.IndexOf('[');
        var end   = content.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            _logger.LogWarning("용어 추출 응답에서 JSON 배열을 찾지 못함: {Content}", content);
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

                if (string.IsNullOrWhiteSpace(termEn) || string.IsNullOrWhiteSpace(termKo))
                    continue;

                // 허용 목록과 대소문자 무관 매칭 — 매칭 실패 시 General 대체
                var matched = AllowedCategoryNames.FirstOrDefault(
                    n => n.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                    ?? "General";

                results.Add(new RawTerm(termEn.Trim(), termKo.Trim(), matched));
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "용어 JSON 배열 파싱 실패: {Fragment}", jsonFragment);
            return [];
        }
    }
}
