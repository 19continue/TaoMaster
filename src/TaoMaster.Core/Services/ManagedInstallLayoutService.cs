using TaoMaster.Core.Models;
using TaoMaster.Core.Utilities;

namespace TaoMaster.Core.Services;

public sealed class ManagedInstallLayoutService
{
    public WorkspaceLayout Resolve(WorkspaceLayout layout, ManagerSettings settings)
    {
        var jdkRoot = NormalizeRoot(settings.ManagedJdkInstallRoot, layout.JdkRoot);
        var mavenRoot = NormalizeRoot(settings.ManagedMavenInstallRoot, layout.MavenRoot);

        return layout with
        {
            JdkRoot = jdkRoot,
            MavenRoot = mavenRoot
        };
    }

    public ManagedInstallRootMigrationResult MigrateManagedInstallRoots(
        ManagerState state,
        WorkspaceLayout baseLayout,
        string targetJdkRoot,
        string targetMavenRoot)
    {
        var currentLayout = Resolve(baseLayout, state.Settings);
        var normalizedTargetJdkRoot = NormalizeRoot(targetJdkRoot, baseLayout.JdkRoot);
        var normalizedTargetMavenRoot = NormalizeRoot(targetMavenRoot, baseLayout.MavenRoot);

        Directory.CreateDirectory(normalizedTargetJdkRoot);
        Directory.CreateDirectory(normalizedTargetMavenRoot);

        var (jdks, migratedJdks) = MigrateInstallations(
            state.Jdks,
            currentLayout.JdkRoot,
            normalizedTargetJdkRoot);
        var (mavens, migratedMavens) = MigrateInstallations(
            state.Mavens,
            currentLayout.MavenRoot,
            normalizedTargetMavenRoot);

        var updatedState = state with
        {
            Settings = state.Settings with
            {
                ManagedJdkInstallRoot = normalizedTargetJdkRoot,
                ManagedMavenInstallRoot = normalizedTargetMavenRoot
            },
            Jdks = jdks,
            Mavens = mavens
        };

        return new ManagedInstallRootMigrationResult(updatedState, migratedJdks, migratedMavens);
    }

    private static (IReadOnlyList<ManagedInstallation> Installations, int MigratedCount) MigrateInstallations(
        IReadOnlyList<ManagedInstallation> installations,
        string currentRoot,
        string targetRoot)
    {
        if (string.Equals(
                PathUtilities.NormalizePath(currentRoot),
                PathUtilities.NormalizePath(targetRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            return (installations, 0);
        }

        var migratedCount = 0;
        var updatedInstallations = new List<ManagedInstallation>(installations.Count);

        foreach (var installation in installations)
        {
            if (!installation.IsManaged || !Directory.Exists(installation.HomeDirectory))
            {
                updatedInstallations.Add(installation);
                continue;
            }

            var targetDirectory = BuildTargetDirectory(installation.HomeDirectory, currentRoot, targetRoot);
            if (string.Equals(
                    PathUtilities.NormalizePath(installation.HomeDirectory),
                    PathUtilities.NormalizePath(targetDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                updatedInstallations.Add(installation);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
            MoveDirectory(installation.HomeDirectory, targetDirectory);
            migratedCount++;

            updatedInstallations.Add(installation with
            {
                HomeDirectory = PathUtilities.NormalizePath(targetDirectory)
            });
        }

        return (updatedInstallations, migratedCount);
    }

    private static string BuildTargetDirectory(string sourceDirectory, string currentRoot, string targetRoot)
    {
        var normalizedSource = PathUtilities.NormalizePath(sourceDirectory);
        var normalizedCurrentRoot = PathUtilities.NormalizePath(currentRoot);
        var normalizedTargetRoot = PathUtilities.NormalizePath(targetRoot);

        var relativePath = PathUtilities.IsDescendantOrSelf(normalizedSource, normalizedCurrentRoot)
            ? Path.GetRelativePath(normalizedCurrentRoot, normalizedSource)
            : Path.GetFileName(normalizedSource);

        var candidate = Path.Combine(normalizedTargetRoot, relativePath);
        if (!Directory.Exists(candidate))
        {
            return candidate;
        }

        var index = 2;
        while (true)
        {
            var uniqueCandidate = $"{candidate}-{index}";
            if (!Directory.Exists(uniqueCandidate))
            {
                return uniqueCandidate;
            }

            index++;
        }
    }

    private static string NormalizeRoot(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return PathUtilities.NormalizePath(candidate);
    }

    private static void MoveDirectory(string sourceDirectory, string targetDirectory)
    {
        try
        {
            Directory.Move(sourceDirectory, targetDirectory);
            return;
        }
        catch (IOException)
        {
            CopyDirectory(sourceDirectory, targetDirectory);
            Directory.Delete(sourceDirectory, recursive: true);
        }
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);

        foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var targetFile = Path.Combine(targetPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }
}
