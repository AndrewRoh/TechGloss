using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TechGloss.Core.Contracts;
using TechGloss.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace TechGloss.Infrastructure.Http;

public sealed class OllamaHttpClient : IOllamaChatClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _opts;

    public OllamaHttpClient(HttpClient http, IOptions<TechGlossOptions> options)
    {
        _http = http;
        _opts = options.Value.Ollama;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt, string userText,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var path = _opts.UseOpenAiCompatiblePath
            ? "/v1/chat/completions"
            : _opts.ChatPath;

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_opts.BaseUrl.TrimEnd('/')}{path}");

        req.Content = JsonContent.Create(new
        {
            model = _opts.Model,
            stream = true,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userText }
            }
        });

        using var resp = await _http.SendAsync(req,
            HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);

            if (string.IsNullOrWhiteSpace(line) || line[0] != '{') continue;

            using var doc = JsonDocument.Parse(line);

            if (doc.RootElement.TryGetProperty("message", out var m)
                && m.TryGetProperty("content", out var c))
            {
                var delta = c.GetString();
                if (!string.IsNullOrEmpty(delta))
                    yield return delta;
            }

            if (doc.RootElement.TryGetProperty("done", out var done)
                && done.GetBoolean())
                break;
        }
    }
}
