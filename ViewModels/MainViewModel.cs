using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DownloadManager.Models;
using DownloadManager.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;

namespace DownloadManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DownloadService _downloadService;

    [ObservableProperty]
    private string title = "Download Manager";

    [ObservableProperty]
    private string downloadUrl = string.Empty;

    public ObservableCollection<DownloadItem> Downloads { get; } = new();

    public MainViewModel(DownloadService downloadService)
    {
        _downloadService = downloadService;
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
            Status = "Downloading",
            IsDownloading = true
        };

        Downloads.Add(download);

        DownloadUrl = string.Empty;

        var progress = new Progress<DownloadProgress>(value =>
        {
            download.TotalBytes = value.TotalBytes;
            download.DownloadedBytes = value.DownloadedBytes;
            download.Progress = value.Percentage;
            download.Speed = value.Speed;
        });

        try
        {
            await _downloadService.DownloadAsync(
                url,
                filePath,
                progress,
                download.CancellationTokenSource.Token);

            download.Status = "Completed";
            download.IsDownloading = false;
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

            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (HttpRequestException ex)
        {
            download.IsDownloading = false;
            download.Status = $"Failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            download.IsDownloading = false;
            download.Status = $"Failed: {ex.Message}";
        }
        finally
        {
            download.CancellationTokenSource.Dispose();
        }
    }

    [RelayCommand]
    private void CancelDownload(DownloadItem download)
    {
        if (download.Status != "Downloading")
            return;

        if (!download.IsDownloading)
            return;

        download.Status = "Cancelling...";

        download.CancellationTokenSource.Cancel();
    }

    [RelayCommand]
    private void PauseDownload(DownloadItem download)
    {
        if (download.Status != "Downloading")
            return;

        download.IsPaused = true;
        download.IsDownloading = false;
        download.Status = "Paused";

        download.CancellationTokenSource.Cancel();
    }

    [RelayCommand]
    private async Task ResumeDownload(DownloadItem download)
    {
        if (!download.IsPaused || download.IsDownloading)
            return;

        download.IsPaused = false;
        download.IsDownloading = true;
        download.Status = "Downloading";

        download.CancellationTokenSource.Dispose();
        download.CancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<DownloadProgress>(value =>
        {
            download.TotalBytes = value.TotalBytes;
            download.DownloadedBytes = value.DownloadedBytes;
            download.Progress = value.Percentage;
            download.Speed = value.Speed;
        });

        try
        {
            await _downloadService.DownloadAsync(
                download.Url,
                download.FilePath,
                progress,
                download.CancellationTokenSource.Token);

            download.Status = "Completed";
            download.IsDownloading = false;
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

            if (File.Exists(download.FilePath))
                File.Delete(download.FilePath);
        }
        catch (HttpRequestException ex)
        {
            download.IsDownloading = false;
            download.Status = $"Failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            download.IsDownloading = false;
            download.Status = $"Failed: {ex.Message}";
        }
    }
}