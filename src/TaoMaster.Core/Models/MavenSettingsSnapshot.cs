namespace TaoMaster.Core.Models;

public sealed record MavenSettingsSnapshot(
    string SettingsFilePath,
    string LocalRepositoryPath,
    IReadOnlyList<MavenMirrorConfiguration> Mirrors);
