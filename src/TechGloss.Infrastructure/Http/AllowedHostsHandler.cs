namespace TechGloss.Infrastructure.Http;

public sealed class AllowedHostsHandler : DelegatingHandler
{
    private readonly HashSet<string> _allowedHosts;

    public AllowedHostsHandler(IEnumerable<string> allowedHosts)
        => _allowedHosts = new HashSet<string>(allowedHosts, StringComparer.OrdinalIgnoreCase);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var host = request.RequestUri?.Host
            ?? throw new InvalidOperationException("Request URI is null");

        if (!_allowedHosts.Contains(host))
            throw new InvalidOperationException($"Host not in allow-list: {host}");

        return base.SendAsync(request, ct);
    }
}
