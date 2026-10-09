namespace DownloadManager.Models;

public enum DownloadStatus
{
    Pending,
    Queued,
    Downloading,
    Paused,
    Completed,
    Cancelled,
    Failed,
    Retrying
}