using System.Text;
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
        string text, string sourceLang, string targetLang, string? categoryName,
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
                CategoryName = categoryName
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "GlossaryApi 미응답 — 용어집 없이 번역 진행 (requestId={RequestId})", requestId);
            glossaryRows = [];
        }

        var systemPrompt = PromptBuilder.BuildSystemPrompt(sourceLang, targetLang, glossaryRows);

        // 스트리밍하면서 번역 결과 전체를 수집 (용어 자동 추출용)
        var translatedSb = new StringBuilder();
        await foreach (var chunk in _llm.StreamChatAsync(systemPrompt, text, ct))
        {
            onChunk.Report(chunk);
            translatedSb.Append(chunk);
        }

        // 번역 완료 후 용어 자동 추출 (CancellationToken.None — UI 취소와 무관하게 실행)
        await ExtractTermsAfterTranslationAsync(
            text, translatedSb.ToString(), sourceLang, targetLang, requestId);
    }

    private async Task ExtractTermsAfterTranslationAsync(
        string sourceText, string translatedText,
        string sourceLang, string targetLang, string requestId)
    {
        if (string.IsNullOrWhiteSpace(translatedText)) return;
        try
        {
            var extracted = await _glossary.ExtractTermsAsync(new ExtractTermsRequest
            {
                SourceText     = sourceText,
                TranslatedText = translatedText,
                SourceLang     = sourceLang,
                TargetLang     = targetLang,
            }, CancellationToken.None);

            var newCount   = extracted.Count(r => r.IsNew);
            var totalCount = extracted.Count;
            _logger.LogInformation(
                "용어 자동 추출 완료 requestId={RequestId}: 총 {Total}건 (신규 {New}건)",
                requestId, totalCount, newCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "용어 자동 추출 실패 requestId={RequestId} — 무시하고 계속", requestId);
        }
    }
}
