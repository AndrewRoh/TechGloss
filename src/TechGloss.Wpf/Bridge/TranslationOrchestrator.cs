using Microsoft.Extensions.Logging;
using TechGloss.Core.Contracts;
using TechGloss.Core.Services;
using TechGloss.Infrastructure.Logging;

namespace TechGloss.Wpf.Bridge;

public sealed class TranslationOrchestrator
{
    private readonly IGlossaryClient _glossary;
    private readonly IOllamaChatClient _llm;
    private readonly ILogger<TranslationOrchestrator> _logger;

    public TranslationOrchestrator(IGlossaryClient glossary, IOllamaChatClient llm,
        ILogger<TranslationOrchestrator> logger)
    {
        _glossary = glossary;
        _llm = llm;
        _logger = logger;
    }

    public async Task RunStreamingAsync(
        string text, string sourceLang, string targetLang, string? categorySlug,
        IProgress<string> onChunk, CancellationToken ct = default)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation(
            "Translation requestId={RequestId} textHash={Hash} src={Src} tgt={Tgt}",
            requestId, MaskingLogger.HashText(text), sourceLang, targetLang);

        IReadOnlyList<GlossarySearchRow> glossaryRows;
        try
        {
            glossaryRows = await _glossary.SearchAsync(new GlossarySearchRequest
            {
                QueryText    = text,
                SourceLang   = sourceLang,
                TargetLang   = targetLang,
                TopK         = 8,
                CategorySlug = categorySlug
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GlossaryApi 미응답 — 용어집 없이 번역 진행 (requestId={RequestId})", requestId);
            glossaryRows = [];
        }

        var systemPrompt = PromptBuilder.BuildSystemPrompt(sourceLang, targetLang, glossaryRows);

        await foreach (var chunk in _llm.StreamChatAsync(systemPrompt, text, ct))
        {
            onChunk.Report(chunk);
        }
    }
}
