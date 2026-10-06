using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DownloadManager.Models;
using DownloadManager.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace DownloadManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DownloadService _downloadService;
    private readonly SemaphoreSlim _downloadSemaphore = new(3);

    [ObservableProperty]
    private string title = "Download Manager";

    [ObservableProperty]
    private string downloadUrl = string.Empty;

    public ObservableCollection<DownloadItem> Downloads { get; } = new();

    private readonly DownloadRetryService _downloadRetryService;

    public MainViewModel(DownloadService downloadService, DownloadRetryService downloadRetryService)
    {
        _downloadService = downloadService;
        _downloadRetryService = downloadRetryService;
    }

    [RelayCommand]
    private async Task AddDownload()
    {
        if (string.IsNullOrWhiteSpace(DownloadUrl))
            return;

        var url = DownloadUrl.Trim();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var fileName = Path.GetFileName(uri.LocalPath);

        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "download";

        var downloadsFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            "Downloads");

        Directory.CreateDirectory(downloadsFolder);

        var filePath = Path.Combine(
            downloadsFolder,
            fileName);

        var download = new DownloadItem
        {
            FileName = fileName,
            Url = url,
            FilePath = filePath,
            Status = "Queued"
        };

        Downloads.Add(download);

        DownloadUrl = string.Empty;

        _ = StartDownloadAsync(download);
    }

    private async Task StartDownloadAsync(DownloadItem download)
    {
        await _downloadSemaphore.WaitAsync(
            download.CancellationTokenSource.Token);

        try
        {
            download.IsDownloading = true;
            download.Status = "Downloading";
            download.CanRetry = false;

            var progress = new Progress<DownloadProgress>(value =>
            {
                download.TotalBytes = value.TotalBytes;
                download.DownloadedBytes = value.DownloadedBytes;
                download.Progress = value.Percentage;
                download.Speed = value.Speed;
            });

            await _downloadRetryService.DownloadWithRetryAsync(
                download.Url,
                download.FilePath,
                progress,
                (attempt, maxAttempts) =>
                {
                    download.Status = $"Retrying ({attempt}/{maxAttempts})...";
                },
                download.CancellationTokenSource.Token);

            download.Status = "Completed";
            download.IsDownloading = false;
            download.CanRetry = false;
        }
        catch (OperationCanceledException)
        {
            if (download.IsPaused)
            {
                download.Status = "Paused";
                return;
            }

            download.IsDownloading = false;
            download.Status = "Cancelled";
            download.ResetProgress();

            if (File.Exists(download.FilePath))
                File.Delete(download.FilePath);
        }
        catch (HttpRequestException ex)
        {
            download.IsDownloading = false;
            download.Status = $"Failed: {ex.Message}";
            download.CanRetry = true;
        }
        catch (Exception ex)
        {
            download.IsDownloading = false;
            download.Status = $"Failed: {ex.Message}";
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    [RelayCommand]
    private void CancelDownload(DownloadItem download)
    {
        if (download.Status == "Completed" ||
            download.Status == "Cancelled" ||
            download.Status == "Failed")
        {
            return;
        }

        download.IsPaused = false;
        download.IsDownloading = false;
        download.Status = "Cancelled";

        download.CancellationTokenSource.Cancel();
    }

    [RelayCommand]
    private void PauseDownload(DownloadItem download)
    {
        if (!download.IsDownloading)
            return;

        download.IsPaused = true;
        download.IsDownloading = false;
        download.Status = "Paused";

        download.CancellationTokenSource.Cancel();
    }

    [RelayCommand]
    private void ResumeDownload(DownloadItem download)
    {
        if (!download.IsPaused)
            return;

        download.IsPaused = false;
        download.Status = "Queued";

        download.CancellationTokenSource.Dispose();
        download.CancellationTokenSource = new CancellationTokenSource();

        _ = StartDownloadAsync(download);
    }

    [RelayCommand]
    private void RetryDownload(DownloadItem download)
    {
        if (!download.CanRetry || download.IsDownloading)
            return;

        download.CancellationTokenSource.Dispose();
        download.CancellationTokenSource = new CancellationTokenSource();

        download.IsPaused = false;
        download.CanRetry = false;
        download.Status = "Queued";

        _ = StartDownloadAsync(download);
    }
}