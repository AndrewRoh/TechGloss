namespace TechGloss.GlossaryApi.Models;

public sealed class GlossaryEmbeddingState
{
    public Guid EntryId { get; set; }
    public string EmbedModel { get; set; } = "";
    public int EmbedDimension { get; set; }
    public string EmbedTextHash { get; set; } = "";
    public string VectorStore { get; set; } = "none";
    public string VectorPointId { get; set; } = "";
    public string? LastEmbeddedAt { get; set; }
    public string? LastError { get; set; }
}
