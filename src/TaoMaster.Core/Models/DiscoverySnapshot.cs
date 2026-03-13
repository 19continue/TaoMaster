namespace TaoMaster.Core.Models;

public sealed record DiscoverySnapshot(
    IReadOnlyList<ManagedInstallation> Jdks,
    IReadOnlyList<ManagedInstallation> Mavens)
{
    public static DiscoverySnapshot Empty { get; } =
        new(Array.Empty<ManagedInstallation>(), Array.Empty<ManagedInstallation>());
}
