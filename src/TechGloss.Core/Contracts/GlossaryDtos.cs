namespace TechGloss.Core.Contracts;

public sealed class GlossarySearchRequest
{
    public required string QueryText { get; init; }
    public required string SourceLang { get; init; }
    public required string TargetLang { get; init; }
    public int TopK { get; init; } = 8;
    public string? CategorySlug { get; init; }
}

public sealed class GlossarySearchRow
{
    public Guid EntryId { get; init; }
    public string TermEn { get; init; } = "";
    public string TermKo { get; init; } = "";
    public string DefinitionKo { get; init; } = "";
    public string CategorySlug { get; init; } = "";
    public string Source { get; init; } = "";
    public string Target { get; init; } = "";
}

public sealed class GlossaryLookupRow
{
    public Guid Id { get; init; }
    public string TermKo { get; init; } = "";
    public string TermEn { get; init; } = "";
    public string DefinitionKo { get; init; } = "";
    public string? CategoryName { get; init; }
}
