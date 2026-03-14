namespace TaoMaster.Core.Models;

public sealed record ManagedProject(
    string Id,
    string DisplayName,
    string ProjectDirectory,
    string? BoundJdkId,
    string? BoundMavenId,
    bool AutoApplyBindingsOnOpen,
    DateTimeOffset LastScannedAtUtc,
    ProjectDetectionSnapshot Detection);
