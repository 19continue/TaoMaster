using System.Runtime.Versioning;
using Microsoft.Win32;
using TaoMaster.Core.Models;
using TaoMaster.Core.Utilities;

namespace TaoMaster.Core.Discovery;

public sealed class LocalInstallationDiscoveryService
{
    private readonly InstallationInspector _inspector;

    public LocalInstallationDiscoveryService(InstallationInspector inspector)
    {
        _inspector = inspector;
    }

    public DiscoverySnapshot Discover(WorkspaceLayout layout)
    {
        var jdks = InstallationIdentityUtilities.EnsureUniqueIds(DiscoverJdks(layout));
        var mavens = InstallationIdentityUtilities.EnsureUniqueIds(DiscoverMavens(layout));

        return new DiscoverySnapshot(jdks, mavens);
    }

    private IReadOnlyList<ManagedInstallation> DiscoverJdks(WorkspaceLayout layout)
    {
        var candidates = new Dictionary<string, string>(PathUtilities.Comparer);

        AddEnvironmentCandidate(candidates, "JAVA_HOME", "env:JAVA_HOME");
        AddPathCandidates(candidates, "java.exe", "path");
        if (OperatingSystem.IsWindows())
        {
            AddRegistryJdkCandidates(candidates);
        }
        AddDirectoryCandidates(
            candidates,
            new[]
            {
                layout.JdkRoot,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BellSoft"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amazon Corretto")
            },
            _ => true,
            "directory");

        return candidates
            .OrderBy(entry => entry.Key, PathUtilities.Comparer)
            .Select(entry => _inspector.TryInspectJdkHome(entry.Key, entry.Value, layout, out var installation)
                ? installation
                : null)
            .OfType<ManagedInstallation>()
            .ToList();
    }

    private IReadOnlyList<ManagedInstallation> DiscoverMavens(WorkspaceLayout layout)
    {
        var candidates = new Dictionary<string, string>(PathUtilities.Comparer);

        AddEnvironmentCandidate(candidates, "MAVEN_HOME", "env:MAVEN_HOME");
        AddEnvironmentCandidate(candidates, "M2_HOME", "env:M2_HOME");
        AddPathCandidates(candidates, "mvn.cmd", "path");
        AddDirectoryCandidates(
            candidates,
            new[]
            {
                layout.MavenRoot,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"C:\tools"
            },
            directoryName => directoryName.Contains("maven", StringComparison.OrdinalIgnoreCase),
            "directory");

        return candidates
            .OrderBy(entry => entry.Key, PathUtilities.Comparer)
            .Select(entry => _inspector.TryInspectMavenHome(entry.Key, entry.Value, layout, out var installation)
                ? installation
                : null)
            .OfType<ManagedInstallation>()
            .ToList();
    }

    private static void AddEnvironmentCandidate(IDictionary<string, string> candidates, string variableName, string source)
    {
        foreach (var value in new[]
                 {
                     Environment.GetEnvironmentVariable(variableName),
                     Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User),
                     Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Machine)
                 })
        {
            AddCandidate(candidates, value, source);
        }
    }

    private static void AddPathCandidates(IDictionary<string, string> candidates, string executableName, string source)
    {
        var pathValues = new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
        };

        foreach (var pathValue in pathValues.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var segment in pathValue!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var expandedSegment = Environment.ExpandEnvironmentVariables(segment);
                if (string.IsNullOrWhiteSpace(expandedSegment) || !Directory.Exists(expandedSegment))
                {
                    continue;
                }

                var candidateExecutable = Path.Combine(expandedSegment, executableName);
                if (!File.Exists(candidateExecutable))
                {
                    continue;
                }

                var parent = Directory.GetParent(expandedSegment);
                if (parent is null)
                {
                    continue;
                }

                AddCandidate(candidates, parent.FullName, $"{source}:{expandedSegment}");
            }
        }
    }

    private static void AddDirectoryCandidates(
        IDictionary<string, string> candidates,
        IEnumerable<string> roots,
        Func<string, bool> directoryNameFilter,
        string source)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            AddCandidate(candidates, root, $"{source}:{root}");

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                if (!directoryNameFilter(name))
                {
                    continue;
                }

                AddCandidate(candidates, directory, $"{source}:{root}");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistryJdkCandidates(IDictionary<string, string> candidates)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var keyPath in new[]
                         {
                             @"SOFTWARE\JavaSoft\JDK",
                             @"SOFTWARE\JavaSoft\Java Development Kit"
                         })
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                        using var rootKey = baseKey.OpenSubKey(keyPath);
                        if (rootKey is null)
                        {
                            continue;
                        }

                        foreach (var versionKeyName in rootKey.GetSubKeyNames())
                        {
                            using var versionKey = rootKey.OpenSubKey(versionKeyName);
                            var javaHome = versionKey?.GetValue("JavaHome") as string;
                            AddCandidate(candidates, javaHome, $"registry:{hive}:{keyPath}");
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    private static void AddCandidate(IDictionary<string, string> candidates, string? path, string source)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var normalized = PathUtilities.NormalizePath(Environment.ExpandEnvironmentVariables(path));
            candidates.TryAdd(normalized, source);
        }
        catch
        {
        }
    }
}
