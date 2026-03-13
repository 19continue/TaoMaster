namespace TaoMaster.Core.Models;

public sealed record ActiveToolchainSelection(
    ManagedInstallation? Jdk,
    ManagedInstallation? Maven);
