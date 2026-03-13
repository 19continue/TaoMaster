namespace TaoMaster.Core.Models;

public sealed record SelectionState(string? JdkId, string? MavenId)
{
    public static SelectionState Empty { get; } = new(null, null);
}
