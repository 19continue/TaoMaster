namespace TaoMaster.Core.Models;

public sealed record DoctorReport(IReadOnlyList<DoctorCheck> Checks)
{
    public bool HasFailures => Checks.Any(x => x.Status == DoctorCheckStatus.Fail);
    public bool HasWarnings => Checks.Any(x => x.Status == DoctorCheckStatus.Warn);
}
