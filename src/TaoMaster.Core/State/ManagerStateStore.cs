using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaoMaster.Core.Models;
using TaoMaster.Core.Services;

namespace TaoMaster.Core.State;

public sealed class ManagerStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static ManagerStateStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    private readonly WorkspaceInitializer _workspaceInitializer;

    public ManagerStateStore(WorkspaceInitializer workspaceInitializer)
    {
        _workspaceInitializer = workspaceInitializer;
    }

    public ManagerState EnsureInitialized(WorkspaceLayout layout)
    {
        _workspaceInitializer.EnsureCreated(layout);

        if (!File.Exists(layout.StateFile))
        {
            var state = ManagerState.CreateDefault(layout);
            Save(layout, state);
            return state;
        }

        return Load(layout);
    }

    public ManagerState Load(WorkspaceLayout layout)
    {
        _workspaceInitializer.EnsureCreated(layout);

        ManagerState? state;
        using (var stream = File.OpenRead(layout.StateFile))
        {
            state = JsonSerializer.Deserialize<ManagerState>(stream, JsonOptions);
        }

        var normalizedState = NormalizeState(state ?? ManagerState.CreateDefault(layout), layout);
        if (!EqualityComparer<ManagerState>.Default.Equals(state, normalizedState))
        {
            Save(layout, normalizedState);
        }

        return normalizedState;
    }

    public void Save(WorkspaceLayout layout, ManagerState state)
    {
        _workspaceInitializer.EnsureCreated(layout);

        using var stream = File.Create(layout.StateFile);
        JsonSerializer.Serialize(stream, state, JsonOptions);
    }

    private static ManagerState NormalizeState(ManagerState state, WorkspaceLayout layout)
    {
        var settings = state.Settings;
        var normalizedPathMode = string.IsNullOrWhiteSpace(settings.PathMode)
                                 || settings.PathMode.Equals("managed-segments", StringComparison.OrdinalIgnoreCase)
            ? "managed-shell-sync"
            : settings.PathMode;

        var normalizedInstallRoot = string.IsNullOrWhiteSpace(settings.InstallRoot) ? layout.RootDirectory : settings.InstallRoot;
        var normalizedCacheRoot = string.IsNullOrWhiteSpace(settings.DownloadCacheRoot) ? layout.CacheRoot : settings.DownloadCacheRoot;
        var normalizedTempRoot = string.IsNullOrWhiteSpace(settings.TempRoot) ? layout.TempRoot : settings.TempRoot;
        var normalizedManagedJdkInstallRoot = string.IsNullOrWhiteSpace(settings.ManagedJdkInstallRoot)
            ? layout.JdkRoot
            : settings.ManagedJdkInstallRoot;
        var normalizedManagedMavenInstallRoot = string.IsNullOrWhiteSpace(settings.ManagedMavenInstallRoot)
            ? layout.MavenRoot
            : settings.ManagedMavenInstallRoot;
        var normalizedMavenSettingsFilePath = string.IsNullOrWhiteSpace(settings.MavenSettingsFilePath)
            ? ManagerSettings.GetDefaultMavenSettingsFilePath()
            : settings.MavenSettingsFilePath;
        var normalizedMavenLocalRepositoryPath = string.IsNullOrWhiteSpace(settings.MavenLocalRepositoryPath)
            ? ManagerSettings.GetDefaultMavenLocalRepositoryPath()
            : settings.MavenLocalRepositoryPath;
        var normalizedMirrors = settings.MavenMirrors ?? Array.Empty<MavenMirrorConfiguration>();
        var normalizedCustomDownloadSources = settings.CustomMavenDownloadSources ?? Array.Empty<MavenDownloadSourceConfiguration>();
        var normalizedPreferredMavenDownloadSourceId = string.IsNullOrWhiteSpace(settings.PreferredMavenDownloadSourceId)
            ? "apache-official"
            : settings.PreferredMavenDownloadSourceId.Trim();

        if (normalizedPathMode == settings.PathMode
            && normalizedInstallRoot == settings.InstallRoot
            && normalizedCacheRoot == settings.DownloadCacheRoot
            && normalizedTempRoot == settings.TempRoot
            && normalizedManagedJdkInstallRoot == settings.ManagedJdkInstallRoot
            && normalizedManagedMavenInstallRoot == settings.ManagedMavenInstallRoot
            && normalizedMavenSettingsFilePath == settings.MavenSettingsFilePath
            && normalizedMavenLocalRepositoryPath == settings.MavenLocalRepositoryPath
            && normalizedMirrors.SequenceEqual(settings.MavenMirrors ?? Array.Empty<MavenMirrorConfiguration>())
            && normalizedCustomDownloadSources.SequenceEqual(settings.CustomMavenDownloadSources ?? Array.Empty<MavenDownloadSourceConfiguration>())
            && normalizedPreferredMavenDownloadSourceId == settings.PreferredMavenDownloadSourceId)
        {
            return state;
        }

        return state with
        {
            Settings = settings with
            {
                PathMode = normalizedPathMode,
                InstallRoot = normalizedInstallRoot,
                DownloadCacheRoot = normalizedCacheRoot,
                TempRoot = normalizedTempRoot,
                ManagedJdkInstallRoot = normalizedManagedJdkInstallRoot,
                ManagedMavenInstallRoot = normalizedManagedMavenInstallRoot,
                MavenSettingsFilePath = normalizedMavenSettingsFilePath,
                MavenLocalRepositoryPath = normalizedMavenLocalRepositoryPath,
                MavenMirrors = normalizedMirrors,
                CustomMavenDownloadSources = normalizedCustomDownloadSources,
                PreferredMavenDownloadSourceId = normalizedPreferredMavenDownloadSourceId
            }
        };
    }
}
