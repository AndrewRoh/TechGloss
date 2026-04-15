using System.Text.Json;
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
        JsonElement payload, Action<string> replyToWeb, CancellationToken ct)
    {
        var text = payload.GetProperty("text").GetString() ?? "";
        var sourceLang = payload.GetProperty("source_lang").GetString() ?? "en";
        var targetLang = payload.GetProperty("target_lang").GetString() ?? "ko";
        var categorySlug = payload.TryGetProperty("category_slug", out var cs)
            ? cs.GetString() : null;

        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation(
            "Translation requestId={RequestId} textHash={Hash} src={Src} tgt={Tgt}",
            requestId, MaskingLogger.HashText(text), sourceLang, targetLang);

        var glossaryRows = await _glossary.SearchAsync(new GlossarySearchRequest
        {
            QueryText    = text,
            SourceLang   = sourceLang,
            TargetLang   = targetLang,
            TopK         = 8,
            CategorySlug = categorySlug
        }, ct);

        var systemPrompt = PromptBuilder.BuildSystemPrompt(sourceLang, targetLang, glossaryRows);

        try
        {
            await foreach (var chunk in _llm.StreamChatAsync(systemPrompt, text, ct))
            {
                replyToWeb(JsonSerializer.Serialize(
                    new { type = "translation.chunk", payload = chunk }));
            }
            replyToWeb(JsonSerializer.Serialize(new { type = "translation.done" }));
        }
        catch (OperationCanceledException)
        {
            replyToWeb(JsonSerializer.Serialize(
                new { type = "translation.error", payload = "cancelled" }));
        }
        catch (Exception ex)
        {
            replyToWeb(JsonSerializer.Serialize(
                new { type = "translation.error", payload = ex.Message }));
        }
    }
}
