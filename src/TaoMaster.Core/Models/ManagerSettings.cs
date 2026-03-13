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
    MavenConfigurationScope MavenConfigurationScope,
    string MavenSettingsFilePath,
    string MavenToolchainsFilePath,
    string MavenLocalRepositoryPath,
    IReadOnlyList<MavenMirrorConfiguration> MavenMirrors,
    IReadOnlyList<JdkDownloadSourceConfiguration> CustomJdkDownloadSources,
    string PreferredJdkDownloadSourceId,
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
            MavenConfigurationScope: MavenConfigurationScope.User,
            MavenSettingsFilePath: GetDefaultMavenSettingsFilePath(),
            MavenToolchainsFilePath: GetDefaultMavenToolchainsFilePath(),
            MavenLocalRepositoryPath: GetDefaultMavenLocalRepositoryPath(),
            MavenMirrors: Array.Empty<MavenMirrorConfiguration>(),
            CustomJdkDownloadSources: Array.Empty<JdkDownloadSourceConfiguration>(),
            PreferredJdkDownloadSourceId: "jdk-official",
            CustomMavenDownloadSources: Array.Empty<MavenDownloadSourceConfiguration>(),
            PreferredMavenDownloadSourceId: "apache-official",
            PreferredUiLanguage: "SimplifiedChinese");

    public static string GetDefaultMavenSettingsFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".m2",
            "settings.xml");

    public static string GetDefaultMavenToolchainsFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".m2",
            "toolchains.xml");

    public static string GetDefaultMavenLocalRepositoryPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".m2",
            "repository");
}
