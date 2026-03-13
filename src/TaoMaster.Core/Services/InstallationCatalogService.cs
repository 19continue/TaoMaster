using TaoMaster.Core.Discovery;
using TaoMaster.Core.Models;
using TaoMaster.Core.Utilities;

namespace TaoMaster.Core.Services;

public sealed class InstallationCatalogService
{
    private readonly InstallationInspector _inspector;

    public InstallationCatalogService(InstallationInspector inspector)
    {
        _inspector = inspector;
    }

    public ManagerState MergeDiscovered(ManagerState state, DiscoverySnapshot snapshot)
    {
        var jdks = MergeInstallations(state.Jdks, snapshot.Jdks);
        var mavens = MergeInstallations(state.Mavens, snapshot.Mavens);

        return state with
        {
            Jdks = jdks,
            Mavens = mavens,
            ActiveSelection = NormalizeSelection(state.ActiveSelection, jdks, mavens)
        };
    }

    public (ManagerState State, ManagedInstallation Installation) ImportInstallation(
        ManagerState state,
        ToolchainKind kind,
        string homeDirectory,
        WorkspaceLayout layout)
    {
        ManagedInstallation? installation = null;

        var success = kind switch
        {
            ToolchainKind.Jdk => _inspector.TryInspectJdkHome(homeDirectory, "import", layout, out installation),
            ToolchainKind.Maven => _inspector.TryInspectMavenHome(homeDirectory, "import", layout, out installation),
            _ => false
        };

        if (!success || installation is null)
        {
            throw new ArgumentException($"目录不是有效的 {kind} 安装目录: {homeDirectory}", nameof(homeDirectory));
        }

        var updatedState = kind switch
        {
            ToolchainKind.Jdk => BuildImportedJdkState(state, installation),
            ToolchainKind.Maven => BuildImportedMavenState(state, installation),
            _ => state
        };

        return (updatedState, installation);
    }

    public ManagerState RegisterInstallation(ManagerState state, ManagedInstallation installation) =>
        installation.Kind switch
        {
            ToolchainKind.Jdk => state with
            {
                Jdks = MergeInstallations(state.Jdks, new[] { installation }),
                ActiveSelection = NormalizeSelection(
                    state.ActiveSelection,
                    MergeInstallations(state.Jdks, new[] { installation }),
                    state.Mavens)
            },
            ToolchainKind.Maven => state with
            {
                Mavens = MergeInstallations(state.Mavens, new[] { installation }),
                ActiveSelection = NormalizeSelection(
                    state.ActiveSelection,
                    state.Jdks,
                    MergeInstallations(state.Mavens, new[] { installation }))
            },
            _ => state
        };

    private static IReadOnlyList<ManagedInstallation> MergeInstallations(
        IEnumerable<ManagedInstallation> existing,
        IEnumerable<ManagedInstallation> incoming)
    {
        var merged = new Dictionary<string, ManagedInstallation>(PathUtilities.Comparer);

        foreach (var installation in existing.Where(IsAvailable))
        {
            merged[PathUtilities.NormalizePath(installation.HomeDirectory)] = installation;
        }

        foreach (var installation in incoming.Where(IsAvailable))
        {
            var normalizedPath = PathUtilities.NormalizePath(installation.HomeDirectory);

            if (!merged.TryGetValue(normalizedPath, out var current))
            {
                merged[normalizedPath] = installation;
                continue;
            }

            merged[normalizedPath] = installation with
            {
                Source = current.Source.Equals("import", StringComparison.OrdinalIgnoreCase) ? current.Source : installation.Source
            };
        }

        return InstallationIdentityUtilities.EnsureUniqueIds(merged.Values);
    }

    private static SelectionState NormalizeSelection(
        SelectionState selection,
        IReadOnlyList<ManagedInstallation> jdks,
        IReadOnlyList<ManagedInstallation> mavens)
    {
        var jdkId = jdks.Any(x => x.Id.Equals(selection.JdkId, StringComparison.OrdinalIgnoreCase))
            ? selection.JdkId
            : null;
        var mavenId = mavens.Any(x => x.Id.Equals(selection.MavenId, StringComparison.OrdinalIgnoreCase))
            ? selection.MavenId
            : null;

        return new SelectionState(jdkId, mavenId);
    }

    private static bool IsAvailable(ManagedInstallation installation)
    {
        try
        {
            return Directory.Exists(installation.HomeDirectory);
        }
        catch
        {
            return false;
        }
    }

    private static ManagerState BuildImportedJdkState(ManagerState state, ManagedInstallation installation)
    {
        var jdks = MergeInstallations(state.Jdks, new[] { installation });

        return state with
        {
            Jdks = jdks,
            ActiveSelection = NormalizeSelection(state.ActiveSelection, jdks, state.Mavens)
        };
    }

    private static ManagerState BuildImportedMavenState(ManagerState state, ManagedInstallation installation)
    {
        var mavens = MergeInstallations(state.Mavens, new[] { installation });

        return state with
        {
            Mavens = mavens,
            ActiveSelection = NormalizeSelection(state.ActiveSelection, state.Jdks, mavens)
        };
    }
}
