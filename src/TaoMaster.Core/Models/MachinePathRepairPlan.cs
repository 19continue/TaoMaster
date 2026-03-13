namespace TaoMaster.Core.Models;

public sealed record MachinePathRepairPlan(
    string CurrentPath,
    string UpdatedPath,
    IReadOnlyList<string> RemovedSegments,
    string PowerShellScript)
{
    public bool Changed => RemovedSegments.Count > 0;
}
