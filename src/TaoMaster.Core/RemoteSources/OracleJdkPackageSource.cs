using System.Text.RegularExpressions;
using TaoMaster.Core.Models;

namespace TaoMaster.Core.RemoteSources;

public sealed class OracleJdkPackageSource
{
    private static readonly Regex SectionRegex = new(
        @"Java SE Development Kit\s+(?<version>[\d][\w.+-]*)\s+downloads(?<content>.*?)(?=Java SE Development Kit\s+[\d][\w.+-]*\s+downloads|</body>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex PublicWindowsZipRegex = new(
        @"https://download\.oracle\.com/java/(?<feature>\d+)/latest/jdk-\k<feature>_windows-x64_bin\.zip",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;

    public OracleJdkPackageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<RemotePackageDescriptor>> GetAvailablePackagesAsync(CancellationToken cancellationToken)
    {
        var html = await _httpClient.GetStringAsync("https://www.oracle.com/java/technologies/downloads/", cancellationToken);
        var packages = new List<RemotePackageDescriptor>();

        foreach (Match section in SectionRegex.Matches(html))
        {
            var version = section.Groups["version"].Value.Trim();
            var content = section.Groups["content"].Value;
            var downloadMatch = PublicWindowsZipRegex.Match(content);
            if (!downloadMatch.Success)
            {
                continue;
            }

            var feature = downloadMatch.Groups["feature"].Value;
            var downloadUrl = downloadMatch.Value;
            var checksum = await ReadChecksumAsync(downloadUrl, cancellationToken);

            packages.Add(
                new RemotePackageDescriptor(
                    Kind: ToolchainKind.Jdk,
                    Provider: "oracle",
                    DisplayName: $"Oracle JDK {version} (Feature {feature})",
                    Version: version,
                    DownloadUrl: downloadUrl,
                    FileName: Path.GetFileName(downloadUrl),
                    Checksum: checksum,
                    ChecksumAlgorithm: "SHA256",
                    SuggestedInstallDirectoryName: $"oracle-{version}-{feature}-x64",
                    Architecture: "x64",
                    OfficialDownloadUrl: downloadUrl));
        }

        return packages
            .OrderByDescending(package => ParseFeatureVersion(package.Version))
            .ThenByDescending(package => package.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RemotePackageDescriptor> ResolveAsync(string? versionOrFeature, CancellationToken cancellationToken)
    {
        var packages = await GetAvailablePackagesAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(versionOrFeature))
        {
            return packages.First();
        }

        var normalized = versionOrFeature.Trim();
        var matched = packages.FirstOrDefault(package =>
            package.Version.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || GetFeatureText(package.Version).Equals(normalized, StringComparison.OrdinalIgnoreCase));

        return matched
               ?? throw new InvalidOperationException($"Oracle 官网当前没有提供 Windows x64 的 JDK {normalized} 直链下载。");
    }

    private async Task<string> ReadChecksumAsync(string downloadUrl, CancellationToken cancellationToken)
    {
        var checksumText = await _httpClient.GetStringAsync($"{downloadUrl}.sha256", cancellationToken);
        return checksumText
            .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .First();
    }

    private static int ParseFeatureVersion(string version)
    {
        var featureText = GetFeatureText(version);
        return int.TryParse(featureText, out var featureVersion) ? featureVersion : 0;
    }

    private static string GetFeatureText(string version) =>
        version.StartsWith("8u", StringComparison.OrdinalIgnoreCase)
            ? "8"
            : version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? version;
}
