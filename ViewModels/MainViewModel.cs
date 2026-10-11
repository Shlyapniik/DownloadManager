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

    private readonly Dictionary<DownloadItem, Task> _downloadTasks = new();

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

        StartDownload(download);
    }

    private void StartDownload(
        DownloadItem download)
    {
        var task = StartDownloadAsync(download);

        _downloadTasks[download] = task;

        _ = task.ContinueWith(
            _ =>
            {
                _downloadTasks.Remove(download);
            },
            TaskScheduler.FromCurrentSynchronizationContext());
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
            download.IsDownloading = false;
            download.Status =
                DownloadStatus.Cancelled;

            download.ResetProgress();

            await SaveDownloadAsync(download);

            return;
        }

        try
        {
            // Пользователь мог отменить загрузку,
            // пока она ждала свободный слот.
            if (download.CancellationTokenSource
                .IsCancellationRequested)
            {
                download.Status =
                    DownloadStatus.Cancelled;

                download.IsDownloading = false;

                download.ResetProgress();

                await SaveDownloadAsync(download);

                return;
            }

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

            using var progressSaveCts =
                new CancellationTokenSource();

            var progressSaveTask =
                SaveProgressLoopAsync(
                    download,
                    progressSaveCts.Token);

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
                progressSaveCts.Cancel();

                try
                {
                    await progressSaveTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            download.DownloadedBytes =
                download.TotalBytes > 0
                    ? download.TotalBytes
                    : File.Exists(download.FilePath)
                        ? new FileInfo(download.FilePath).Length
                        : download.DownloadedBytes;

            download.Progress = 100;
            download.Status =
                DownloadStatus.Completed;

            download.IsDownloading = false;
            download.IsPaused = false;
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
            download.IsDownloading = false;
            download.Speed = 0;

            if (download.IsPaused)
            {
                download.Status =
                    DownloadStatus.Paused;

                await SaveDownloadAsync(download);

                return;
            }

            download.Status =
                DownloadStatus.Cancelled;

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

            download.ResetProgress();

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
        DownloadItem download,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(2),
                    cancellationToken);

                await SaveDownloadAsync(download);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
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
        download.Status =
            DownloadStatus.Paused;

        _logger.LogInformation(
            "Download paused: {FileName}",
            download.FileName);

        download.CancellationTokenSource.Cancel();

        if (_downloadTasks.TryGetValue(
                download,
                out var task))
        {
            await task;
        }
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

        StartDownload(download);
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

        _logger.LogInformation(
            "Download cancellation requested: {FileName}",
            download.FileName);

        download.CancellationTokenSource.Cancel();

        if (_downloadTasks.TryGetValue(
                download,
                out var task))
        {
            await task;
        }
        else
        {
            download.Status =
                DownloadStatus.Cancelled;

            download.IsDownloading = false;

            download.ResetProgress();

            await SaveDownloadAsync(download);
        }
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

        StartDownload(download);
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

            download.IsPaused = false;

            download.CancellationTokenSource.Cancel();

            if (_downloadTasks.TryGetValue(
                    download,
                    out var task))
            {
                await task;
            }

            if (File.Exists(download.FilePath))
            {
                try
                {
                    File.Delete(download.FilePath);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Could not delete file: {FilePath}",
                        download.FilePath);
                }
            }

            await _downloadRepository.DeleteAsync(
                download.Id);

            Downloads.Remove(download);

            download.CancellationTokenSource.Dispose();

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

    private string GetUniqueFilePath(
        string directory,
        string fileName)
    {
        var nameWithoutExtension =
            Path.GetFileNameWithoutExtension(fileName);

        var extension =
            Path.GetExtension(fileName);

        var filePath =
            Path.Combine(directory, fileName);

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
}