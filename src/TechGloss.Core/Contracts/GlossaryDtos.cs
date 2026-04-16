namespace TechGloss.Core.Contracts;

public sealed class GlossarySearchRequest
{
    public required string QueryText { get; init; }
    public required string SourceLang { get; init; }
    public required string TargetLang { get; init; }
    public int TopK { get; init; } = 8;
    public string? CategoryName { get; init; }
}

public sealed class GlossarySearchRow
{
    public Guid EntryId { get; init; }
    public string TermEn { get; init; } = "";
    public string TermKo { get; init; } = "";
    public string DefinitionKo { get; init; } = "";
    public string CategoryName { get; init; } = "";
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

/// <summary>번역 결과에서 용어를 자동 추출하도록 GlossaryApi에 요청하는 DTO</summary>
public sealed class ExtractTermsRequest
{
    public required string SourceText { get; init; }
    public required string TranslatedText { get; init; }
    public required string SourceLang { get; init; }
    public required string TargetLang { get; init; }
}

/// <summary>자동 추출 결과로 DB에 추가/업데이트된 용어 항목</summary>
public sealed class ExtractedTermRow
{
    public Guid EntryId { get; init; }
    public string TermEn { get; init; } = "";
    public string TermKo { get; init; } = "";
    public string CategoryName { get; init; } = "";
    public bool IsNew { get; init; }
}
