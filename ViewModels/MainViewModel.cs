using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DownloadManager.Data;
using DownloadManager.Models;
using DownloadManager.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;

namespace DownloadManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DownloadService _downloadService;
    private readonly DownloadRetryService _downloadRetryService;
    private readonly DownloadRepository _downloadRepository;
    private readonly SemaphoreSlim _downloadSemaphore = new(3);
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private string title = "Download Manager";

    [ObservableProperty]
    private string downloadUrl = string.Empty;

    public ObservableCollection<DownloadItem> Downloads { get; } = new();

    public MainViewModel(
        DownloadService downloadService,
        DownloadRetryService downloadRetryService,
        DownloadRepository downloadRepository,
        ILogger<MainViewModel> logger)
    {
        _downloadService = downloadService;
        _downloadRetryService = downloadRetryService;
        _downloadRepository = downloadRepository;
        _logger = logger;

        _ = LoadDownloadsAsync();
    }

    private async Task LoadDownloadsAsync()
    {
        try
        {
            var records =
                await _downloadRepository.GetAllAsync();

            foreach (var record in records)
            {
                var status = record.Status;

                if (status == DownloadStatus.Downloading ||
                    status == DownloadStatus.Queued ||
                    status == DownloadStatus.Retrying)
                {
                    status = DownloadStatus.Paused;
                }

                var downloadedBytes = record.DownloadedBytes;

                if (File.Exists(record.FilePath))
                {
                    downloadedBytes =
                        new FileInfo(record.FilePath).Length;
                }

                var download = new DownloadItem
                {
                    Id = record.Id,
                    FileName = record.FileName,
                    Url = record.Url,
                    FilePath = record.FilePath,
                    Status = status,
                    TotalBytes = record.TotalBytes,
                    DownloadedBytes = downloadedBytes,
                    ErrorMessage = record.ErrorMessage,
                    CreatedAt = record.CreatedAt,
                    Progress = record.TotalBytes > 0
                        ? (double)downloadedBytes /
                          record.TotalBytes * 100
                        : 0,
                    CanRetry = status == DownloadStatus.Failed
                };

                Downloads.Add(download);
            }

            _logger.LogInformation(
                "Loaded {Count} downloads from database",
                records.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load downloads from database");
        }
    }


    [RelayCommand]
    private async Task AddDownload()
    {
        if (string.IsNullOrWhiteSpace(DownloadUrl))
            return;

        var url = DownloadUrl.Trim();

        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri) ||
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

        var filePath = GetUniqueFilePath(
            downloadsFolder,
            fileName);

        var download = new DownloadItem
        {
            FileName = Path.GetFileName(filePath),
            Url = url,
            FilePath = filePath,
            Status = DownloadStatus.Queued,
            CreatedAt = DateTime.UtcNow
        };

        var record = new DownloadRecord
        {
            FileName = download.FileName,
            Url = download.Url,
            FilePath = download.FilePath,
            Status = download.Status,
            CreatedAt = download.CreatedAt
        };

        await _downloadRepository.AddAsync(record);

        download.Id = record.Id;

        Downloads.Add(download);

        DownloadUrl = string.Empty;

        _logger.LogInformation(
            "Download added: {Url} -> {FilePath}",
            url,
            filePath);

        _ = StartDownloadAsync(download);
    }

    private string GetUniqueFilePath(
        string directory,
        string fileName)
    {
        var nameWithoutExtension =
            Path.GetFileNameWithoutExtension(fileName);

        var extension =
            Path.GetExtension(fileName);

        var filePath = Path.Combine(directory, fileName);

        var counter = 1;

        while (
            File.Exists(filePath) ||
            Downloads.Any(d =>
                string.Equals(
                    d.FilePath,
                    filePath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            var uniqueFileName =
                $"{nameWithoutExtension} ({counter}){extension}";

            filePath = Path.Combine(
                directory,
                uniqueFileName);

            counter++;
        }

        return filePath;
    }


    private async Task StartDownloadAsync(
        DownloadItem download)
    {
        try
        {
            await _downloadSemaphore.WaitAsync(
                download.CancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            download.IsDownloading = true;
            download.Status =
                DownloadStatus.Downloading;

            await SaveDownloadAsync(download);

            var progress =
                new Progress<DownloadProgress>(value =>
                {
                    download.TotalBytes =
                        value.TotalBytes;

                    download.DownloadedBytes =
                        value.DownloadedBytes;

                    download.Progress =
                        value.Percentage;

                    download.Speed =
                        value.Speed;
                });

            var progressSaveTask =
                SaveProgressLoopAsync(download);

            try
            {
                await _downloadRetryService
                    .DownloadWithRetryAsync(
                        download.Url,
                        download.FilePath,
                        progress,
                        (attempt, maxAttempts) =>
                        {
                            download.Status =
                                DownloadStatus.Retrying;
                        },
                        download.CancellationTokenSource.Token);
            }
            finally
            {
                await progressSaveTask;
            }

            download.Status =
                DownloadStatus.Completed;

            download.IsDownloading = false;
            download.CanRetry = false;
            download.ErrorMessage = string.Empty;
            download.Speed = 0;

            await SaveDownloadAsync(download);

            _logger.LogInformation(
                "Download completed: {FileName}",
                download.FileName);
        }
        catch (OperationCanceledException)
        {
            if (download.IsPaused)
            {
                download.Status =
                    DownloadStatus.Paused;

                download.IsDownloading = false;
                download.Speed = 0;

                await SaveDownloadAsync(download);

                return;
            }

            download.IsDownloading = false;
            download.Status =
                DownloadStatus.Cancelled;

            download.Speed = 0;

            if (File.Exists(download.FilePath))
            {
                try
                {
                    File.Delete(download.FilePath);
                }
                catch (IOException)
                {
                    _logger.LogWarning(
                        "Could not delete cancelled file: {FilePath}",
                        download.FilePath);
                }
            }

            await SaveDownloadAsync(download);
        }
        catch (HttpRequestException ex)
        {
            download.IsDownloading = false;
            download.Status =
                DownloadStatus.Failed;

            download.ErrorMessage = ex.Message;
            download.CanRetry = true;
            download.Speed = 0;

            await SaveDownloadAsync(download);

            _logger.LogError(
                ex,
                "Download failed: {FileName}",
                download.FileName);
        }
        catch (Exception ex)
        {
            download.IsDownloading = false;
            download.Status =
                DownloadStatus.Failed;

            download.ErrorMessage = ex.Message;
            download.CanRetry = true;
            download.Speed = 0;

            await SaveDownloadAsync(download);

            _logger.LogError(
                ex,
                "Unexpected download error: {FileName}",
                download.FileName);
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    private async Task SaveProgressLoopAsync(
        DownloadItem download)
    {
        try
        {
            while (!download.CancellationTokenSource
                       .Token
                       .IsCancellationRequested)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(2),
                    download.CancellationTokenSource.Token);

                if (download.CancellationTokenSource
                    .Token
                    .IsCancellationRequested)
                {
                    break;
                }

                await SaveDownloadAsync(download);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to save download progress: {FileName}",
                download.FileName);
        }
    }

    private async Task SaveDownloadAsync(
        DownloadItem download)
    {
        var record = new DownloadRecord
        {
            Id = download.Id,
            FileName = download.FileName,
            Url = download.Url,
            FilePath = download.FilePath,
            Status = download.Status,
            TotalBytes = download.TotalBytes,
            DownloadedBytes = download.DownloadedBytes,
            ErrorMessage = download.ErrorMessage,
            CreatedAt = download.CreatedAt
        };

        await _downloadRepository.UpdateAsync(record);
    }

    [RelayCommand]
    private async Task PauseDownload(
        DownloadItem download)
    {
        if (download.Status !=
            DownloadStatus.Downloading)
        {
            return;
        }

        download.IsPaused = true;
        download.IsDownloading = false;
        download.Status =
            DownloadStatus.Paused;

        _logger.LogInformation(
            "Download paused: {FileName}",
            download.FileName);

        download.CancellationTokenSource.Cancel();

        await SaveDownloadAsync(download);
    }

    [RelayCommand]
    private async Task ResumeDownload(
        DownloadItem download)
    {
        if (download.Status !=
            DownloadStatus.Paused)
        {
            return;
        }

        download.IsPaused = false;
        download.Status =
            DownloadStatus.Queued;

        download.CancellationTokenSource.Dispose();

        download.CancellationTokenSource =
            new CancellationTokenSource();

        _logger.LogInformation(
            "Download resumed: {FileName}",
            download.FileName);

        await SaveDownloadAsync(download);

        _ = StartDownloadAsync(download);
    }

    [RelayCommand]
    private async Task CancelDownload(
        DownloadItem download)
    {
        if (download.Status ==
                DownloadStatus.Completed ||
            download.Status ==
                DownloadStatus.Cancelled)
        {
            return;
        }

        download.IsPaused = false;
        download.IsDownloading = false;
        download.Status =
            DownloadStatus.Cancelled;

        _logger.LogInformation(
            "Download cancelled: {FileName}",
            download.FileName);

        download.CancellationTokenSource.Cancel();

        download.ResetProgress();

        await SaveDownloadAsync(download);
    }

    [RelayCommand]
    private async Task RetryDownload(
        DownloadItem download)
    {
        if (!download.CanRetry ||
            download.IsDownloading)
        {
            return;
        }

        download.CancellationTokenSource.Dispose();

        download.CancellationTokenSource =
            new CancellationTokenSource();

        download.IsPaused = false;
        download.CanRetry = false;
        download.ErrorMessage = string.Empty;
        download.Status =
            DownloadStatus.Queued;

        _logger.LogInformation(
            "Manual retry requested: {FileName}",
            download.FileName);

        await SaveDownloadAsync(download);

        _ = StartDownloadAsync(download);
    }

    [RelayCommand]
    private async Task DeleteDownload(
        DownloadItem download)
    {
        try
        {
            _logger.LogInformation(
                "Deleting download: {FileName}",
                download.FileName);

            if (download.IsDownloading ||
                download.Status == DownloadStatus.Downloading ||
                download.Status == DownloadStatus.Queued ||
                download.Status == DownloadStatus.Retrying)
            {
                download.IsPaused = false;
                download.CancellationTokenSource.Cancel();
            }

            await _downloadRepository.DeleteAsync(
                download.Id);

            if (File.Exists(download.FilePath))
            {
                try
                {
                    File.Delete(download.FilePath);
                }
                catch (IOException)
                {
                    _logger.LogWarning(
                        "Could not delete file: {FilePath}",
                        download.FilePath);
                }
            }

            Downloads.Remove(download);

            _logger.LogInformation(
                "Download deleted: {FileName}",
                download.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete download: {FileName}",
                download.FileName);
        }
    }
}