namespace TaoMaster.Core.Models;

public sealed record RemotePackageDescriptor(
    ToolchainKind Kind,
    string Provider,
    string DisplayName,
    string Version,
    string DownloadUrl,
    string FileName,
    string Checksum,
    string ChecksumAlgorithm,
    string SuggestedInstallDirectoryName,
    string? Architecture = null);
