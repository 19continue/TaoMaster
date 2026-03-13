namespace TaoMaster.Core.Models;

public sealed record ActivationResult(
    ActiveToolchainSelection Selection,
    string? UserJavaHome,
    string? UserMavenHome,
    string? UserM2Home,
    string UserPath);
