using TaoMaster.Core;

namespace TaoMaster.Core.Models;

public sealed record ManagerState(
    ManagerSettings Settings,
    SelectionState ActiveSelection,
    IReadOnlyList<ManagedInstallation> Jdks,
    IReadOnlyList<ManagedInstallation> Mavens)
{
    public static ManagerState CreateDefault(WorkspaceLayout layout) =>
        new(
            ManagerSettings.CreateDefault(layout),
            SelectionState.Empty,
            Array.Empty<ManagedInstallation>(),
            Array.Empty<ManagedInstallation>());
}
