namespace TaoMaster.Core.Models;

public sealed record UserPathRepairResult(
    string UpdatedPath,
    IReadOnlyList<string> RemovedSegments)
{
    public bool Changed => RemovedSegments.Count > 0;
}
