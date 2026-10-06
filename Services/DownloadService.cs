using System.Diagnostics;
using System.IO;
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
        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        await using var inputStream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        await using var outputStream =
            new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

        var buffer = new byte[81920];
        long downloadedBytes = 0;

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
                ? downloadedBytes / elapsedSeconds
                : 0;

            var percentage = totalBytes.HasValue
                ? (double)downloadedBytes / totalBytes.Value * 100
                : 0;

            progress.Report(
                new DownloadProgress(
                    totalBytes ?? 0,
                    downloadedBytes,
                    percentage,
                    speed));
        }
    }
}