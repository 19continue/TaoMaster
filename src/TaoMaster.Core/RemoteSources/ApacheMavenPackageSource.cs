using System.Text.RegularExpressions;
using TaoMaster.Core.Models;
using TaoMaster.Core.Utilities;

namespace TaoMaster.Core.RemoteSources;

public sealed class ApacheMavenPackageSource
{
    private const string CurrentBaseUrl = "https://downloads.apache.org/maven/maven-3";
    private const string ArchiveBaseUrl = "https://archive.apache.org/dist/maven/maven-3";

    private static readonly Regex VersionLinkRegex = new(@"href=""(?<version>\d+\.\d+\.\d+)/""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;

    public ApacheMavenPackageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<string>> GetCurrentVersionsAsync(CancellationToken cancellationToken)
    {
        var html = await _httpClient.GetStringAsync($"{CurrentBaseUrl}/", cancellationToken);

        return VersionLinkRegex.Matches(html)
            .Select(match => match.Groups["version"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x, VersionStringComparer.Instance)
            .ToList();
    }

    public async Task<RemotePackageDescriptor> ResolveAsync(string? version, CancellationToken cancellationToken)
    {
        var resolvedVersion = string.IsNullOrWhiteSpace(version) || version.Equals("latest", StringComparison.OrdinalIgnoreCase)
            ? (await GetCurrentVersionsAsync(cancellationToken)).FirstOrDefault()
            : version.Trim();

        if (string.IsNullOrWhiteSpace(resolvedVersion))
        {
            throw new InvalidOperationException("无法确定要安装的 Maven 版本。");
        }

        var fileName = $"apache-maven-{resolvedVersion}-bin.zip";
        var currentDownloadUrl = $"{CurrentBaseUrl}/{resolvedVersion}/binaries/{fileName}";
        var currentChecksumUrl = $"{currentDownloadUrl}.sha512";

        if (await UrlExistsAsync(currentDownloadUrl, cancellationToken))
        {
            var checksum = await ReadShaChecksumAsync(currentChecksumUrl, cancellationToken);

            return new RemotePackageDescriptor(
                Kind: ToolchainKind.Maven,
                Provider: "apache",
                DisplayName: $"Apache Maven {resolvedVersion}",
                Version: resolvedVersion,
                DownloadUrl: currentDownloadUrl,
                FileName: fileName,
                Checksum: checksum,
                ChecksumAlgorithm: "SHA512",
                SuggestedInstallDirectoryName: $"apache-maven-{resolvedVersion}",
                Architecture: "noarch");
        }

        var archiveDownloadUrl = $"{ArchiveBaseUrl}/{resolvedVersion}/binaries/{fileName}";
        if (!await UrlExistsAsync(archiveDownloadUrl, cancellationToken))
        {
            throw new InvalidOperationException($"找不到 Apache Maven {resolvedVersion} 的官方 ZIP 包。");
        }

        var archiveChecksumUrl = $"{archiveDownloadUrl}.sha512";
        var archiveChecksum = await ReadShaChecksumAsync(archiveChecksumUrl, cancellationToken);

        return new RemotePackageDescriptor(
            Kind: ToolchainKind.Maven,
            Provider: "apache",
            DisplayName: $"Apache Maven {resolvedVersion}",
            Version: resolvedVersion,
            DownloadUrl: archiveDownloadUrl,
            FileName: fileName,
            Checksum: archiveChecksum,
            ChecksumAlgorithm: "SHA512",
            SuggestedInstallDirectoryName: $"apache-maven-{resolvedVersion}",
            Architecture: "noarch");
    }

    private async Task<bool> UrlExistsAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        return response.IsSuccessStatusCode;
    }

    private async Task<string> ReadShaChecksumAsync(string url, CancellationToken cancellationToken)
    {
        var content = await _httpClient.GetStringAsync(url, cancellationToken);
        var token = content
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return token?.Trim()
               ?? throw new InvalidOperationException($"无法读取校验值: {url}");
    }
}
