using System.ComponentModel;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using TaoMaster.Core.Models;

namespace TaoMaster.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsUserEnvironmentService
{
    private const string UserEnvironmentRegistryPath = @"Environment";
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = new(0xffff);

    public string? GetUserVariable(string name)
    {
        using var environmentKey = Registry.CurrentUser.OpenSubKey(UserEnvironmentRegistryPath, writable: false);
        return environmentKey?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
    }

    public string? GetMachineVariable(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);

    public void SetUserVariable(string name, string? value)
    {
        using var environmentKey = Registry.CurrentUser.CreateSubKey(UserEnvironmentRegistryPath, writable: true)
                                   ?? throw new InvalidOperationException("无法打开用户环境变量注册表项。");

        if (string.IsNullOrWhiteSpace(value))
        {
            environmentKey.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            var valueKind = ShouldUseExpandString(name, value)
                ? RegistryValueKind.ExpandString
                : RegistryValueKind.String;
            environmentKey.SetValue(name, value, valueKind);
        }

        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }

    public string BuildManagedUserPath(string? existingUserPath, bool includeJavaEntry, bool includeMavenEntry)
    {
        var remainingSegments = SplitPath(existingUserPath)
            .Where(segment => !IsManagedPathEntry(segment))
            .ToList();

        var managedSegments = new List<string>(2);
        if (includeJavaEntry)
        {
            managedSegments.Add(EnvironmentVariableNames.ManagedJavaPathEntry);
        }

        if (includeMavenEntry)
        {
            managedSegments.Add(EnvironmentVariableNames.ManagedMavenPathEntry);
        }

        return string.Join(";", managedSegments.Concat(remainingSegments));
    }

    public string BuildEffectivePathForNewProcesses(string? userPathOverride = null)
    {
        var machinePath = GetMachineVariable(EnvironmentVariableNames.Path);
        var userPath = userPathOverride ?? GetUserVariable(EnvironmentVariableNames.Path);

        return string.Join(
            ";",
            new[] { machinePath, userPath }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public IReadOnlyList<ResolvedExecutableCandidate> FindExecutableCandidates(
        string executableName,
        string? userPathOverride,
        IReadOnlyDictionary<string, string?> variables)
    {
        var matches = new List<ResolvedExecutableCandidate>();
        var segmentIndex = 0;

        AddExecutableCandidates(
            matches,
            executableName,
            GetMachineVariable(EnvironmentVariableNames.Path),
            EnvironmentPathScope.Machine,
            variables,
            ref segmentIndex);

        AddExecutableCandidates(
            matches,
            executableName,
            userPathOverride ?? GetUserVariable(EnvironmentVariableNames.Path),
            EnvironmentPathScope.User,
            variables,
            ref segmentIndex);

        return matches;
    }

    public string? ResolveExecutable(
        string executableName,
        string effectivePath,
        IReadOnlyDictionary<string, string?> variables)
    {
        foreach (var segment in SplitPath(effectivePath))
        {
            var expandedSegment = ExpandVariables(segment, variables);
            if (string.IsNullOrWhiteSpace(expandedSegment) || !Directory.Exists(expandedSegment))
            {
                continue;
            }

            var candidate = Path.Combine(expandedSegment, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public UserPathRepairResult RepairUserPathForManagedToolchains(
        string? existingUserPath,
        bool includeJavaEntry,
        bool includeMavenEntry)
    {
        var removedSegments = new List<string>();
        var retainedSegments = new List<string>();
        var variables = BuildVariableMap(
            GetUserVariable(EnvironmentVariableNames.JavaHome),
            GetUserVariable(EnvironmentVariableNames.MavenHome),
            GetUserVariable(EnvironmentVariableNames.M2Home));

        foreach (var segment in SplitPath(existingUserPath))
        {
            if (IsManagedPathEntry(segment) || IsDirectToolchainPath(segment, variables))
            {
                removedSegments.Add(segment);
                continue;
            }

            retainedSegments.Add(segment);
        }

        var retainedPath = string.Join(";", retainedSegments);
        var updatedPath = BuildManagedUserPath(retainedPath, includeJavaEntry, includeMavenEntry);

        return new UserPathRepairResult(updatedPath, removedSegments);
    }

    public MachinePathRepairPlan BuildMachinePathRepairPlan(
        string? selectedJdkHome,
        string? selectedMavenHome,
        string? userPathOverride = null)
    {
        var currentMachinePath = GetMachineVariable(EnvironmentVariableNames.Path) ?? string.Empty;
        var userPath = userPathOverride ?? GetUserVariable(EnvironmentVariableNames.Path);
        var variableMap = BuildVariableMap(
            selectedJdkHome ?? GetUserVariable(EnvironmentVariableNames.JavaHome),
            selectedMavenHome ?? GetUserVariable(EnvironmentVariableNames.MavenHome),
            selectedMavenHome ?? GetUserVariable(EnvironmentVariableNames.M2Home));

        var removedSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(selectedJdkHome))
        {
            foreach (var candidate in FindExecutableCandidates("java.exe", userPath, variableMap)
                         .Where(candidate => candidate.Scope == EnvironmentPathScope.Machine))
            {
                removedSegments.Add(candidate.OriginalPathSegment);
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedMavenHome))
        {
            foreach (var candidate in FindExecutableCandidates("mvn.cmd", userPath, variableMap)
                         .Where(candidate => candidate.Scope == EnvironmentPathScope.Machine))
            {
                removedSegments.Add(candidate.OriginalPathSegment);
            }
        }

        var retainedSegments = SplitPath(currentMachinePath)
            .Where(segment => !removedSegments.Contains(segment))
            .ToList();

        var updatedPath = string.Join(";", retainedSegments);
        var orderedRemovedSegments = removedSegments
            .OrderBy(segment => segment, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MachinePathRepairPlan(
            currentMachinePath,
            updatedPath,
            orderedRemovedSegments,
            BuildMachinePathRepairScript(updatedPath, orderedRemovedSegments));
    }

    public string BuildProcessPath(string effectivePath, string? selectedJdkHome, string? selectedMavenHome)
    {
        var segments = SplitPath(effectivePath)
            .Where(segment => !IsManagedPathEntry(segment))
            .ToList();

        if (!string.IsNullOrWhiteSpace(selectedMavenHome))
        {
            segments.Insert(0, Path.Combine(selectedMavenHome, "bin"));
        }

        if (!string.IsNullOrWhiteSpace(selectedJdkHome))
        {
            segments.Insert(0, Path.Combine(selectedJdkHome, "bin"));
        }

        return string.Join(";", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    public IReadOnlyDictionary<string, string?> BuildVariableMap(
        string? javaHome,
        string? mavenHome,
        string? m2Home)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            values[entry.Key.ToString() ?? string.Empty] = entry.Value?.ToString();
        }

        values[EnvironmentVariableNames.JavaHome] = javaHome;
        values[EnvironmentVariableNames.MavenHome] = mavenHome;
        values[EnvironmentVariableNames.M2Home] = m2Home;

        return values;
    }

    public string ExpandVariables(string value, IReadOnlyDictionary<string, string?> variables)
    {
        var expanded = value;

        foreach (var pair in variables.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
        {
            expanded = expanded.Replace(
                $"%{pair.Key}%",
                pair.Value ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        return expanded;
    }

    public MavenProbeResult RunMavenProbe(
        string jdkHome,
        string mavenHome,
        string effectivePath,
        TimeSpan timeout)
    {
        var mvnCmd = Path.Combine(mavenHome, "bin", "mvn.cmd");
        if (!File.Exists(mvnCmd))
        {
            return new MavenProbeResult(false, -1, "mvn.cmd 不存在。", null, null);
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c \"\"{mvnCmd}\" -v\"",
            WorkingDirectory = mavenHome,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        processStartInfo.Environment[EnvironmentVariableNames.JavaHome] = jdkHome;
        processStartInfo.Environment[EnvironmentVariableNames.MavenHome] = mavenHome;
        processStartInfo.Environment[EnvironmentVariableNames.M2Home] = mavenHome;
        processStartInfo.Environment[EnvironmentVariableNames.Path] = BuildProcessPath(effectivePath, jdkHome, mavenHome);

        using var process = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException("无法启动 Maven 诊断进程。");

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return new MavenProbeResult(false, -1, "执行 `mvn -v` 超时。", null, null);
        }

        var output = (process.StandardOutput.ReadToEnd() + Environment.NewLine + process.StandardError.ReadToEnd()).Trim();
        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var mavenHomeLine = lines.FirstOrDefault(line => line.StartsWith("Maven home:", StringComparison.OrdinalIgnoreCase));
        var javaVersionLine = lines.FirstOrDefault(line => line.StartsWith("Java version:", StringComparison.OrdinalIgnoreCase));
        var success = process.ExitCode == 0;

        return new MavenProbeResult(success, process.ExitCode, output, mavenHomeLine, javaVersionLine);
    }

    public void BroadcastEnvironmentChanged()
    {
        if (SendNotifyMessage(HwndBroadcast, WmSettingChange, UIntPtr.Zero, "Environment"))
        {
            return;
        }

        if (!SendMessageTimeout(HwndBroadcast, WmSettingChange, UIntPtr.Zero, "Environment", SmtoAbortIfHung, 200, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "广播环境变量变更失败。");
        }
    }

    private static IReadOnlyList<string> SplitPath(string? pathValue) =>
        (pathValue ?? string.Empty)
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(segment => !string.IsNullOrWhiteSpace(segment))
        .ToList();

    private static void AddExecutableCandidates(
        ICollection<ResolvedExecutableCandidate> matches,
        string executableName,
        string? pathValue,
        EnvironmentPathScope scope,
        IReadOnlyDictionary<string, string?> variables,
        ref int segmentIndex)
    {
        foreach (var segment in SplitPath(pathValue))
        {
            var currentIndex = segmentIndex++;
            var expandedSegment = ExpandPathSegment(segment, variables);

            if (string.IsNullOrWhiteSpace(expandedSegment) || !Directory.Exists(expandedSegment))
            {
                continue;
            }

            var candidatePath = Path.Combine(expandedSegment, executableName);
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            matches.Add(new ResolvedExecutableCandidate(
                executableName,
                candidatePath,
                segment,
                expandedSegment,
                scope,
                currentIndex));
        }
    }

    private static bool IsManagedPathEntry(string segment) =>
        segment.Equals(EnvironmentVariableNames.ManagedJavaPathEntry, StringComparison.OrdinalIgnoreCase)
        || segment.Equals(EnvironmentVariableNames.ManagedMavenPathEntry, StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectToolchainPath(
        string segment,
        IReadOnlyDictionary<string, string?> variables)
    {
        var expandedSegment = ExpandPathSegment(segment, variables);
        if (string.IsNullOrWhiteSpace(expandedSegment))
        {
            return false;
        }

        if (!Directory.Exists(expandedSegment))
        {
            return LooksLikeToolchainPath(expandedSegment);
        }

        return File.Exists(Path.Combine(expandedSegment, "java.exe"))
               || File.Exists(Path.Combine(expandedSegment, "javac.exe"))
               || File.Exists(Path.Combine(expandedSegment, "mvn.cmd"))
               || File.Exists(Path.Combine(expandedSegment, "mvn.bat"))
               || LooksLikeToolchainPath(expandedSegment);
    }

    private static bool ShouldUseExpandString(string name, string value) =>
        name.Equals(EnvironmentVariableNames.Path, StringComparison.OrdinalIgnoreCase)
        || value.Contains('%', StringComparison.Ordinal);

    private static string ExpandPathSegment(
        string segment,
        IReadOnlyDictionary<string, string?> variables)
    {
        var expanded = segment;

        foreach (var pair in variables.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
        {
            expanded = expanded.Replace(
                $"%{pair.Key}%",
                pair.Value ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        return Environment.ExpandEnvironmentVariables(expanded);
    }

    private static bool LooksLikeToolchainPath(string path)
    {
        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        var leaf = Path.GetFileName(normalized);
        var parent = Path.GetFileName(Path.GetDirectoryName(normalized) ?? string.Empty);

        return leaf.Equals("javapath", StringComparison.OrdinalIgnoreCase)
               || (leaf.Equals("bin", StringComparison.OrdinalIgnoreCase)
                   && (parent.Contains("jdk", StringComparison.OrdinalIgnoreCase)
                       || parent.Contains("jre", StringComparison.OrdinalIgnoreCase)
                       || parent.Contains("maven", StringComparison.OrdinalIgnoreCase)
                       || normalized.Contains(@"\jdks\", StringComparison.OrdinalIgnoreCase)
                       || normalized.Contains(@"\mavens\", StringComparison.OrdinalIgnoreCase)
                       || normalized.Contains("apache-maven", StringComparison.OrdinalIgnoreCase)
                       || normalized.Contains(@"\java\", StringComparison.OrdinalIgnoreCase)));
    }

    private static string BuildMachinePathRepairScript(
        string updatedPath,
        IReadOnlyList<string> removedSegments)
    {
        if (removedSegments.Count == 0)
        {
            return "Write-Host 'No conflicting machine PATH entries were detected.'";
        }

        var escapedUpdatedPath = EscapePowerShellLiteral(updatedPath);
        var backupStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        var lines = new List<string>
        {
            "$ErrorActionPreference = 'Stop'",
            "$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())",
            "if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script in an elevated PowerShell session.' }",
            "$backupFile = Join-Path $env:TEMP 'taomaster-machine-path-backup-" + backupStamp + ".txt'",
            "$registryPath = 'SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment'",
            "$environmentKey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($registryPath, $true)",
            "if ($null -eq $environmentKey) { throw 'Unable to open HKLM machine environment registry key.' }",
            "$currentPath = $environmentKey.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)",
            "Set-Content -Path $backupFile -Value $currentPath -Encoding UTF8",
            "$newPath = '" + escapedUpdatedPath + "'",
            "$environmentKey.SetValue('Path', $newPath, [Microsoft.Win32.RegistryValueKind]::ExpandString)",
            "$environmentKey.Close()",
            "Add-Type -TypeDefinition @'",
            "using System;",
            "using System.Runtime.InteropServices;",
            "public static class TaoMasterNativeMethods",
            "{",
            "    [DllImport(\"user32.dll\", SetLastError = true, CharSet = CharSet.Unicode)]",
            "    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);",
            "}",
            "'@",
            "$result = [UIntPtr]::Zero",
            "[void][TaoMasterNativeMethods]::SendMessageTimeout([IntPtr]0xffff, 0x001A, [UIntPtr]::Zero, 'Environment', 0x0002, 200, [ref]$result)",
            "Write-Host 'Removed machine PATH entries:'"
        };

        foreach (var segment in removedSegments)
        {
            lines.Add("Write-Host ' - " + EscapePowerShellLiteral(segment) + "'");
        }

        lines.Add("Write-Host ('Backup saved to: ' + $backupFile)");
        lines.Add("Write-Host 'Restart terminals and IDEs that were already open.'");

        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SendNotifyMessage(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        string lParam);
}
