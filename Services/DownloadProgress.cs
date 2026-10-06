namespace DownloadManager.Services;

public record DownloadProgress(
    long TotalBytes,
    long DownloadedBytes,
    double Percentage,
    double Speed,
    TimeSpan? RemainingTime);