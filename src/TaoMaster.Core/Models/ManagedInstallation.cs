namespace TaoMaster.Core.Models;

public sealed record ManagedInstallation(
    string Id,
    ToolchainKind Kind,
    string DisplayName,
    string Version,
    string HomeDirectory,
    string Source,
    bool IsManaged,
    string? Vendor = null,
    string? Architecture = null);
