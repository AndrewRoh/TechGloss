using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using TechGloss.Core.Contracts;
using TechGloss.Infrastructure.Http;
using TechGloss.Infrastructure.Options;

namespace TechGloss.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTechGlossInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<TechGlossOptions>(config.GetSection("TechGloss"));

        var opts = config.GetSection("TechGloss").Get<TechGlossOptions>() ?? new();
        var allowedHosts = new[]
        {
            new Uri(opts.Ollama.BaseUrl).Host,
            new Uri(opts.GlossaryApi.BaseUrl).Host,
            "localhost", "127.0.0.1"
        };

        services
            .AddHttpClient<IOllamaChatClient, OllamaHttpClient>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(opts.Ollama.TimeoutSeconds);
            })
            .AddHttpMessageHandler(() => new AllowedHostsHandler(allowedHosts))
            .AddTransientHttpErrorPolicy(p =>
                p.WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i))));

        services
            .AddHttpClient<IGlossaryClient, GlossaryHttpClient>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler(() => new AllowedHostsHandler(allowedHosts));

        return services;
    }
}
