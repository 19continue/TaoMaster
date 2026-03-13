namespace TaoMaster.Core.Services;

public sealed class PackageDownloadService
{
    private readonly HttpClient _httpClient;

    public PackageDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task DownloadAsync(string url, string destinationFile, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)
                                  ?? throw new InvalidOperationException("下载目标目录无效。"));

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var output = File.Create(destinationFile);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await input.CopyToAsync(output, cancellationToken);
    }
}
