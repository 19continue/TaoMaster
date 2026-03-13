namespace TaoMaster.Core.Models;

public sealed record MavenProbeResult(
    bool Success,
    int ExitCode,
    string Output,
    string? MavenHomeLine,
    string? JavaVersionLine);
