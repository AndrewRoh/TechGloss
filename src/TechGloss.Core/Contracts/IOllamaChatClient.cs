namespace TechGloss.Core.Contracts;

public interface IOllamaChatClient
{
    IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt, string userText, CancellationToken ct = default);
}
