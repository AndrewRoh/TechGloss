using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TechGloss.Core.Contracts;
using TechGloss.Core.Messages;

namespace TechGloss.Wpf.Bridge;

public sealed class HostBridge
{
    private readonly IGlossaryClient _glossary;
    private readonly TranslationOrchestrator _translator;
    private readonly ILogger<HostBridge> _logger;

    public HostBridge(IGlossaryClient glossary, TranslationOrchestrator translator,
        ILogger<HostBridge> logger)
    {
        _glossary = glossary;
        _translator = translator;
        _logger = logger;
    }

    public async Task HandleWebMessageAsync(
        string webMessageJson, Action<string> replyToWeb,
        CancellationToken ct = default)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        var sw = Stopwatch.StartNew();

        WebEnvelope msg;
        try
        {
            msg = JsonSerializer.Deserialize<WebEnvelope>(webMessageJson)
                  ?? throw new JsonException("null envelope");
        }
        catch
        {
            replyToWeb(JsonSerializer.Serialize(new { type = "error", message = "invalid_json" }));
            return;
        }

        switch (msg.Type)
        {
            case "lookup":
            {
                var q = msg.Payload.GetProperty("q").GetString() ?? "";
                var lang = msg.Payload.TryGetProperty("lang", out var l)
                    ? l.GetString() ?? "auto" : "auto";
                var rows = await _glossary.LookupAsync(q, lang, ct: ct);
                replyToWeb(JsonSerializer.Serialize(new { type = "lookup.result", payload = rows }));
                break;
            }
            case "translate":
            {
                await _translator.RunStreamingAsync(msg.Payload, replyToWeb, ct);
                break;
            }
            default:
                replyToWeb(JsonSerializer.Serialize(new { type = "error", message = $"unknown_type:{msg.Type}" }));
                break;
        }

        _logger.LogInformation(
            "Bridge {Type} requestId={RequestId} elapsed={ElapsedMs}ms",
            msg.Type, requestId, sw.ElapsedMilliseconds);
    }
}
