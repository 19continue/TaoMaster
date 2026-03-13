using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using TaoMaster.Core.Models;

namespace TaoMaster.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsShellIntegrationService
{
    private const string CommandProcessorRegistryPath = @"Software\Microsoft\Command Processor";
    private const string AutoRunValueName = "AutoRun";
    private const string PowerShellBeginMarker = "# >>> TaoMaster Shell Sync >>>";
    private const string PowerShellEndMarker = "# <<< TaoMaster Shell Sync <<<";
    private const string CmdWrapperFileName = "taomaster-cmd-autorun.cmd";
    private const string CmdSessionScriptFileName = "taomaster-shell-sync.cmd";
    private const string PowerShellSessionScriptFileName = "taomaster-shell-sync.ps1";
    private const string CmdOriginalAutoRunFileName = "taomaster-cmd-autorun-original.cmd";
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly SelectionActivationService _activationService;

    public WindowsShellIntegrationService(SelectionActivationService activationService)
    {
        _activationService = activationService;
    }

    public ShellIntegrationStatus EnsureEnabled(WorkspaceLayout layout, ManagerState state)
    {
        Directory.CreateDirectory(layout.ScriptRoot);

        var powerShellSessionScriptPath = Path.Combine(layout.ScriptRoot, PowerShellSessionScriptFileName);
        var cmdSessionScriptPath = Path.Combine(layout.ScriptRoot, CmdSessionScriptFileName);
        var cmdWrapperPath = Path.Combine(layout.ScriptRoot, CmdWrapperFileName);
        var cmdOriginalAutoRunPath = Path.Combine(layout.ScriptRoot, CmdOriginalAutoRunFileName);

        File.WriteAllText(powerShellSessionScriptPath, _activationService.BuildShellScript(state, "powershell"), Utf8WithoutBom);
        File.WriteAllText(cmdSessionScriptPath, _activationService.BuildShellScript(state, "cmd"), Utf8WithoutBom);

        var currentAutoRun = GetCmdAutoRunCommand();
        var expectedAutoRun = BuildExpectedCmdAutoRunCommand();
        var isManagedAutoRun = IsManagedCmdAutoRun(currentAutoRun);

        if (!isManagedAutoRun && !string.IsNullOrWhiteSpace(currentAutoRun))
        {
            File.WriteAllText(
                cmdOriginalAutoRunPath,
                string.Join(
                    Environment.NewLine,
                    "@echo off",
                    currentAutoRun.Trim()),
                Utf8WithoutBom);
        }

        WriteCmdWrapperScript(
            cmdWrapperPath,
            BuildCmdScriptReference(CmdSessionScriptFileName),
            File.Exists(cmdOriginalAutoRunPath) ? cmdOriginalAutoRunPath : null);

        SetCmdAutoRunCommand(expectedAutoRun);

        foreach (var profilePath in GetTargetPowerShellProfiles())
        {
            EnsurePowerShellProfile(profilePath);
        }

        return GetStatus(layout);
    }

    public ShellIntegrationStatus GetStatus(WorkspaceLayout layout)
    {
        var cmdWrapperPath = Path.Combine(layout.ScriptRoot, CmdWrapperFileName);
        var cmdSessionScriptPath = Path.Combine(layout.ScriptRoot, CmdSessionScriptFileName);
        var powerShellSessionScriptPath = Path.Combine(layout.ScriptRoot, PowerShellSessionScriptFileName);
        var currentAutoRun = GetCmdAutoRunCommand() ?? string.Empty;

        var powerShellProfiles = GetTargetPowerShellProfiles().ToList();
        var enabledProfiles = powerShellProfiles
            .Count(profilePath => ContainsPowerShellBlock(profilePath));

        return new ShellIntegrationStatus(
            CmdAutoRunEnabled: IsManagedCmdAutoRun(currentAutoRun),
            PowerShellProfileEnabled: enabledProfiles == powerShellProfiles.Count,
            PowerShellProfileCount: powerShellProfiles.Count,
            PowerShellEnabledProfileCount: enabledProfiles,
            CmdAutoRunCommand: currentAutoRun,
            CmdWrapperPath: cmdWrapperPath,
            CmdSessionScriptPath: cmdSessionScriptPath,
            PowerShellSessionScriptPath: powerShellSessionScriptPath);
    }

    private static IEnumerable<string> GetTargetPowerShellProfiles()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        yield return Path.Combine(documentsPath, "WindowsPowerShell", "profile.ps1");
        yield return Path.Combine(documentsPath, "PowerShell", "profile.ps1");
        yield return Path.Combine(documentsPath, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
        yield return Path.Combine(documentsPath, "PowerShell", "Microsoft.PowerShell_profile.ps1");
    }

    private static void EnsurePowerShellProfile(string profilePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);

        var block = string.Join(
            Environment.NewLine,
            PowerShellBeginMarker,
            $"$taoMasterShellSync = Join-Path $env:LOCALAPPDATA '{ProductInfo.WorkspaceDirectoryName}\\scripts\\{PowerShellSessionScriptFileName}'",
            "if (Test-Path $taoMasterShellSync) { . $taoMasterShellSync }",
            PowerShellEndMarker);

        var existingContent = File.Exists(profilePath)
            ? File.ReadAllText(profilePath, Utf8WithoutBom)
            : string.Empty;

        var updatedContent = UpsertPowerShellBlock(existingContent, block);
        File.WriteAllText(profilePath, updatedContent, Utf8WithoutBom);
    }

    private static string UpsertPowerShellBlock(string existingContent, string block)
    {
        var contentWithoutBlock = RemovePowerShellBlock(existingContent).TrimEnd();

        return string.IsNullOrWhiteSpace(contentWithoutBlock)
            ? block + Environment.NewLine
            : contentWithoutBlock + Environment.NewLine + Environment.NewLine + block + Environment.NewLine;
    }

    private static string RemovePowerShellBlock(string content)
    {
        var beginIndex = content.IndexOf(PowerShellBeginMarker, StringComparison.Ordinal);
        if (beginIndex < 0)
        {
            return content;
        }

        var endIndex = content.IndexOf(PowerShellEndMarker, beginIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return content;
        }

        var removalEnd = endIndex + PowerShellEndMarker.Length;
        while (removalEnd < content.Length && (content[removalEnd] == '\r' || content[removalEnd] == '\n'))
        {
            removalEnd++;
        }

        return content.Remove(beginIndex, removalEnd - beginIndex);
    }

    private static bool ContainsPowerShellBlock(string profilePath)
    {
        if (!File.Exists(profilePath))
        {
            return false;
        }

        var content = File.ReadAllText(profilePath, Utf8WithoutBom);
        return content.Contains(PowerShellBeginMarker, StringComparison.Ordinal)
               && content.Contains(PowerShellEndMarker, StringComparison.Ordinal);
    }

    private static void WriteCmdWrapperScript(string wrapperPath, string sessionScriptReference, string? originalAutoRunPath)
    {
        var lines = new List<string>
        {
            "@echo off"
        };

        if (!string.IsNullOrWhiteSpace(originalAutoRunPath))
        {
            lines.Add($"if exist \"{BuildCmdScriptReference(CmdOriginalAutoRunFileName)}\" call \"{BuildCmdScriptReference(CmdOriginalAutoRunFileName)}\"");
        }

        lines.Add($"if exist \"{sessionScriptReference}\" call \"{sessionScriptReference}\"");
        File.WriteAllText(wrapperPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, Utf8WithoutBom);
    }

    private static string BuildExpectedCmdAutoRunCommand() =>
        $"if exist \"{BuildCmdScriptReference(CmdWrapperFileName)}\" call \"{BuildCmdScriptReference(CmdWrapperFileName)}\"";

    private static bool IsManagedCmdAutoRun(string? currentAutoRun)
    {
        if (string.IsNullOrWhiteSpace(currentAutoRun))
        {
            return false;
        }

        return currentAutoRun.Contains(CmdWrapperFileName, StringComparison.OrdinalIgnoreCase)
               || currentAutoRun.Equals(BuildExpectedCmdAutoRunCommand(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetCmdAutoRunCommand()
    {
        using var commandProcessorKey = Registry.CurrentUser.OpenSubKey(CommandProcessorRegistryPath, writable: false);
        return commandProcessorKey?.GetValue(AutoRunValueName)?.ToString();
    }

    private static void SetCmdAutoRunCommand(string command)
    {
        using var commandProcessorKey = Registry.CurrentUser.CreateSubKey(CommandProcessorRegistryPath, writable: true)
                                        ?? throw new InvalidOperationException("Unable to open the HKCU Command Processor registry key.");
        commandProcessorKey.SetValue(AutoRunValueName, command, RegistryValueKind.String);
    }

    private static string BuildCmdScriptReference(string fileName) =>
        $@"%LOCALAPPDATA%\{ProductInfo.WorkspaceDirectoryName}\scripts\{fileName}";
}
