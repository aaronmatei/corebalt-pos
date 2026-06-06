using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Pos.Till.Api;
using Pos.Till.ViewModels;
using Pos.Till.Views;

namespace Pos.Till;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root. The till is a PURE API client: it builds an HttpClient against
            // Pos.Api and never references the domain/application/infrastructure assemblies.
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables(prefix: "POS_TILL_")
                .Build();

            var options = config.GetSection("Till").Get<TillOptions>() ?? new TillOptions();
            var api = new PosApiClient(options);
            var shell = new ShellViewModel(api, options); // starts on the PIN login screen

            desktop.MainWindow = new MainWindow { DataContext = shell };
            desktop.Exit += (_, _) => api.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
