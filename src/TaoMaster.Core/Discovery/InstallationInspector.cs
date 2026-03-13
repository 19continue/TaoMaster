using System.Text.RegularExpressions;
using TaoMaster.Core.Models;
using TaoMaster.Core.Utilities;

namespace TaoMaster.Core.Discovery;

public sealed class InstallationInspector
{
    private static readonly Regex MavenVersionRegex = new(@"maven[-_.]?(\d[\w.\-+]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JdkVersionRegex = new(@"(\d+(?:[._]\d+)*(?:\+\d+)?)", RegexOptions.Compiled);

    public bool TryInspectJdkHome(string homeDirectory, string source, WorkspaceLayout layout, out ManagedInstallation? installation)
    {
        installation = null;

        if (string.IsNullOrWhiteSpace(homeDirectory) || !Directory.Exists(homeDirectory))
        {
            return false;
        }

        var normalizedHome = PathUtilities.NormalizePath(homeDirectory);
        var javaExe = Path.Combine(normalizedHome, "bin", "java.exe");
        var javacExe = Path.Combine(normalizedHome, "bin", "javac.exe");

        if (!File.Exists(javaExe) || !File.Exists(javacExe))
        {
            return false;
        }

        var releaseMetadata = ReadReleaseFile(Path.Combine(normalizedHome, "release"));
        var version = GetValue(releaseMetadata, "JAVA_VERSION")
                      ?? ExtractVersionFromName(Path.GetFileName(normalizedHome))
                      ?? "unknown";
        var vendor = SimplifyVendor(
            GetValue(releaseMetadata, "IMPLEMENTOR")
            ?? GetValue(releaseMetadata, "IMPLEMENTOR_VERSION"))
            ?? InferLegacyJdkVendor(releaseMetadata, Path.GetFileName(normalizedHome));
        var architecture = NormalizeArchitecture(GetValue(releaseMetadata, "OS_ARCH"));
        var displayName = string.IsNullOrWhiteSpace(vendor)
            ? $"JDK {version}"
            : $"{vendor} JDK {version}";

        installation = new ManagedInstallation(
            Id: BuildId(vendor, version, architecture, "jdk"),
            Kind: ToolchainKind.Jdk,
            DisplayName: displayName,
            Version: version,
            HomeDirectory: normalizedHome,
            Source: source,
            IsManaged: PathUtilities.IsDescendantOrSelf(normalizedHome, layout.JdkRoot),
            Vendor: vendor,
            Architecture: architecture);

        return true;
    }

    public bool TryInspectMavenHome(string homeDirectory, string source, WorkspaceLayout layout, out ManagedInstallation? installation)
    {
        installation = null;

        if (string.IsNullOrWhiteSpace(homeDirectory) || !Directory.Exists(homeDirectory))
        {
            return false;
        }

        var normalizedHome = PathUtilities.NormalizePath(homeDirectory);
        var mvnCmd = Path.Combine(normalizedHome, "bin", "mvn.cmd");

        if (!File.Exists(mvnCmd))
        {
            return false;
        }

        var version = DetectMavenVersion(normalizedHome) ?? "unknown";
        var vendor = "Apache";

        installation = new ManagedInstallation(
            Id: BuildMavenId(vendor, version),
            Kind: ToolchainKind.Maven,
            DisplayName: $"{vendor} Maven {version}",
            Version: version,
            HomeDirectory: normalizedHome,
            Source: source,
            IsManaged: PathUtilities.IsDescendantOrSelf(normalizedHome, layout.MavenRoot),
            Vendor: vendor,
            Architecture: "noarch");

        return true;
    }

    private static Dictionary<string, string> ReadReleaseFile(string releaseFile)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(releaseFile))
        {
            return values;
        }

        foreach (var line in File.ReadLines(releaseFile))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');

            values[key] = value;
        }

        return values;
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string? DetectMavenVersion(string homeDirectory)
    {
        var libDirectory = Path.Combine(homeDirectory, "lib");
        if (Directory.Exists(libDirectory))
        {
            var versionFromLib = Directory.EnumerateFiles(libDirectory, "maven-core-*.jar", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Select(name => name["maven-core-".Length..])
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(versionFromLib))
            {
                return versionFromLib;
            }
        }

        var folderName = Path.GetFileName(homeDirectory);
        var match = MavenVersionRegex.Match(folderName);

        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractVersionFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var match = JdkVersionRegex.Match(name);
        return match.Success ? match.Groups[1].Value.Replace('_', '.') : null;
    }

    private static string? NormalizeArchitecture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "x86_64" => "x64",
            "amd64" => "x64",
            "x64" => "x64",
            "aarch64" => "arm64",
            "arm64" => "arm64",
            "x86" => "x86",
            _ => value.Trim()
        };
    }

    private static string? SimplifyVendor(string? rawVendor)
    {
        if (string.IsNullOrWhiteSpace(rawVendor))
        {
            return null;
        }

        var normalized = rawVendor.Trim();

        return normalized switch
        {
            var value when value.Contains("Oracle", StringComparison.OrdinalIgnoreCase) => "Oracle",
            var value when value.Contains("Adoptium", StringComparison.OrdinalIgnoreCase) => "Temurin",
            var value when value.Contains("Eclipse", StringComparison.OrdinalIgnoreCase) => "Temurin",
            var value when value.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) => "Microsoft",
            var value when value.Contains("Amazon", StringComparison.OrdinalIgnoreCase) => "Corretto",
            var value when value.Contains("BellSoft", StringComparison.OrdinalIgnoreCase) => "Liberica",
            var value when value.Contains("Azul", StringComparison.OrdinalIgnoreCase) => "Zulu",
            var value when value.Contains("OpenJDK", StringComparison.OrdinalIgnoreCase) => "OpenJDK",
            var value when value.StartsWith("jdk", StringComparison.OrdinalIgnoreCase) => null,
            _ => normalized.Replace(" Corporation", string.Empty, StringComparison.OrdinalIgnoreCase).Trim()
        };
    }

    private static string? InferLegacyJdkVendor(IReadOnlyDictionary<string, string> releaseMetadata, string? folderName)
    {
        var buildType = GetValue(releaseMetadata, "BUILD_TYPE");
        if (string.Equals(buildType, "commercial", StringComparison.OrdinalIgnoreCase))
        {
            return "Oracle";
        }

        if (!string.IsNullOrWhiteSpace(folderName))
        {
            if (folderName.Contains("oracle", StringComparison.OrdinalIgnoreCase))
            {
                return "Oracle";
            }

            if (folderName.Contains("openjdk", StringComparison.OrdinalIgnoreCase))
            {
                return "OpenJDK";
            }
        }

        return null;
    }

    private static string BuildId(string? vendor, string version, string? architecture, string fallbackPrefix)
    {
        var parts = new List<string>
        {
            Slugify(string.IsNullOrWhiteSpace(vendor) ? fallbackPrefix : vendor)
        };

        if (!string.IsNullOrWhiteSpace(version))
        {
            parts.Add(Slugify(version));
        }

        if (!string.IsNullOrWhiteSpace(architecture))
        {
            parts.Add(Slugify(architecture));
        }

        return string.Join("-", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildMavenId(string vendor, string version) =>
        $"{Slugify(vendor)}-maven-{Slugify(version)}";

    private static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        var chars = normalized
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '+' ? ch : '-')
            .ToArray();

        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }
}
