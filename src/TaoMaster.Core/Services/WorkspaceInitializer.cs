namespace TaoMaster.Core.Services;

public sealed class WorkspaceInitializer
{
    public void EnsureCreated(WorkspaceLayout layout)
    {
        var legacyRoot = GetLegacyWorkspaceRoot();
        MigrateLegacyWorkspaceIfNeeded(layout, legacyRoot);
        Directory.CreateDirectory(layout.RootDirectory);
        Directory.CreateDirectory(layout.JdkRoot);
        Directory.CreateDirectory(layout.MavenRoot);
        Directory.CreateDirectory(layout.CacheRoot);
        Directory.CreateDirectory(layout.TempRoot);
        Directory.CreateDirectory(layout.LogRoot);
        Directory.CreateDirectory(layout.ScriptRoot);
        NormalizeLegacyStatePaths(layout, legacyRoot);
    }

    private static void MigrateLegacyWorkspaceIfNeeded(WorkspaceLayout layout, string legacyRoot)
    {
        if (Directory.Exists(layout.RootDirectory) || !Directory.Exists(legacyRoot))
        {
            return;
        }

        try
        {
            Directory.Move(legacyRoot, layout.RootDirectory);
        }
        catch
        {
            // Best-effort migration: startup should still continue with a fresh workspace.
        }
    }

    private static void NormalizeLegacyStatePaths(WorkspaceLayout layout, string legacyRoot)
    {
        if (!File.Exists(layout.StateFile))
        {
            return;
        }

        try
        {
            var content = File.ReadAllText(layout.StateFile);
            var escapedLegacyRoot = JsonEscapePath(legacyRoot);
            var escapedCurrentRoot = JsonEscapePath(layout.RootDirectory);

            if (!content.Contains(legacyRoot, StringComparison.OrdinalIgnoreCase)
                && !content.Contains(escapedLegacyRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var normalized = content
                .Replace(legacyRoot, layout.RootDirectory, StringComparison.OrdinalIgnoreCase)
                .Replace(escapedLegacyRoot, escapedCurrentRoot, StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(content, normalized, StringComparison.Ordinal))
            {
                File.WriteAllText(layout.StateFile, normalized);
            }
        }
        catch
        {
            // Best-effort normalization: a stale state file should not block startup.
        }
    }

    private static string GetLegacyWorkspaceRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductInfo.LegacyWorkspaceDirectoryName);

    private static string JsonEscapePath(string path) =>
        path.Replace(@"\", @"\\", StringComparison.Ordinal);
}
