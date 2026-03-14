using TaoMaster.Core.Discovery;
using TaoMaster.Core.Models;
using TaoMaster.Core.Utilities;

namespace TaoMaster.Core.Services;

public sealed class InstallationCatalogService
{
    private readonly InstallationInspector _inspector;
    private readonly ProjectCatalogService _projectCatalogService;

    public InstallationCatalogService(InstallationInspector inspector)
    {
        _inspector = inspector;
        _projectCatalogService = new ProjectCatalogService();
    }

    public ManagerState MergeDiscovered(ManagerState state, DiscoverySnapshot snapshot)
    {
        var jdks = MergeInstallations(state.Jdks, snapshot.Jdks);
        var mavens = MergeInstallations(state.Mavens, snapshot.Mavens);

        return _projectCatalogService.NormalizeProjects(state with
        {
            Jdks = jdks,
            Mavens = mavens,
            ActiveSelection = NormalizeSelection(state.ActiveSelection, jdks, mavens)
        });
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

        return (updatedState, ResolveInstallationByHomeDirectory(updatedState, kind, installation.HomeDirectory));
    }

    public ManagerState RegisterInstallation(ManagerState state, ManagedInstallation installation) =>
        _projectCatalogService.NormalizeProjects(installation.Kind switch
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
        });

    public InstallationRemovalResult RemoveInstallation(
        ManagerState state,
        ToolchainKind kind,
        string id,
        WorkspaceLayout layout,
        bool deleteFiles)
    {
        var installation = FindInstallation(state, kind, id);

        if (deleteFiles)
        {
            EnsureManagedInstallationCanBeDeleted(installation, layout);

            if (Directory.Exists(installation.HomeDirectory))
            {
                Directory.Delete(installation.HomeDirectory, recursive: true);
            }
        }

        var updatedState = kind switch
        {
            ToolchainKind.Jdk => state with
            {
                Jdks = state.Jdks
                    .Where(item => !item.Id.Equals(installation.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                ActiveSelection = state.ActiveSelection with
                {
                    JdkId = state.ActiveSelection.JdkId != null
                            && state.ActiveSelection.JdkId.Equals(installation.Id, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : state.ActiveSelection.JdkId
                }
            },
            ToolchainKind.Maven => state with
            {
                Mavens = state.Mavens
                    .Where(item => !item.Id.Equals(installation.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                ActiveSelection = state.ActiveSelection with
                {
                    MavenId = state.ActiveSelection.MavenId != null
                              && state.ActiveSelection.MavenId.Equals(installation.Id, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : state.ActiveSelection.MavenId
                }
            },
            _ => state
        };

        return new InstallationRemovalResult(_projectCatalogService.NormalizeProjects(updatedState), installation, deleteFiles);
    }

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

    private ManagerState BuildImportedJdkState(ManagerState state, ManagedInstallation installation)
    {
        var jdks = MergeInstallations(state.Jdks, new[] { installation });

        return _projectCatalogService.NormalizeProjects(state with
        {
            Jdks = jdks,
            ActiveSelection = NormalizeSelection(state.ActiveSelection, jdks, state.Mavens)
        });
    }

    private ManagerState BuildImportedMavenState(ManagerState state, ManagedInstallation installation)
    {
        var mavens = MergeInstallations(state.Mavens, new[] { installation });

        return _projectCatalogService.NormalizeProjects(state with
        {
            Mavens = mavens,
            ActiveSelection = NormalizeSelection(state.ActiveSelection, state.Jdks, mavens)
        });
    }

    private static ManagedInstallation FindInstallation(ManagerState state, ToolchainKind kind, string id)
    {
        var installation = (kind switch
        {
            ToolchainKind.Jdk => state.Jdks,
            ToolchainKind.Maven => state.Mavens,
            _ => Array.Empty<ManagedInstallation>()
        }).FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        return installation
               ?? throw new ArgumentException($"No {kind} installation with ID `{id}` was found.", nameof(id));
    }

    private static ManagedInstallation ResolveInstallationByHomeDirectory(
        ManagerState state,
        ToolchainKind kind,
        string homeDirectory)
    {
        var normalizedHomeDirectory = PathUtilities.NormalizePath(homeDirectory);
        var installation = (kind switch
        {
            ToolchainKind.Jdk => state.Jdks,
            ToolchainKind.Maven => state.Mavens,
            _ => Array.Empty<ManagedInstallation>()
        }).FirstOrDefault(item =>
            PathUtilities.NormalizePath(item.HomeDirectory).Equals(normalizedHomeDirectory, StringComparison.OrdinalIgnoreCase));

        return installation
               ?? throw new InvalidOperationException($"The imported {kind} installation could not be resolved from state.");
    }

    private static void EnsureManagedInstallationCanBeDeleted(ManagedInstallation installation, WorkspaceLayout layout)
    {
        if (!installation.IsManaged)
        {
            throw new InvalidOperationException(
                $"Installation `{installation.Id}` is not managed by TaoMaster. Use remove to unregister it without deleting files.");
        }

        var expectedRoot = installation.Kind == ToolchainKind.Jdk
            ? layout.JdkRoot
            : layout.MavenRoot;

        if (!PathUtilities.IsDescendantOrSelf(installation.HomeDirectory, expectedRoot))
        {
            throw new InvalidOperationException(
                $"Installation `{installation.Id}` is outside the managed workspace root and cannot be deleted automatically.");
        }
    }
}
