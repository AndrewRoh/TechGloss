using System.Text.Json;

namespace TechGloss.Core.Messages;

public sealed class WebEnvelope
{
    public required string Type { get; init; }
    public JsonElement Payload { get; init; }
}
