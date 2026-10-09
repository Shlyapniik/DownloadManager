using CommunityToolkit.Mvvm.ComponentModel;

namespace DownloadManager.Models;

public partial class DownloadItem : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private string url = string.Empty;

    [ObservableProperty]
    private string filePath = string.Empty;

    [ObservableProperty]
    private long totalBytes;

    [ObservableProperty]
    private long downloadedBytes;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private double speed;

    [ObservableProperty]
    private DownloadStatus status = DownloadStatus.Pending;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool isDownloading;

    [ObservableProperty]
    private bool canRetry;

    public DateTime CreatedAt { get; set; }

    public CancellationTokenSource CancellationTokenSource { get; set; } = new();

    public string DownloadedSizeText =>
        FormatBytes(DownloadedBytes);

    public string TotalSizeText =>
        TotalBytes > 0
            ? FormatBytes(TotalBytes)
            : "Unknown";

    public string SpeedText =>
        $"{FormatBytes((long)Speed)}/s";

    public string RemainingTimeText
    {
        get
        {
            if (TotalBytes <= 0 || Speed <= 0)
                return "Unknown";

            var remainingBytes =
                TotalBytes - DownloadedBytes;

            var seconds =
                remainingBytes / Speed;

            return FormatTime(
                TimeSpan.FromSeconds(seconds));
        }
    }

    public string StatusText => Status switch
    {
        DownloadStatus.Pending => "Pending",
        DownloadStatus.Queued => "Queued",
        DownloadStatus.Downloading => "Downloading",
        DownloadStatus.Paused => "Paused",
        DownloadStatus.Completed => "Completed",
        DownloadStatus.Cancelled => "Cancelled",
        DownloadStatus.Failed => "Failed",
        DownloadStatus.Retrying => "Retrying",
        _ => "Unknown"
    };

    partial void OnStatusChanged(DownloadStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnDownloadedBytesChanged(long value)
    {
        OnPropertyChanged(nameof(DownloadedSizeText));
        OnPropertyChanged(nameof(RemainingTimeText));
    }

    partial void OnTotalBytesChanged(long value)
    {
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(RemainingTimeText));
    }

    partial void OnSpeedChanged(double value)
    {
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(RemainingTimeText));
    }

    public void ResetProgress()
    {
        TotalBytes = 0;
        DownloadedBytes = 0;
        Progress = 0;
        Speed = 0;
    }

    private static string FormatBytes(long bytes)
    {
        const double kilobyte = 1024;
        const double megabyte = kilobyte * 1024;
        const double gigabyte = megabyte * 1024;

        if (bytes >= gigabyte)
            return $"{bytes / gigabyte:F2} GB";

        if (bytes >= megabyte)
            return $"{bytes / megabyte:F2} MB";

        if (bytes >= kilobyte)
            return $"{bytes / kilobyte:F2} KB";

        return $"{bytes} B";
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
            return time.ToString(@"h\:mm\:ss");

        if (time.TotalMinutes >= 1)
            return time.ToString(@"m\:ss");

        return $"{Math.Max(0, (int)time.TotalSeconds)} sec";
    }
}