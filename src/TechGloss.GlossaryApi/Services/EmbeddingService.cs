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

    public static string BuildEmbedText(
        string categorySlug, string termEn, string termKo, string definitionKo)
        => $"{categorySlug}: {termEn} => {termKo}. {definitionKo}";

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            $"{_baseUrl.TrimEnd('/')}/api/embeddings",
            new { model = _model, prompt = text }, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var arr = doc.RootElement.GetProperty("embedding");
        return arr.EnumerateArray().Select(e => e.GetSingle()).ToArray();
    }
}
