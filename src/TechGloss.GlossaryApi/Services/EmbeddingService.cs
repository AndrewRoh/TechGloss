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
}
