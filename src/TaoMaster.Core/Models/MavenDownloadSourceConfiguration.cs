namespace TaoMaster.Core.Models;

public sealed record MavenDownloadSourceConfiguration(
    string Id,
    string Name,
    string BaseUrl,
    bool IsBuiltIn = false);
