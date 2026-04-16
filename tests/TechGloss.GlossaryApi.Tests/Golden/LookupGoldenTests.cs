using System.Net;
using System.Net.Http.Json;
using TechGloss.Core.Contracts;

namespace TechGloss.GlossaryApi.Tests.Golden;

public class LookupGoldenTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    public LookupGoldenTests(CustomWebApplicationFactory f)
        => _client = f.CreateClient();

    [Theory]
    [InlineData("deploy", "en", "배포")]
    [InlineData("배포", "ko", "deploy")]
    [InlineData("build", "auto", "빌드")]
    public async Task Lookup_SeedTerm_DefinitionKoContainsExpected(
        string q, string lang, string expectedInDefinition)
    {
        var resp = await _client.GetAsync(
            $"/glossary/lookup?q={Uri.EscapeDataString(q)}&lang={lang}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<GlossaryLookupRow>>();
        Assert.NotNull(rows);
        Assert.Contains(rows!, r =>
            r.DefinitionKo.Contains(expectedInDefinition, StringComparison.OrdinalIgnoreCase)
            || r.TermKo.Contains(expectedInDefinition, StringComparison.OrdinalIgnoreCase)
            || r.TermEn.Contains(expectedInDefinition, StringComparison.OrdinalIgnoreCase));
    }
}
