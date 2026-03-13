using System.Xml.Linq;
using TaoMaster.Core.Models;

namespace TaoMaster.Core.Services;

public sealed class MavenConfigurationService
{
    private static readonly XNamespace SettingsNamespace = "http://maven.apache.org/SETTINGS/1.2.0";
    private static readonly XNamespace XmlSchemaNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    public IReadOnlyList<MavenMirrorConfiguration> GetBuiltInMirrors() =>
    [
        new MavenMirrorConfiguration("aliyun-public", "Aliyun Public", "https://maven.aliyun.com/repository/public", "*", true),
        new MavenMirrorConfiguration("tencent-public", "Tencent Public", "https://mirrors.cloud.tencent.com/nexus/repository/maven-public/", "*", true),
        new MavenMirrorConfiguration("huawei-public", "Huawei Public", "https://repo.huaweicloud.com/repository/maven/", "*", true),
        new MavenMirrorConfiguration("tuna-central", "Tsinghua Tuna", "https://mirrors.tuna.tsinghua.edu.cn/maven/", "central", true)
    ];

    public MavenSettingsApplyResult ApplySettings(
        string settingsFilePath,
        string localRepositoryPath,
        IReadOnlyList<MavenMirrorConfiguration> mirrors,
        string? previousLocalRepositoryPath,
        bool migrateLocalRepository)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            throw new InvalidOperationException("Maven settings.xml 路径不能为空。");
        }

        if (string.IsNullOrWhiteSpace(localRepositoryPath))
        {
            throw new InvalidOperationException("Maven 本地仓库路径不能为空。");
        }

        var normalizedSettingsFilePath = Path.GetFullPath(settingsFilePath.Trim());
        var normalizedLocalRepositoryPath = Path.GetFullPath(localRepositoryPath.Trim());
        var normalizedPreviousRepositoryPath = string.IsNullOrWhiteSpace(previousLocalRepositoryPath)
            ? null
            : Path.GetFullPath(previousLocalRepositoryPath.Trim());

        var settingsDirectory = Path.GetDirectoryName(normalizedSettingsFilePath)
                                ?? throw new InvalidOperationException("无法确定 Maven settings.xml 所在目录。");
        Directory.CreateDirectory(settingsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedLocalRepositoryPath)
                                  ?? throw new InvalidOperationException("无法确定 Maven 本地仓库的父目录。"));

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
        var root = document.Root ?? throw new InvalidOperationException("Maven settings.xml 根节点无效。");

        ReplaceSingleElement(root, "localRepository", normalizedLocalRepositoryPath);
        ReplaceMirrors(root, mirrors);

        document.Save(normalizedSettingsFilePath);

        return new MavenSettingsApplyResult(
            SettingsFilePath: normalizedSettingsFilePath,
            LocalRepositoryPath: normalizedLocalRepositoryPath,
            RepositoryMigrated: migrateLocalRepository
                                && !string.IsNullOrWhiteSpace(normalizedPreviousRepositoryPath)
                                && !string.Equals(normalizedPreviousRepositoryPath, normalizedLocalRepositoryPath, StringComparison.OrdinalIgnoreCase),
            BackupFilePath: backupFilePath);
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

    private static void ReplaceSingleElement(XElement root, string localName, string value)
    {
        root.Elements(SettingsNamespace + localName).Remove();
        root.Add(new XElement(SettingsNamespace + localName, value));
    }

    private static void ReplaceMirrors(XElement root, IReadOnlyList<MavenMirrorConfiguration> mirrors)
    {
        root.Elements(SettingsNamespace + "mirrors").Remove();

        var mirrorsElement = new XElement(SettingsNamespace + "mirrors");
        foreach (var mirror in mirrors)
        {
            mirrorsElement.Add(
                new XElement(
                    SettingsNamespace + "mirror",
                    new XElement(SettingsNamespace + "id", mirror.Id),
                    new XElement(SettingsNamespace + "name", mirror.Name),
                    new XElement(SettingsNamespace + "url", mirror.Url),
                    new XElement(SettingsNamespace + "mirrorOf", string.IsNullOrWhiteSpace(mirror.MirrorOf) ? "*" : mirror.MirrorOf)));
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
            throw new InvalidOperationException("目标 Maven 本地仓库目录已存在且不为空，无法自动迁移。");
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
}
