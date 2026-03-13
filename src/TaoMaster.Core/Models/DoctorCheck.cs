namespace TaoMaster.Core.Models;

public sealed record DoctorCheck(
    DoctorCheckStatus Status,
    string Code,
    string Message,
    string? Detail = null);
