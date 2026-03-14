namespace TaoMaster.Core.Models;

public sealed record MavenJdkToolchainConfiguration(
    string JdkHome,
    string Version,
    string? Vendor = null,
    string? Architecture = null);
