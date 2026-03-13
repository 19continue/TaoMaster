using TaoMaster.Core.Models;

namespace TaoMaster.Core.Utilities;

public static class InstallationIdentityUtilities
{
    public static IReadOnlyList<ManagedInstallation> EnsureUniqueIds(IEnumerable<ManagedInstallation> installations)
    {
        var ordered = installations
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.HomeDirectory, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var idCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<ManagedInstallation>(ordered.Count);

        foreach (var installation in ordered)
        {
            var nextCount = idCounts.TryGetValue(installation.Id, out var currentCount)
                ? currentCount + 1
                : 1;

            idCounts[installation.Id] = nextCount;

            unique.Add(nextCount == 1
                ? installation
                : installation with { Id = $"{installation.Id}-{nextCount}" });
        }

        return unique;
    }
}
