using DownloadManager.Data;
using DownloadManager.Services;
using DownloadManager.ViewModels;
using DownloadManager.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace DownloadManager;

public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        var logDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Logs");

        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(
                    logDirectory,
                    "download-manager-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog();
        });

        services.AddSingleton<HttpClient>();

        var databasePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "DownloadManager",
            "downloads.db");

        Directory.CreateDirectory(
            Path.GetDirectoryName(databasePath)!);

        services.AddDbContextFactory<DownloadDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));

        services.AddSingleton<DownloadService>();
        services.AddSingleton<DownloadRetryService>();
        services.AddSingleton<DownloadRepository>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        Log.Information("Services configured");

        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext =
                scope.ServiceProvider.GetRequiredService<DownloadDbContext>();

            dbContext.Database.EnsureCreated();
        }

        var mainWindow =
            _serviceProvider.GetRequiredService<MainWindow>();

        mainWindow.Show();

        Log.Information("Download Manager started");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Information("Download Manager started");

        var mainWindow =
            _serviceProvider.GetRequiredService<MainWindow>();

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Download Manager stopped");

        _serviceProvider.Dispose();

        Log.CloseAndFlush();

        base.OnExit(e);
    }
}