using TaoMaster.Core;

namespace TaoMaster.Core.Models;

public sealed record ManagerState(
    ManagerSettings Settings,
    SelectionState ActiveSelection,
    IReadOnlyList<ManagedInstallation> Jdks,
    IReadOnlyList<ManagedInstallation> Mavens,
    IReadOnlyList<ManagedProject> Projects,
    string? ActiveProjectId = null)
{
    public static ManagerState CreateDefault(WorkspaceLayout layout) =>
        new(
            ManagerSettings.CreateDefault(layout),
            SelectionState.Empty,
            Array.Empty<ManagedInstallation>(),
            Array.Empty<ManagedInstallation>(),
            Array.Empty<ManagedProject>(),
            null);
}
