using TaoMaster.Core;

namespace TaoMaster.Core.Models;

public sealed record ManagerSettings(
    string InstallRoot,
    string DownloadCacheRoot,
    string TempRoot,
    string PathMode,
    string PreferredJdkProvider,
    string PreferredMavenProvider,
    string PreferredUiLanguage = "auto")
{
    public static ManagerSettings CreateDefault(WorkspaceLayout layout) =>
        new(
            InstallRoot: layout.RootDirectory,
            DownloadCacheRoot: layout.CacheRoot,
            TempRoot: layout.TempRoot,
            PathMode: "managed-segments",
            PreferredJdkProvider: "temurin",
            PreferredMavenProvider: "apache",
            PreferredUiLanguage: "auto");
}
