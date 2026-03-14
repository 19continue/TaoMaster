namespace TaoMaster.App;

internal sealed record JdkToolchainListItem(
    string DisplayName,
    string Detail,
    string JdkHome,
    string Version,
    string? Vendor,
    string? Architecture,
    string? MatchedInstallationId);
