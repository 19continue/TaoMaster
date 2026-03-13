namespace TaoMaster.Core.Models;

public sealed record ShellIntegrationStatus(
    bool CmdAutoRunEnabled,
    bool PowerShellProfileEnabled,
    int PowerShellProfileCount,
    int PowerShellEnabledProfileCount,
    string CmdAutoRunCommand,
    string CmdWrapperPath,
    string CmdSessionScriptPath,
    string PowerShellSessionScriptPath)
{
    public bool IsEnabled => CmdAutoRunEnabled && PowerShellProfileEnabled;
}
