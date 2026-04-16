using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TechGloss.Wpf.Bridge;

namespace TechGloss.Wpf;

public partial class MainWindow : Window
{
    private readonly HostBridge _bridge;

    public MainWindow(HostBridge bridge)
    {
        InitializeComponent();
        _bridge = bridge;
    }

    private async void OnWebViewLoaded(object sender, RoutedEventArgs e)
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TechGloss", "WebView2");

        var env = await CoreWebView2Environment.CreateAsync(
            userDataFolder: userDataFolder);
        await webView.EnsureCoreWebView2Async(env);

        var core = webView.CoreWebView2;

        var distPath = Path.Combine(AppContext.BaseDirectory, "Web", "dist");
        core.SetVirtualHostNameToFolderMapping(
            "app.local",
            distPath,
            CoreWebView2HostResourceAccessKind.Allow);

        core.WebMessageReceived += async (_, args) =>
        {
            var json = args.WebMessageAsJson;
            try
            {
                await _bridge.HandleWebMessageAsync(json, replyJson =>
                    core.PostWebMessageAsJson(replyJson));
            }
            catch (Exception ex)
            {
                core.PostWebMessageAsJson(
                    System.Text.Json.JsonSerializer.Serialize(
                        new { type = "translation.error", payload = ex.Message }));
            }
        };

#if DEBUG
        core.OpenDevToolsWindow();
#endif

        core.Navigate("https://app.local/index.html");
    }
}
