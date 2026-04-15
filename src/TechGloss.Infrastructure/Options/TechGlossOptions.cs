namespace TechGloss.Infrastructure.Options;

public sealed class TechGlossOptions
{
    public OllamaOptions Ollama { get; set; } = new();
    public GlossaryApiOptions GlossaryApi { get; set; } = new();
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://172.20.64.76:11434";
    public string Model { get; set; } = "gemma4:31b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public string ChatPath { get; set; } = "/api/chat";
    public bool UseOpenAiCompatiblePath { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class GlossaryApiOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:5088";
}
