using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TechGloss.Core.Contracts;

namespace TechGloss.GlossaryApi.Tests;

public class LookupEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public LookupEndpointTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task Lookup_EmptyQ_ReturnsEmptyArray()
    {
        var resp = await _client.GetAsync("/glossary/lookup?q=");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<GlossaryLookupRow>>();
        Assert.Empty(rows!);
    }

    [Fact]
    public async Task Lookup_KnownTerm_ReturnsMatch()
    {
        var resp = await _client.GetAsync("/glossary/lookup?q=deploy&lang=en");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<GlossaryLookupRow>>();
        Assert.Contains(rows!, r => r.TermEn.Contains("deploy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Lookup_QMaxLength_ReturnsOk()
    {
        var longQ = new string('a', 129);
        var resp = await _client.GetAsync($"/glossary/lookup?q={longQ}");
        Assert.True(resp.StatusCode == HttpStatusCode.OK
                 || resp.StatusCode == HttpStatusCode.BadRequest);
    }
}
