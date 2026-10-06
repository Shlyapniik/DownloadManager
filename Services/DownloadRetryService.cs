using System.Net.Http;

namespace DownloadManager.Services;

public class DownloadRetryService
{
    private readonly DownloadService _downloadService;

    public DownloadRetryService(DownloadService downloadService)
    {
        _downloadService = downloadService;
    }

    public async Task DownloadWithRetryAsync(
        string url,
        string filePath,
        IProgress<DownloadProgress> progress,
        Action<int, int> onRetry,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await _downloadService.DownloadAsync(
                    url,
                    filePath,
                    progress,
                    cancellationToken);

                return;
            }
            catch (HttpRequestException)
            {
                if (attempt >= maxAttempts)
                    throw;

                onRetry(attempt + 1, maxAttempts);

                await Task.Delay(
                    TimeSpan.FromSeconds(2),
                    cancellationToken);
            }
        }
    }
}