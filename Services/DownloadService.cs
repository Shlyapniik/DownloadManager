
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
        long existingBytes = File.Exists(filePath)
            ? new FileInfo(filePath).Length
            : 0;

        _logger.LogInformation(
            "Starting download: {Url} -> {FilePath}",
            url,
            filePath);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            url);

        if (existingBytes > 0)
        {
            request.Headers.Range =
                new System.Net.Http.Headers.RangeHeaderValue(
                    existingBytes,
                    null);
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        bool appendToFile = false;
        long? expectedTotalBytes = null;

        if (existingBytes > 0 &&
            response.StatusCode == HttpStatusCode.PartialContent)
        {
            var contentRange =
                response.Content.Headers.ContentRange;

            if (contentRange?.From != existingBytes)
            {
                throw new HttpRequestException(
                    "The server returned an unexpected byte range.");
            }

            appendToFile = true;
            expectedTotalBytes = contentRange.Length;

            _logger.LogInformation(
                "Resuming from byte {Offset}",
                existingBytes);
        }
        else if (existingBytes > 0 &&
                 response.StatusCode == HttpStatusCode.OK)
        {
            _logger.LogWarning(
                "Server ignored Range. Restarting from the beginning.");
        }
        else if (existingBytes > 0)
        {
            throw new HttpRequestException(
                $"Unexpected HTTP status for resumed download: {(int)response.StatusCode}.");
        }
        else
        {
            expectedTotalBytes =
                response.Content.Headers.ContentLength;
        }

        var contentLength =
            response.Content.Headers.ContentLength;

        if (appendToFile &&
            contentLength.HasValue &&
            expectedTotalBytes.HasValue &&
            contentLength.Value !=
                expectedTotalBytes.Value - existingBytes)
        {
            throw new HttpRequestException(
                "The server returned an inconsistent Content-Length.");
        }

        await using var inputStream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        await using var outputStream = new FileStream(
            filePath,
            appendToFile ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        var buffer = new byte[81920];

        long downloadedBytes = appendToFile
            ? existingBytes
            : 0;

        long lastMeasuredBytes = downloadedBytes;

        var stopwatch = Stopwatch.StartNew();
        var lastMeasurement = stopwatch.Elapsed;

        var totalBytes = expectedTotalBytes ?? 0;

        while (true)
        {
            var bytesRead = await inputStream.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
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
                    remainingTime = TimeSpan.FromSeconds(
                        Math.Max(
                            0,
                            totalBytes - downloadedBytes) / speed);
                }

                var percentage = totalBytes > 0
                    ? Math.Min(
                        100,
                        (double)downloadedBytes / totalBytes * 100)
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

        await outputStream.FlushAsync(cancellationToken);

        if (expectedTotalBytes.HasValue &&
            downloadedBytes != expectedTotalBytes.Value)
        {
            throw new HttpRequestException(
                $"Incomplete download. Expected {expectedTotalBytes.Value} bytes, " +
                $"but received {downloadedBytes} bytes.");
        }

        progress.Report(
            new DownloadProgress(
                totalBytes,
                downloadedBytes,
                100,
                0,
                TimeSpan.Zero));

        _logger.LogInformation(
            "Download completed: {FilePath} ({Bytes} bytes)",
            filePath,
            downloadedBytes);
    }
}
