namespace TechGloss.Infrastructure.Options;

public sealed class TechGlossOptions
{
    public OllamaOptions Ollama { get; set; } = new();
    public GlossaryApiOptions GlossaryApi { get; set; } = new();
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://172.20.64.76:11434";
    public string Model { get; set; } = "gemma4:latest";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public string ChatPath { get; set; } = "/api/chat";
    public bool UseOpenAiCompatiblePath { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class GlossaryApiOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:5088";
    /// <summary>ExtractTerms 등 Ollama 내부 호출을 포함하는 요청을 커버할 수 있도록 Ollama 타임아웃보다 크게 설정</summary>
    public int TimeoutSeconds { get; set; } = 180;
}
