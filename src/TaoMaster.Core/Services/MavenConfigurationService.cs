using System.Xml.Linq;
using TaoMaster.Core.Models;

namespace TaoMaster.Core.Services;

public sealed class MavenConfigurationService
{
    private static readonly XNamespace SettingsNamespace = "http://maven.apache.org/SETTINGS/1.2.0";
    private static readonly XNamespace XmlSchemaNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace ToolchainsNamespace = "http://maven.apache.org/TOOLCHAINS/1.1.0";

    public IReadOnlyList<MavenMirrorConfiguration> GetBuiltInMirrors() =>
    [
        new MavenMirrorConfiguration("aliyun-public", "Aliyun Public", "https://maven.aliyun.com/repository/public", "*", true),
        new MavenMirrorConfiguration("tencent-public", "Tencent Public", "https://mirrors.cloud.tencent.com/nexus/repository/maven-public/", "*", true),
        new MavenMirrorConfiguration("huawei-public", "Huawei Public", "https://repo.huaweicloud.com/repository/maven/", "*", true),
        new MavenMirrorConfiguration("tuna-central", "Tsinghua TUNA", "https://mirrors.tuna.tsinghua.edu.cn/maven/", "central", true)
    ];

    public IReadOnlyList<MavenDownloadSourceConfiguration> GetBuiltInDownloadSources() =>
    [
        new MavenDownloadSourceConfiguration("apache-official", "Apache Official", "https://downloads.apache.org/maven/maven-3", true),
        new MavenDownloadSourceConfiguration("apache-archive", "Apache Archive", "https://archive.apache.org/dist/maven/maven-3", true),
        new MavenDownloadSourceConfiguration("aliyun-apache", "Aliyun Apache Mirror", "https://mirrors.aliyun.com/apache/maven/maven-3", true),
        new MavenDownloadSourceConfiguration("tencent-apache", "Tencent Apache Mirror", "https://mirrors.cloud.tencent.com/apache/maven/maven-3", true),
        new MavenDownloadSourceConfiguration("huawei-apache", "Huawei Apache Mirror", "https://mirrors.huaweicloud.com/apache/maven/maven-3", true),
        new MavenDownloadSourceConfiguration("tuna-apache", "Tsinghua TUNA Apache Mirror", "https://mirrors.tuna.tsinghua.edu.cn/apache/maven/maven-3", true)
    ];

    public string ResolveSettingsFilePath(MavenConfigurationScope scope, string? mavenHome = null) =>
        scope == MavenConfigurationScope.User
            ? ManagerSettings.GetDefaultMavenSettingsFilePath()
            : BuildGlobalConfigPath(mavenHome, "settings.xml");

    public string ResolveToolchainsFilePath(MavenConfigurationScope scope, string? mavenHome = null) =>
        scope == MavenConfigurationScope.User
            ? ManagerSettings.GetDefaultMavenToolchainsFilePath()
            : BuildGlobalConfigPath(mavenHome, "toolchains.xml");

    public IReadOnlyList<MavenDownloadSourceConfiguration> BuildAvailableDownloadSources(
        IReadOnlyList<MavenDownloadSourceConfiguration>? customSources)
    {
        var merged = new Dictionary<string, MavenDownloadSourceConfiguration>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in GetBuiltInDownloadSources())
        {
            merged[source.Id] = source;
        }

        foreach (var source in customSources ?? Array.Empty<MavenDownloadSourceConfiguration>())
        {
            if (string.IsNullOrWhiteSpace(source.Id)
                || string.IsNullOrWhiteSpace(source.Name)
                || string.IsNullOrWhiteSpace(source.BaseUrl))
            {
                continue;
            }

            merged[source.Id] = source with
            {
                BaseUrl = NormalizeUrl(source.BaseUrl)
            };
        }

        return merged.Values
            .OrderByDescending(source => source.IsBuiltIn)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public MavenSettingsSnapshot ReadSettingsSnapshot(string settingsFilePath)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            throw new InvalidOperationException("Maven settings.xml path cannot be empty.");
        }

        var normalizedSettingsFilePath = Path.GetFullPath(settingsFilePath.Trim());
        if (!File.Exists(normalizedSettingsFilePath))
        {
            return new MavenSettingsSnapshot(
                normalizedSettingsFilePath,
                ManagerSettings.GetDefaultMavenLocalRepositoryPath(),
                Array.Empty<MavenMirrorConfiguration>());
        }

        var document = XDocument.Load(normalizedSettingsFilePath, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("The Maven settings.xml root element is invalid.");
        var elementNamespace = GetElementNamespace(root);
        var localRepositoryPath = root.Element(elementNamespace + "localRepository")?.Value?.Trim();
        var mirrors = root
            .Elements(elementNamespace + "mirrors")
            .Elements(elementNamespace + "mirror")
            .Select(element => ReadMirror(element, elementNamespace))
            .Where(mirror => mirror is not null)
            .Cast<MavenMirrorConfiguration>()
            .ToList();

        return new MavenSettingsSnapshot(
            normalizedSettingsFilePath,
            string.IsNullOrWhiteSpace(localRepositoryPath)
                ? ManagerSettings.GetDefaultMavenLocalRepositoryPath()
                : localRepositoryPath,
            mirrors);
    }

    public IReadOnlyList<MavenMirrorConfiguration> ImportMirrorsFromXmlFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException("Mirror XML file path cannot be empty.");
        }

        var normalizedPath = Path.GetFullPath(filePath.Trim());
        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException("Mirror XML file was not found.", normalizedPath);
        }

        var document = XDocument.Load(normalizedPath, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("The mirror XML root element is invalid.");
        var elementNamespace = GetElementNamespace(root);

        IEnumerable<XElement> mirrorElements = root.Name.LocalName switch
        {
            "settings" => root.Elements(elementNamespace + "mirrors").Elements(elementNamespace + "mirror"),
            "mirrors" => root.Elements(elementNamespace + "mirror"),
            "mirror" => [root],
            _ => throw new InvalidOperationException("The selected XML file does not contain a supported Maven mirror structure.")
        };

        return mirrorElements
            .Select(element => ReadMirror(element, elementNamespace))
            .Where(mirror => mirror is not null)
            .Cast<MavenMirrorConfiguration>()
            .OrderBy(mirror => mirror.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public MavenSettingsApplyResult ApplySettings(
        string settingsFilePath,
        string localRepositoryPath,
        IReadOnlyList<MavenMirrorConfiguration> mirrors,
        string? previousLocalRepositoryPath,
        bool migrateLocalRepository)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            throw new InvalidOperationException("Maven settings.xml path cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(localRepositoryPath))
        {
            throw new InvalidOperationException("Maven local repository path cannot be empty.");
        }

        var normalizedSettingsFilePath = Path.GetFullPath(settingsFilePath.Trim());
        var normalizedLocalRepositoryPath = Path.GetFullPath(localRepositoryPath.Trim());
        var normalizedPreviousRepositoryPath = string.IsNullOrWhiteSpace(previousLocalRepositoryPath)
            ? null
            : Path.GetFullPath(previousLocalRepositoryPath.Trim());

        var settingsDirectory = Path.GetDirectoryName(normalizedSettingsFilePath)
                                ?? throw new InvalidOperationException("Could not determine the settings.xml parent directory.");
        Directory.CreateDirectory(settingsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedLocalRepositoryPath)
                                  ?? throw new InvalidOperationException("Could not determine the local repository parent directory."));

        if (migrateLocalRepository
            && !string.IsNullOrWhiteSpace(normalizedPreviousRepositoryPath)
            && !string.Equals(normalizedPreviousRepositoryPath, normalizedLocalRepositoryPath, StringComparison.OrdinalIgnoreCase))
        {
            MigrateRepositoryDirectory(normalizedPreviousRepositoryPath, normalizedLocalRepositoryPath);
        }
        else
        {
            Directory.CreateDirectory(normalizedLocalRepositoryPath);
        }

        var backupFilePath = BackupExistingSettingsFile(normalizedSettingsFilePath);
        var document = LoadOrCreateSettingsDocument(normalizedSettingsFilePath);
        var root = document.Root ?? throw new InvalidOperationException("The Maven settings.xml root element is invalid.");
        var elementNamespace = GetElementNamespace(root);

        ReplaceSingleElement(root, "localRepository", normalizedLocalRepositoryPath, elementNamespace);
        ReplaceMirrors(root, mirrors, elementNamespace);

        document.Save(normalizedSettingsFilePath);

        return new MavenSettingsApplyResult(
            SettingsFilePath: normalizedSettingsFilePath,
            LocalRepositoryPath: normalizedLocalRepositoryPath,
            RepositoryMigrated: migrateLocalRepository
                                && !string.IsNullOrWhiteSpace(normalizedPreviousRepositoryPath)
                                && !string.Equals(normalizedPreviousRepositoryPath, normalizedLocalRepositoryPath, StringComparison.OrdinalIgnoreCase),
            BackupFilePath: backupFilePath);
    }

    public void EnsureEditableSettingsFile(string settingsFilePath)
    {
        var normalizedPath = Path.GetFullPath(settingsFilePath.Trim());
        if (File.Exists(normalizedPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedPath)!);
        LoadOrCreateSettingsDocument(normalizedPath).Save(normalizedPath);
    }

    public void EnsureEditableToolchainsFile(string toolchainsFilePath)
    {
        var normalizedPath = Path.GetFullPath(toolchainsFilePath.Trim());
        if (File.Exists(normalizedPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedPath)!);
        CreateToolchainsDocument().Save(normalizedPath);
    }

    private static MavenMirrorConfiguration? ReadMirror(XElement element, XNamespace elementNamespace)
    {
        var id = element.Element(elementNamespace + "id")?.Value?.Trim();
        var name = element.Element(elementNamespace + "name")?.Value?.Trim();
        var url = element.Element(elementNamespace + "url")?.Value?.Trim();
        var mirrorOf = element.Element(elementNamespace + "mirrorOf")?.Value?.Trim();

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return new MavenMirrorConfiguration(
            id,
            string.IsNullOrWhiteSpace(name) ? id : name,
            NormalizeUrl(url),
            string.IsNullOrWhiteSpace(mirrorOf) ? "*" : mirrorOf,
            false);
    }

    private static XDocument LoadOrCreateSettingsDocument(string settingsFilePath)
    {
        if (File.Exists(settingsFilePath))
        {
            return XDocument.Load(settingsFilePath, LoadOptions.PreserveWhitespace);
        }

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(
                SettingsNamespace + "settings",
                new XAttribute(XNamespace.Xmlns + "xsi", XmlSchemaNamespace),
                new XAttribute(
                    XmlSchemaNamespace + "schemaLocation",
                    "http://maven.apache.org/SETTINGS/1.2.0 https://maven.apache.org/xsd/settings-1.2.0.xsd")));
    }

    private static XDocument CreateToolchainsDocument() =>
        new(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(
                ToolchainsNamespace + "toolchains",
                new XAttribute(XNamespace.Xmlns + "xsi", XmlSchemaNamespace),
                new XAttribute(
                    XmlSchemaNamespace + "schemaLocation",
                    "http://maven.apache.org/TOOLCHAINS/1.1.0 https://maven.apache.org/xsd/toolchains-1.1.0.xsd")));

    private static void ReplaceSingleElement(XElement root, string localName, string value, XNamespace elementNamespace)
    {
        root.Elements(elementNamespace + localName).Remove();
        root.Add(new XElement(elementNamespace + localName, value));
    }

    private static void ReplaceMirrors(XElement root, IReadOnlyList<MavenMirrorConfiguration> mirrors, XNamespace elementNamespace)
    {
        root.Elements(elementNamespace + "mirrors").Remove();

        var mirrorsElement = new XElement(elementNamespace + "mirrors");
        foreach (var mirror in mirrors
                     .Where(mirror => !string.IsNullOrWhiteSpace(mirror.Id)
                                      && !string.IsNullOrWhiteSpace(mirror.Name)
                                      && !string.IsNullOrWhiteSpace(mirror.Url))
                     .OrderBy(mirror => mirror.Name, StringComparer.OrdinalIgnoreCase))
        {
            mirrorsElement.Add(
                new XElement(
                    elementNamespace + "mirror",
                    new XElement(elementNamespace + "id", mirror.Id),
                    new XElement(elementNamespace + "name", mirror.Name),
                    new XElement(elementNamespace + "url", NormalizeUrl(mirror.Url)),
                    new XElement(elementNamespace + "mirrorOf", string.IsNullOrWhiteSpace(mirror.MirrorOf) ? "*" : mirror.MirrorOf)));
        }

        root.Add(mirrorsElement);
    }

    private static string? BackupExistingSettingsFile(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
        {
            return null;
        }

        var backupFilePath = $"{settingsFilePath}.bak";
        File.Copy(settingsFilePath, backupFilePath, overwrite: true);
        return backupFilePath;
    }

    private static void MigrateRepositoryDirectory(string sourcePath, string targetPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            Directory.CreateDirectory(targetPath);
            return;
        }

        if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
        {
            throw new InvalidOperationException("The target Maven local repository directory already exists and is not empty.");
        }

        CopyDirectory(sourcePath, targetPath);

        try
        {
            Directory.Delete(sourcePath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup: the new repository is already ready to use.
        }
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);

        foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var targetFile = Path.Combine(targetPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static string NormalizeUrl(string url) =>
        url.Trim().TrimEnd('/');

    private static string BuildGlobalConfigPath(string? mavenHome, string fileName)
    {
        var candidateHome = string.IsNullOrWhiteSpace(mavenHome)
            ? Environment.GetEnvironmentVariable("MAVEN_HOME")
            : mavenHome;

        if (string.IsNullOrWhiteSpace(candidateHome))
        {
            throw new InvalidOperationException("A Maven installation must be selected before using global Maven configuration files.");
        }

        return Path.Combine(candidateHome, "conf", fileName);
    }

    private static XNamespace GetElementNamespace(XElement root) =>
        root.Name.Namespace == XNamespace.None
            ? XNamespace.None
            : root.Name.Namespace;
}
