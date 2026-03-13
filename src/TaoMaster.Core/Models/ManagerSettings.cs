using TaoMaster.Core;

namespace TaoMaster.Core.Models;

public sealed record ManagerSettings(
    string InstallRoot,
    string DownloadCacheRoot,
    string TempRoot,
    string ManagedJdkInstallRoot,
    string ManagedMavenInstallRoot,
    string PathMode,
    string PreferredJdkProvider,
    string PreferredMavenProvider,
    string MavenSettingsFilePath,
    string MavenLocalRepositoryPath,
    IReadOnlyList<MavenMirrorConfiguration> MavenMirrors,
    IReadOnlyList<MavenDownloadSourceConfiguration> CustomMavenDownloadSources,
    string PreferredMavenDownloadSourceId,
    string PreferredUiLanguage = "SimplifiedChinese")
{
    public static ManagerSettings CreateDefault(WorkspaceLayout layout) =>
        new(
            InstallRoot: layout.RootDirectory,
            DownloadCacheRoot: layout.CacheRoot,
            TempRoot: layout.TempRoot,
            ManagedJdkInstallRoot: layout.JdkRoot,
            ManagedMavenInstallRoot: layout.MavenRoot,
            PathMode: "managed-shell-sync",
            PreferredJdkProvider: "temurin",
            PreferredMavenProvider: "apache",
            MavenSettingsFilePath: GetDefaultMavenSettingsFilePath(),
            MavenLocalRepositoryPath: GetDefaultMavenLocalRepositoryPath(),
            MavenMirrors: Array.Empty<MavenMirrorConfiguration>(),
            CustomMavenDownloadSources: Array.Empty<MavenDownloadSourceConfiguration>(),
            PreferredMavenDownloadSourceId: "apache-official",
            PreferredUiLanguage: "SimplifiedChinese");

    public static string GetDefaultMavenSettingsFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".m2",
            "settings.xml");

    public static string GetDefaultMavenLocalRepositoryPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".m2",
            "repository");
}
