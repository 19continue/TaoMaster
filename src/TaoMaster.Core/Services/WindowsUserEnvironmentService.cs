using System.ComponentModel;
using System.Collections;
using System.Diagnostics;
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

    private static bool IsManagedPathEntry(string segment) =>
        segment.Equals(EnvironmentVariableNames.ManagedJavaPathEntry, StringComparison.OrdinalIgnoreCase)
        || segment.Equals(EnvironmentVariableNames.ManagedMavenPathEntry, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldUseExpandString(string name, string value) =>
        name.Equals(EnvironmentVariableNames.Path, StringComparison.OrdinalIgnoreCase)
        || value.Contains('%', StringComparison.Ordinal);

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
