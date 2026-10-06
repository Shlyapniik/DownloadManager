using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;

namespace DownloadManager.Services;

public class DownloadService
{
    private readonly HttpClient _httpClient;

    public DownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task DownloadAsync(
        string url,
        string filePath,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        long existingBytes = 0;

        if (File.Exists(filePath))
        {
            existingBytes = new FileInfo(filePath).Length;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            url);

        if (existingBytes > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(
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

        var stopwatch = Stopwatch.StartNew();

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

            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;

            var speed = elapsedSeconds > 0
                ? (downloadedBytes - existingBytes) / elapsedSeconds
                : 0;

            var percentage = totalBytes > 0
                ? (double)downloadedBytes / totalBytes * 100
                : 0;

            progress.Report(
                new DownloadProgress(
                    totalBytes,
                    downloadedBytes,
                    percentage,
                    speed));
        }
    }
}