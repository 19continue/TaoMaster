namespace TaoMaster.Core.Models;

public sealed record InstallationRemovalResult(
    ManagerState State,
    ManagedInstallation Installation,
    bool DeletedFiles);
