namespace TaoMaster.Core.Models;

public sealed record MavenMirrorConfiguration(
    string Id,
    string Name,
    string Url,
    string MirrorOf,
    bool IsBuiltIn = false);
