using System.Runtime.Versioning;
using TaoMaster.Core.Models;

namespace TaoMaster.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class SelectionActivationService
{
    private readonly ToolchainSelectionResolver _selectionResolver;
    private readonly WindowsUserEnvironmentService _environmentService;

    public SelectionActivationService(
        ToolchainSelectionResolver selectionResolver,
        WindowsUserEnvironmentService environmentService)
    {
        _selectionResolver = selectionResolver;
        _environmentService = environmentService;
    }

    public ActivationResult Apply(ManagerState state)
    {
        var selection = _selectionResolver.Resolve(state);
        EnsureSelectionExists(state, selection);

        ApplyJavaSelection(selection.Jdk, state.ActiveSelection.JdkId);
        ApplyMavenSelection(selection.Maven, state.ActiveSelection.MavenId);

        var userPath = _environmentService.BuildManagedUserPath(
            _environmentService.GetUserVariable(EnvironmentVariableNames.Path),
            includeJavaEntry: selection.Jdk is not null,
            includeMavenEntry: selection.Maven is not null);

        _environmentService.SetUserVariable(EnvironmentVariableNames.Path, userPath);
        _environmentService.BroadcastEnvironmentChanged();

        return new ActivationResult(
            selection,
            _environmentService.GetUserVariable(EnvironmentVariableNames.JavaHome),
            _environmentService.GetUserVariable(EnvironmentVariableNames.MavenHome),
            _environmentService.GetUserVariable(EnvironmentVariableNames.M2Home),
            userPath);
    }

    public string BuildShellScript(ManagerState state, string shellKind)
    {
        var selection = _selectionResolver.Resolve(state);
        EnsureSelectionExists(state, selection);

        return shellKind.ToLowerInvariant() switch
        {
            "powershell" => BuildPowerShellScript(selection),
            "cmd" => BuildCmdScript(selection),
            _ => throw new ArgumentException("仅支持 `powershell` 或 `cmd`。", nameof(shellKind))
        };
    }

    private void ApplyJavaSelection(ManagedInstallation? jdk, string? selectedJdkId)
    {
        if (jdk is not null)
        {
            _environmentService.SetUserVariable(EnvironmentVariableNames.JavaHome, jdk.HomeDirectory);
            _environmentService.SetUserVariable(EnvironmentVariableNames.ManagedJavaId, jdk.Id);
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedJdkId))
        {
            throw new InvalidOperationException($"当前选中的 JDK `{selectedJdkId}` 不存在。");
        }

        if (!string.IsNullOrWhiteSpace(_environmentService.GetUserVariable(EnvironmentVariableNames.ManagedJavaId)))
        {
            _environmentService.SetUserVariable(EnvironmentVariableNames.ManagedJavaId, null);
            _environmentService.SetUserVariable(EnvironmentVariableNames.JavaHome, null);
        }
    }

    private void ApplyMavenSelection(ManagedInstallation? maven, string? selectedMavenId)
    {
        if (maven is not null)
        {
            _environmentService.SetUserVariable(EnvironmentVariableNames.MavenHome, maven.HomeDirectory);
            _environmentService.SetUserVariable(EnvironmentVariableNames.M2Home, maven.HomeDirectory);
            _environmentService.SetUserVariable(EnvironmentVariableNames.ManagedMavenId, maven.Id);
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedMavenId))
        {
            throw new InvalidOperationException($"当前选中的 Maven `{selectedMavenId}` 不存在。");
        }

        if (!string.IsNullOrWhiteSpace(_environmentService.GetUserVariable(EnvironmentVariableNames.ManagedMavenId)))
        {
            _environmentService.SetUserVariable(EnvironmentVariableNames.ManagedMavenId, null);
            _environmentService.SetUserVariable(EnvironmentVariableNames.MavenHome, null);
            _environmentService.SetUserVariable(EnvironmentVariableNames.M2Home, null);
        }
    }

    private static void EnsureSelectionExists(ManagerState state, ActiveToolchainSelection selection)
    {
        if (!string.IsNullOrWhiteSpace(state.ActiveSelection.JdkId) && selection.Jdk is null)
        {
            throw new InvalidOperationException($"当前选中的 JDK `{state.ActiveSelection.JdkId}` 不存在。");
        }

        if (!string.IsNullOrWhiteSpace(state.ActiveSelection.MavenId) && selection.Maven is null)
        {
            throw new InvalidOperationException($"当前选中的 Maven `{state.ActiveSelection.MavenId}` 不存在。");
        }
    }

    private static string BuildPowerShellScript(ActiveToolchainSelection selection)
    {
        var lines = new List<string>
        {
            "$managedSegments = @()",
            "$segments = @()",
            "foreach ($segment in ($env:PATH -split ';')) {",
            "  if ([string]::IsNullOrWhiteSpace($segment)) { continue }",
            "  if ($segment -ieq '%JAVA_HOME%\\bin' -or $segment -ieq '%MAVEN_HOME%\\bin') { continue }",
            "  if ($env:JAVA_HOME -and $segment -ieq (Join-Path $env:JAVA_HOME 'bin')) { continue }",
            "  if ($env:MAVEN_HOME -and $segment -ieq (Join-Path $env:MAVEN_HOME 'bin')) { continue }",
            "  $segments += $segment",
            "}"
        };

        if (selection.Jdk is not null)
        {
            lines.Add($"$env:{EnvironmentVariableNames.JavaHome} = '{EscapePowerShell(selection.Jdk.HomeDirectory)}'");
            lines.Add($"$env:{EnvironmentVariableNames.ManagedJavaId} = '{EscapePowerShell(selection.Jdk.Id)}'");
            lines.Add("$managedSegments += (Join-Path $env:JAVA_HOME 'bin')");
        }
        else
        {
            lines.Add($"Remove-Item Env:{EnvironmentVariableNames.ManagedJavaId} -ErrorAction SilentlyContinue");
        }

        if (selection.Maven is not null)
        {
            lines.Add($"$env:{EnvironmentVariableNames.MavenHome} = '{EscapePowerShell(selection.Maven.HomeDirectory)}'");
            lines.Add($"$env:{EnvironmentVariableNames.M2Home} = '{EscapePowerShell(selection.Maven.HomeDirectory)}'");
            lines.Add($"$env:{EnvironmentVariableNames.ManagedMavenId} = '{EscapePowerShell(selection.Maven.Id)}'");
            lines.Add("$managedSegments += (Join-Path $env:MAVEN_HOME 'bin')");
        }
        else
        {
            lines.Add($"Remove-Item Env:{EnvironmentVariableNames.ManagedMavenId} -ErrorAction SilentlyContinue");
        }

        lines.Add("$env:PATH = (($managedSegments + $segments) -join ';')");

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildCmdScript(ActiveToolchainSelection selection)
    {
        var lines = new List<string>();

        if (selection.Jdk is not null)
        {
            lines.Add($"set \"{EnvironmentVariableNames.JavaHome}={selection.Jdk.HomeDirectory}\"");
            lines.Add($"set \"{EnvironmentVariableNames.ManagedJavaId}={selection.Jdk.Id}\"");
        }

        if (selection.Maven is not null)
        {
            lines.Add($"set \"{EnvironmentVariableNames.MavenHome}={selection.Maven.HomeDirectory}\"");
            lines.Add($"set \"{EnvironmentVariableNames.M2Home}={selection.Maven.HomeDirectory}\"");
            lines.Add($"set \"{EnvironmentVariableNames.ManagedMavenId}={selection.Maven.Id}\"");
        }

        var pathEntries = new List<string>();
        if (selection.Jdk is not null)
        {
            pathEntries.Add(Path.Combine(selection.Jdk.HomeDirectory, "bin"));
        }

        if (selection.Maven is not null)
        {
            pathEntries.Add(Path.Combine(selection.Maven.HomeDirectory, "bin"));
        }

        if (pathEntries.Count > 0)
        {
            lines.Add($"set \"{EnvironmentVariableNames.Path}={string.Join(";", pathEntries)};%PATH%\"");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapePowerShell(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
