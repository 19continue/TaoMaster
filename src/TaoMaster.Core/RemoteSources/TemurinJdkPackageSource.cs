using System.Text.Json;
using System.Text.Json.Serialization;
using TaoMaster.Core.Models;

namespace TaoMaster.Core.RemoteSources;

public sealed class TemurinJdkPackageSource
{
    private static readonly IReadOnlyList<int> FallbackFeatureReleases =
    [
        25, 24, 23, 22, 21, 20, 19, 18, 17, 11, 8
    ];

    private readonly HttpClient _httpClient;

    public TemurinJdkPackageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<int>> GetAvailableFeatureReleasesAsync(CancellationToken cancellationToken)
    {
        using var stream = await _httpClient.GetStreamAsync(
            "https://api.adoptium.net/v3/info/available_releases",
            cancellationToken);

        var payload = await JsonSerializer.DeserializeAsync<AvailableReleasesResponse>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("无法读取 Temurin 可用版本列表。");

        var releases = payload.AvailableReleases
            .Where(release => release > 0)
            .OrderByDescending(release => release)
            .ToList();

        return releases.Count > 0
            ? releases
            : FallbackFeatureReleases;
    }

    public async Task<IReadOnlyList<RemotePackageDescriptor>> GetLatestPackagesByFeatureAsync(
        string architecture,
        CancellationToken cancellationToken)
    {
        var releases = await GetAvailableFeatureReleasesAsync(cancellationToken);
        var tasks = releases.Select(async feature =>
        {
            try
            {
                return await ResolveLatestAsync(feature, architecture, cancellationToken);
            }
            catch
            {
                return null;
            }
        });
        var packages = await Task.WhenAll(tasks);

        return packages
            .Where(package => package is not null)
            .Select(package => package!)
            .OrderByDescending(package => GetFeatureVersion(package.Version))
            .ThenByDescending(package => package.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RemotePackageDescriptor> ResolveLatestAsync(
        int featureVersion,
        string architecture,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://api.adoptium.net/v3/assets/latest/{featureVersion}/hotspot?architecture={architecture}&heap_size=normal&image_type=jdk&os=windows&vendor=eclipse";

        using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<List<TemurinAsset>>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("无法读取 Temurin 包元数据。");

        var asset = payload.FirstOrDefault()
            ?? throw new InvalidOperationException($"Temurin 没有返回可用于 Windows {architecture} 的 JDK {featureVersion} 包。");
        var package = asset.Binary.Package
            ?? throw new InvalidOperationException("Temurin 返回的包信息不完整。");
        var semver = asset.Version.Semver
            ?? throw new InvalidOperationException("Temurin 返回的版本信息不完整。");

        return new RemotePackageDescriptor(
            Kind: ToolchainKind.Jdk,
            Provider: "temurin",
            DisplayName: $"Temurin JDK {semver} (Feature {featureVersion})",
            Version: semver,
            DownloadUrl: package.Link,
            FileName: package.Name,
            Checksum: package.Checksum,
            ChecksumAlgorithm: "SHA256",
            SuggestedInstallDirectoryName: $"temurin-{semver}-{architecture}",
            Architecture: architecture);
    }

    public async Task<RemotePackageDescriptor> ResolveAsync(
        string? versionOrFeature,
        string architecture,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(versionOrFeature))
        {
            return (await GetLatestPackagesByFeatureAsync(architecture, cancellationToken)).First();
        }

        if (int.TryParse(versionOrFeature, out var featureVersion))
        {
            return await ResolveLatestAsync(featureVersion, architecture, cancellationToken);
        }

        var packages = await GetLatestPackagesByFeatureAsync(architecture, cancellationToken);
        var matchedPackage = packages.FirstOrDefault(package =>
            package.Version.Equals(versionOrFeature, StringComparison.OrdinalIgnoreCase));

        if (matchedPackage is not null)
        {
            return matchedPackage;
        }

        throw new InvalidOperationException($"Temurin 没有找到可用于 Windows {architecture} 的 JDK {versionOrFeature} 包。");
    }

    private static int GetFeatureVersion(string version)
    {
        var featureText = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return int.TryParse(featureText, out var featureVersion) ? featureVersion : 0;
    }

    private sealed record AvailableReleasesResponse(
        [property: JsonPropertyName("available_releases")]
        IReadOnlyList<int> AvailableReleases);

    private sealed record TemurinAsset(
        [property: JsonPropertyName("binary")]
        TemurinBinary Binary,
        [property: JsonPropertyName("version")]
        TemurinVersion Version);

    private sealed record TemurinBinary(
        [property: JsonPropertyName("package")]
        TemurinPackage Package);

    private sealed record TemurinPackage(
        [property: JsonPropertyName("checksum")]
        string Checksum,
        [property: JsonPropertyName("link")]
        string Link,
        [property: JsonPropertyName("name")]
        string Name);

    private sealed record TemurinVersion(
        [property: JsonPropertyName("semver")]
        string Semver);
}
