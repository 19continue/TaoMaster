using TaoMaster.Core.Models;

namespace TaoMaster.Core.Services;

public sealed class PackageDownloadService
{
    private readonly HttpClient _httpClient;

    public PackageDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task DownloadAsync(
        string url,
        string destinationFile,
        CancellationToken cancellationToken,
        IProgress<PackageInstallProgress>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)
                                  ?? throw new InvalidOperationException("下载目标目录无效。"));

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var output = File.Create(destinationFile);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        var totalBytes = response.Content.Headers.ContentLength;
        var buffer = new byte[81920];
        long bytesReceived = 0;

        while (true)
        {
            var bytesRead = await input.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesReceived += bytesRead;
            progress?.Report(new PackageInstallProgress(PackageInstallStage.Downloading, bytesReceived, totalBytes));
        }
    }
}
