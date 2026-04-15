using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Windows;
using TechGloss.Infrastructure;
using TechGloss.Infrastructure.Options;
using TechGloss.Wpf.Bridge;

namespace TechGloss.Wpf;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => c
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Production.json", optional: true))
            .ConfigureServices((ctx, services) =>
            {
                services.AddTechGlossInfrastructure(ctx.Configuration);
                services.AddSingleton<TranslationOrchestrator>();
                services.AddSingleton<HostBridge>();
                services.AddTransient<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        // GlossaryApi 헬스 체크
        var glossaryBaseUrl = _host.Services
            .GetRequiredService<IOptions<TechGlossOptions>>().Value.GlossaryApi.BaseUrl;
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var health = await http.GetAsync($"{glossaryBaseUrl}/health");
            if (!health.IsSuccessStatusCode)
                MessageBox.Show("Glossary API 서버 응답 없음. 설정을 확인하세요.",
                    "TechGloss", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            MessageBox.Show($"Glossary API({glossaryBaseUrl})에 연결할 수 없습니다.\n서버를 먼저 기동하세요.",
                "TechGloss", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
