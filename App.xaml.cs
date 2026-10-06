using DownloadManager.Services;
using DownloadManager.ViewModels;
using DownloadManager.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Windows;

namespace DownloadManager;

public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();

        services.AddSingleton<HttpClient>();

        services.AddSingleton<DownloadService>();

        services.AddSingleton<DownloadRetryService>();

        services.AddSingleton<MainViewModel>();

        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

        mainWindow.Show();
    }
}