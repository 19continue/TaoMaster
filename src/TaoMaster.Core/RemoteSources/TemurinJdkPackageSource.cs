using System.Text.Json;
using System.Text.Json.Serialization;
using TaoMaster.Core.Models;

namespace TaoMaster.Core.RemoteSources;

public sealed class TemurinJdkPackageSource
{
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

        return payload.AvailableReleases
            .OrderByDescending(x => x)
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
            DisplayName: $"Temurin JDK {semver}",
            Version: semver,
            DownloadUrl: package.Link,
            FileName: package.Name,
            Checksum: package.Checksum,
            ChecksumAlgorithm: "SHA256",
            SuggestedInstallDirectoryName: $"temurin-{semver}-{architecture}",
            Architecture: architecture);
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
