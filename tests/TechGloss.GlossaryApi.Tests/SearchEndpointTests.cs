using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TechGloss.Core.Contracts;

namespace TechGloss.GlossaryApi.Tests;

public class SearchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public SearchEndpointTests(WebApplicationFactory<Program> f)
        => _client = f.CreateClient();

    [Fact]
    public async Task Search_WithQuery_ReturnsRows()
    {
        var req = new GlossarySearchRequest
        {
            QueryText  = "deploy software",
            SourceLang = "en",
            TargetLang = "ko",
            TopK       = 5
        };
        var resp = await _client.PostAsJsonAsync("/glossary/search", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<GlossarySearchRow>>();
        Assert.NotNull(rows);
    }
}
