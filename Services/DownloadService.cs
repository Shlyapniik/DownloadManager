using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;

namespace DownloadManager.Services;

public class DownloadService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DownloadService> _logger;

    public DownloadService(
        HttpClient httpClient,
        ILogger<DownloadService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task DownloadAsync(
        string url,
        string filePath,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting download: {Url} -> {FilePath}",
            url,
            filePath);

        long existingBytes = 0;

        if (File.Exists(filePath))
        {
            existingBytes = new FileInfo(filePath).Length;

            if (existingBytes > 0)
            {
                _logger.LogInformation(
                    "Existing partial file found: {Bytes} bytes",
                    existingBytes);
            }
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            url);

        if (existingBytes > 0)
        {
            _logger.LogInformation(
                 "Resuming download from byte {ExistingBytes}",
                 existingBytes);

            request.Headers.Range =
                new System.Net.Http.Headers.RangeHeaderValue(
                    existingBytes,
                    null);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (existingBytes > 0 &&
            response.StatusCode != HttpStatusCode.PartialContent)
        {
            existingBytes = 0;
        }

        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;

        var totalBytes = contentLength.HasValue
            ? existingBytes + contentLength.Value
            : 0;

        await using var inputStream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        await using var outputStream =
            new FileStream(
                filePath,
                existingBytes > 0
                    ? FileMode.Append
                    : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

        var buffer = new byte[81920];

        long downloadedBytes = existingBytes;

        long lastMeasuredBytes = downloadedBytes;

        var stopwatch = Stopwatch.StartNew();
        var lastMeasurement = stopwatch.Elapsed;

        while (true)
        {
            var bytesRead = await inputStream.ReadAsync(
                buffer,
                cancellationToken);

            if (bytesRead == 0)
                break;

            await outputStream.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);

            downloadedBytes += bytesRead;

            var now = stopwatch.Elapsed;
            var elapsed = now - lastMeasurement;

            if (elapsed.TotalMilliseconds >= 500)
            {
                var bytesSinceLastMeasurement =
                    downloadedBytes - lastMeasuredBytes;

                var speed =
                    bytesSinceLastMeasurement /
                    elapsed.TotalSeconds;

                TimeSpan? remainingTime = null;

                if (totalBytes > 0 && speed > 0)
                {
                    var remainingBytes =
                        totalBytes - downloadedBytes;

                    remainingTime = TimeSpan.FromSeconds(
                        remainingBytes / speed);
                }

                var percentage = totalBytes > 0
                    ? (double)downloadedBytes / totalBytes * 100
                    : 0;

                progress.Report(
                    new DownloadProgress(
                        totalBytes,
                        downloadedBytes,
                        percentage,
                        speed,
                        remainingTime));

                lastMeasuredBytes = downloadedBytes;
                lastMeasurement = now;
            }
        }

        if (totalBytes > 0)
        {
            progress.Report(
                new DownloadProgress(
                    totalBytes,
                    downloadedBytes,
                    100,
                    0,
                    TimeSpan.Zero));
        }

        _logger.LogInformation(
            "Download completed: {FilePath}",
            filePath);
    }
}