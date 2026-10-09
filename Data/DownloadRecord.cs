using DownloadManager.Models;

namespace DownloadManager.Data;

public class DownloadRecord
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public DownloadStatus Status { get; set; }

    public long TotalBytes { get; set; }

    public long DownloadedBytes { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}