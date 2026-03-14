namespace TaoMaster.Core.Models;

public sealed record ManagedProject(
    string Id,
    string DisplayName,
    string ProjectDirectory,
    string? BoundJdkId,
    string? BoundMavenId,
    DateTimeOffset LastScannedAtUtc,
    ProjectDetectionSnapshot Detection);
