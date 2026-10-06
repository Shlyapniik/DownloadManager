using CommunityToolkit.Mvvm.ComponentModel;

namespace DownloadManager.Models;

public partial class DownloadItem : ObservableObject
{
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
    private string status = "Pending";

    public CancellationTokenSource CancellationTokenSource { get; } = new();
}