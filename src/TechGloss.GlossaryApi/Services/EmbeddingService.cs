using System.Net.Http.Json;
using System.Text.Json;

namespace TechGloss.GlossaryApi.Services;

public sealed class EmbeddingService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;

    public EmbeddingService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _baseUrl = config["TechGloss:Ollama:BaseUrl"] ?? "http://172.20.64.76:11434";
        _model   = config["TechGloss:Ollama:EmbeddingModel"] ?? "nomic-embed-text";
    }

    // 저장·검색 양쪽에서 동일 포맷 사용 필수 — 포맷 변경 시 전체 재임베딩 필요
    public static string BuildEmbedText(
        string categoryName, string termEn, string termKo, string definitionKo)
        => $"{categoryName}: {termEn} => {termKo}. {definitionKo}";

    // nomic-embed-text 기준 768차원 float[] 반환
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            $"{_baseUrl.TrimEnd('/')}/api/embeddings",
            new { model = _model, prompt = text },
            ct);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        // Ollama 응답: { "embedding": [0.1, 0.2, ...] }
        var arr = doc.RootElement.GetProperty("embedding");
        return arr.EnumerateArray().Select(e => e.GetSingle()).ToArray();
    }
}
