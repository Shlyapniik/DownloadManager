using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace DownloadManager.Services;

public class DownloadRetryService
{
    private readonly DownloadService _downloadService;
    private readonly ILogger<DownloadRetryService> _logger;

    public DownloadRetryService(
    DownloadService downloadService,
    ILogger<DownloadRetryService> logger)
    {
        _downloadService = downloadService;
        _logger = logger;
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
            catch (HttpRequestException ex)
            {
                if (attempt >= maxAttempts)
                {
                    _logger.LogError(
                        ex,
                        "Download failed after {MaxAttempts} attempts: {Url}",
                        maxAttempts,
                        url);

                    throw;
                }

                _logger.LogWarning(
                    ex,
                    "Download failed. Retrying attempt {NextAttempt}/{MaxAttempts}: {Url}",
                    attempt + 1,
                    maxAttempts,
                    url);

                onRetry(attempt + 1, maxAttempts);

                await Task.Delay(
                    TimeSpan.FromSeconds(2),
                    cancellationToken);
            }
        }
    }
}