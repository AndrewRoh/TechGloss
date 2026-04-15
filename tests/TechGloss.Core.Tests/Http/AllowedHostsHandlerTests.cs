using System.Net;
using TechGloss.Infrastructure.Http;

namespace TechGloss.Core.Tests.Http;

public class AllowedHostsHandlerTests
{
    [Fact]
    public async Task AllowedHost_Passes()
    {
        var handler = new AllowedHostsHandler(new[] { "172.20.64.76", "127.0.0.1" })
        {
            InnerHandler = new TestInnerHandler()
        };
        var client = new HttpClient(handler);
        var response = await client.GetAsync("http://172.20.64.76:11434/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DisallowedHost_Throws()
    {
        var handler = new AllowedHostsHandler(new[] { "172.20.64.76" })
        {
            InnerHandler = new TestInnerHandler()
        };
        var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync("http://evil.example.com/steal"));
    }

    private sealed class TestInnerHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
