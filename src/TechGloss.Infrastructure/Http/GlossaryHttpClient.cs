using System.Net.Http.Json;
using TechGloss.Core.Contracts;
using TechGloss.Core.Models;
using TechGloss.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace TechGloss.Infrastructure.Http;

public sealed class GlossaryHttpClient : IGlossaryClient
{
    private readonly HttpClient _http;
    private readonly GlossaryApiOptions _opts;

    public GlossaryHttpClient(HttpClient http, IOptions<TechGlossOptions> options)
    {
        _http = http;
        _opts = options.Value.GlossaryApi;
    }

    public async Task<IReadOnlyList<GlossarySearchRow>> SearchAsync(
        GlossarySearchRequest request, CancellationToken ct = default)
    {
        var result = await _http.PostAsJsonAsync(
            $"{_opts.BaseUrl}/glossary/search", request, ct);
        result.EnsureSuccessStatusCode();
        return await result.Content.ReadFromJsonAsync<List<GlossarySearchRow>>(ct)
               ?? new List<GlossarySearchRow>();
    }

    public async Task<IReadOnlyList<GlossaryLookupRow>> LookupAsync(
        string q, string lang = "auto", int limit = 20,
        Guid? categoryId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(q)) return Array.Empty<GlossaryLookupRow>();

        var url = $"{_opts.BaseUrl}/glossary/lookup?q={Uri.EscapeDataString(q)}" +
                  $"&lang={lang}&limit={limit}" +
                  (categoryId.HasValue ? $"&category_id={categoryId}" : "");
        var result = await _http.GetFromJsonAsync<List<GlossaryLookupRow>>(url, ct);
        return result ?? new List<GlossaryLookupRow>();
    }

    public async Task UpsertAsync(GlossaryEntry entry, CancellationToken ct = default)
    {
        var result = await _http.PostAsJsonAsync($"{_opts.BaseUrl}/glossary/upsert", entry, ct);
        result.EnsureSuccessStatusCode();
    }

    public async Task PublishAsync(Guid entryId, CancellationToken ct = default)
    {
        var result = await _http.PostAsJsonAsync(
            $"{_opts.BaseUrl}/glossary/publish", new { entry_id = entryId }, ct);
        result.EnsureSuccessStatusCode();
    }
}
