namespace TaoMaster.Core.Models;

public sealed record JdkDownloadSourceConfiguration(
    string Id,
    string Name,
    string UrlPrefix,
    string SupportedProviders = "*",
    bool IsBuiltIn = false);
