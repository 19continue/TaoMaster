using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaoMaster.Core.Models;
using TaoMaster.Core.Services;
using TaoMaster.Core.Utilities;

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
        var normalizedMavenConfigurationScope = Enum.IsDefined(settings.MavenConfigurationScope)
            ? settings.MavenConfigurationScope
            : MavenConfigurationScope.User;
        var normalizedMavenSettingsFilePath = string.IsNullOrWhiteSpace(settings.MavenSettingsFilePath)
            ? ManagerSettings.GetDefaultMavenSettingsFilePath()
            : settings.MavenSettingsFilePath;
        var normalizedMavenToolchainsFilePath = string.IsNullOrWhiteSpace(settings.MavenToolchainsFilePath)
            ? ManagerSettings.GetDefaultMavenToolchainsFilePath()
            : settings.MavenToolchainsFilePath;
        var normalizedMavenLocalRepositoryPath = string.IsNullOrWhiteSpace(settings.MavenLocalRepositoryPath)
            ? ManagerSettings.GetDefaultMavenLocalRepositoryPath()
            : settings.MavenLocalRepositoryPath;
        var normalizedMirrors = settings.MavenMirrors ?? Array.Empty<MavenMirrorConfiguration>();
        var normalizedCustomJdkDownloadSources = settings.CustomJdkDownloadSources ?? Array.Empty<JdkDownloadSourceConfiguration>();
        var normalizedPreferredJdkDownloadSourceId = string.IsNullOrWhiteSpace(settings.PreferredJdkDownloadSourceId)
            ? "jdk-official"
            : settings.PreferredJdkDownloadSourceId.Trim();
        var normalizedCustomDownloadSources = settings.CustomMavenDownloadSources ?? Array.Empty<MavenDownloadSourceConfiguration>();
        var normalizedPreferredMavenDownloadSourceId = string.IsNullOrWhiteSpace(settings.PreferredMavenDownloadSourceId)
            ? "apache-official"
            : settings.PreferredMavenDownloadSourceId.Trim();
        var normalizedProjects = (state.Projects ?? Array.Empty<ManagedProject>())
            .Where(project => !string.IsNullOrWhiteSpace(project.Id) && !string.IsNullOrWhiteSpace(project.ProjectDirectory))
            .Select(project =>
            {
                var normalizedProjectDirectory = PathUtilities.NormalizePath(project.ProjectDirectory);
                return project with
                {
                    ProjectDirectory = normalizedProjectDirectory,
                    DisplayName = string.IsNullOrWhiteSpace(project.DisplayName)
                        ? Path.GetFileName(normalizedProjectDirectory)
                        : project.DisplayName,
                    Detection = project.Detection ?? ProjectDetectionSnapshot.Empty
                };
            })
            .OrderBy(project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.ProjectDirectory, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var normalizedActiveProjectId = normalizedProjects.Any(project => project.Id.Equals(state.ActiveProjectId, StringComparison.OrdinalIgnoreCase))
            ? state.ActiveProjectId
            : normalizedProjects.FirstOrDefault()?.Id;

        if (normalizedPathMode == settings.PathMode
            && normalizedInstallRoot == settings.InstallRoot
            && normalizedCacheRoot == settings.DownloadCacheRoot
            && normalizedTempRoot == settings.TempRoot
            && normalizedManagedJdkInstallRoot == settings.ManagedJdkInstallRoot
            && normalizedManagedMavenInstallRoot == settings.ManagedMavenInstallRoot
            && normalizedMavenConfigurationScope == settings.MavenConfigurationScope
            && normalizedMavenSettingsFilePath == settings.MavenSettingsFilePath
            && normalizedMavenToolchainsFilePath == settings.MavenToolchainsFilePath
            && normalizedMavenLocalRepositoryPath == settings.MavenLocalRepositoryPath
            && normalizedMirrors.SequenceEqual(settings.MavenMirrors ?? Array.Empty<MavenMirrorConfiguration>())
            && normalizedCustomJdkDownloadSources.SequenceEqual(settings.CustomJdkDownloadSources ?? Array.Empty<JdkDownloadSourceConfiguration>())
            && normalizedPreferredJdkDownloadSourceId == settings.PreferredJdkDownloadSourceId
            && normalizedCustomDownloadSources.SequenceEqual(settings.CustomMavenDownloadSources ?? Array.Empty<MavenDownloadSourceConfiguration>())
            && normalizedPreferredMavenDownloadSourceId == settings.PreferredMavenDownloadSourceId
            && normalizedProjects.SequenceEqual(state.Projects ?? Array.Empty<ManagedProject>())
            && normalizedActiveProjectId == state.ActiveProjectId)
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
                MavenConfigurationScope = normalizedMavenConfigurationScope,
                MavenSettingsFilePath = normalizedMavenSettingsFilePath,
                MavenToolchainsFilePath = normalizedMavenToolchainsFilePath,
                MavenLocalRepositoryPath = normalizedMavenLocalRepositoryPath,
                MavenMirrors = normalizedMirrors,
                CustomJdkDownloadSources = normalizedCustomJdkDownloadSources,
                PreferredJdkDownloadSourceId = normalizedPreferredJdkDownloadSourceId,
                CustomMavenDownloadSources = normalizedCustomDownloadSources,
                PreferredMavenDownloadSourceId = normalizedPreferredMavenDownloadSourceId
            },
            Projects = normalizedProjects,
            ActiveProjectId = normalizedActiveProjectId
        };
    }
}
