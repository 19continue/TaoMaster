using System.Text.RegularExpressions;
using TaoMaster.Core.Models;
using TaoMaster.Core.Services;
using TaoMaster.Core.Utilities;

namespace TaoMaster.Core.RemoteSources;

public sealed class ApacheMavenPackageSource
{
    private const string CurrentBaseUrl = "https://downloads.apache.org/maven/maven-3";
    private const string ArchiveBaseUrl = "https://archive.apache.org/dist/maven/maven-3";

    private static readonly Regex VersionLinkRegex = new(@"href=""(?<version>\d+\.\d+\.\d+)/""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly MavenConfigurationService _mavenConfigurationService = new();

    public ApacheMavenPackageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public IReadOnlyList<MavenDownloadSourceConfiguration> GetBuiltInDownloadSources() =>
        _mavenConfigurationService.GetBuiltInDownloadSources();

    public IReadOnlyList<MavenDownloadSourceConfiguration> BuildAvailableDownloadSources(
        IReadOnlyList<MavenDownloadSourceConfiguration>? customSources) =>
        _mavenConfigurationService.BuildAvailableDownloadSources(customSources);

    public async Task<IReadOnlyList<string>> GetCurrentVersionsAsync(CancellationToken cancellationToken)
    {
        var html = await _httpClient.GetStringAsync($"{CurrentBaseUrl}/", cancellationToken);

        return VersionLinkRegex.Matches(html)
            .Select(match => match.Groups["version"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x, VersionStringComparer.Instance)
            .ToList();
    }

    public async Task<RemotePackageDescriptor> ResolveAsync(
        string? version,
        CancellationToken cancellationToken,
        string? preferredSourceId = null,
        IReadOnlyList<MavenDownloadSourceConfiguration>? customSources = null)
    {
        var resolvedVersion = string.IsNullOrWhiteSpace(version) || version.Equals("latest", StringComparison.OrdinalIgnoreCase)
            ? (await GetCurrentVersionsAsync(cancellationToken)).FirstOrDefault()
            : version.Trim();

        if (string.IsNullOrWhiteSpace(resolvedVersion))
        {
            throw new InvalidOperationException("Could not determine the Maven version to install.");
        }

        var sources = BuildAvailableDownloadSources(customSources);
        var preferredSource = ResolvePreferredSource(sources, preferredSourceId);
        var fileName = $"apache-maven-{resolvedVersion}-bin.zip";

        foreach (var source in BuildSourceOrder(sources, preferredSource))
        {
            var descriptor = await TryResolveFromSourceAsync(source, resolvedVersion, fileName, cancellationToken);
            if (descriptor is not null)
            {
                return descriptor;
            }
        }

        throw new InvalidOperationException($"Could not find an Apache Maven {resolvedVersion} ZIP package from the configured download sources.");
    }

    private async Task<RemotePackageDescriptor?> TryResolveFromSourceAsync(
        MavenDownloadSourceConfiguration source,
        string version,
        string fileName,
        CancellationToken cancellationToken)
    {
        var baseUrl = source.BaseUrl.TrimEnd('/');
        var downloadUrl = $"{baseUrl}/{version}/binaries/{fileName}";
        if (!await UrlExistsAsync(downloadUrl, cancellationToken))
        {
            return null;
        }

        var checksumUrl = $"{downloadUrl}.sha512";
        var checksum = await ReadShaChecksumAsync(checksumUrl, cancellationToken);

        return new RemotePackageDescriptor(
            Kind: ToolchainKind.Maven,
            Provider: source.Id,
            DisplayName: $"Apache Maven {version}",
            Version: version,
            DownloadUrl: downloadUrl,
            FileName: fileName,
            Checksum: checksum,
            ChecksumAlgorithm: "SHA512",
            SuggestedInstallDirectoryName: $"apache-maven-{version}",
            Architecture: "noarch");
    }

    private static IReadOnlyList<MavenDownloadSourceConfiguration> BuildSourceOrder(
        IReadOnlyList<MavenDownloadSourceConfiguration> sources,
        MavenDownloadSourceConfiguration preferredSource)
    {
        var ordered = new List<MavenDownloadSourceConfiguration> { preferredSource };

        void AppendIfMissing(string id)
        {
            var source = sources.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (source is not null && ordered.All(item => !item.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase)))
            {
                ordered.Add(source);
            }
        }

        AppendIfMissing("apache-official");
        AppendIfMissing("apache-archive");

        foreach (var source in sources)
        {
            if (ordered.All(item => !item.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase)))
            {
                ordered.Add(source);
            }
        }

        return ordered;
    }

    private static MavenDownloadSourceConfiguration ResolvePreferredSource(
        IReadOnlyList<MavenDownloadSourceConfiguration> sources,
        string? preferredSourceId)
    {
        if (!string.IsNullOrWhiteSpace(preferredSourceId))
        {
            var matched = sources.FirstOrDefault(source =>
                source.Id.Equals(preferredSourceId, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                return matched;
            }
        }

        return sources.FirstOrDefault(source => source.Id.Equals("apache-official", StringComparison.OrdinalIgnoreCase))
               ?? sources.First();
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
               ?? throw new InvalidOperationException($"Could not read checksum from {url}");
    }
}
