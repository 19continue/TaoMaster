namespace TaoMaster.Core.Models;

public sealed record PackageInstallProgress(
    PackageInstallStage Stage,
    long BytesReceived = 0,
    long? TotalBytes = null);
