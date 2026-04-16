namespace TechGloss.Core.Models;

public sealed class GlossaryEntry
{
    public Guid Id { get; set; }
    public string TermKo { get; set; } = "";
    public string TermEn { get; set; } = "";
    public string TermKoNormalized { get; set; } = "";
    public string TermEnNormalized { get; set; } = "";
    public string DefinitionKo { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public string? Notes { get; set; }
    public bool CaseSensitive { get; set; } = false;
    public bool IsPreferred { get; set; } = true;
    public string Status { get; set; } = "draft";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class GlossaryCategory
{
    public Guid Id { get; set; }
    /// <summary>영문 카테고리명 — UNIQUE 제약. 예: "General", "Cloud", "DevOps"</summary>
    public string Name { get; set; } = "";
}
