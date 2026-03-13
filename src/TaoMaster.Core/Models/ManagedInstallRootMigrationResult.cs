namespace TaoMaster.Core.Models;

public sealed record ManagedInstallRootMigrationResult(
    ManagerState State,
    int MigratedJdks,
    int MigratedMavens);
