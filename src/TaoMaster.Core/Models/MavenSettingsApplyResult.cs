namespace TaoMaster.Core.Models;

public sealed record MavenSettingsApplyResult(
    string SettingsFilePath,
    string LocalRepositoryPath,
    bool RepositoryMigrated,
    string? BackupFilePath);
