using TaoMaster.Core.Models;

namespace TaoMaster.Core.Services;

public sealed class JdkDownloadSourceService
{
    public IReadOnlyList<JdkDownloadSourceConfiguration> GetBuiltInSources() =>
    [
        new JdkDownloadSourceConfiguration("jdk-official", "Official Direct", string.Empty, "*", true),
        new JdkDownloadSourceConfiguration("jdk-ghproxy-net", "ghproxy.net", "https://ghproxy.net/", "temurin", true),
        new JdkDownloadSourceConfiguration("jdk-ghfast-top", "ghfast.top", "https://ghfast.top/", "temurin", true)
    ];

    public IReadOnlyList<JdkDownloadSourceConfiguration> BuildAvailableSources(
        IReadOnlyList<JdkDownloadSourceConfiguration>? customSources)
    {
        var merged = new Dictionary<string, JdkDownloadSourceConfiguration>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in GetBuiltInSources())
        {
            merged[source.Id] = source;
        }

        foreach (var source in customSources ?? Array.Empty<JdkDownloadSourceConfiguration>())
        {
            if (string.IsNullOrWhiteSpace(source.Id)
                || string.IsNullOrWhiteSpace(source.Name))
            {
                continue;
            }

            merged[source.Id] = source with
            {
                UrlPrefix = NormalizePrefix(source.UrlPrefix),
                SupportedProviders = string.IsNullOrWhiteSpace(source.SupportedProviders) ? "*" : source.SupportedProviders
            };
        }

        return merged.Values
            .OrderByDescending(source => source.IsBuiltIn)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool Supports(RemotePackageDescriptor package, JdkDownloadSourceConfiguration source)
    {
        if (string.IsNullOrWhiteSpace(source.SupportedProviders) || source.SupportedProviders == "*")
        {
            return true;
        }

        var providers = source.SupportedProviders
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return providers.Any(provider => provider.Equals(package.Provider, StringComparison.OrdinalIgnoreCase));
    }

    public RemotePackageDescriptor ApplySource(RemotePackageDescriptor package, JdkDownloadSourceConfiguration source)
    {
        var officialUrl = package.OfficialDownloadUrl ?? package.DownloadUrl;
        if (!Supports(package, source))
        {
            return package with
            {
                OfficialDownloadUrl = officialUrl,
                DownloadSourceId = source.Id,
                DownloadSourceName = source.Name,
                IsDownloadAvailable = false,
                AvailabilityMessage = $"Source `{source.Name}` does not support provider `{package.Provider}`."
            };
        }

        var transformedUrl = string.IsNullOrWhiteSpace(source.UrlPrefix)
            ? officialUrl
            : $"{NormalizePrefix(source.UrlPrefix)}{officialUrl}";

        return package with
        {
            DownloadUrl = transformedUrl,
            OfficialDownloadUrl = officialUrl,
            DownloadSourceId = source.Id,
            DownloadSourceName = source.Name,
            IsDownloadAvailable = true,
            AvailabilityMessage = null
        };
    }

    private static string NormalizePrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().EndsWith("/", StringComparison.Ordinal)
            ? value.Trim()
            : $"{value.Trim()}/";
    }
}
