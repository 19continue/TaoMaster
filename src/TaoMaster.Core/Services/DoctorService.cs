using System.Runtime.Versioning;
using TaoMaster.Core.Models;
using TaoMaster.Core.Utilities;

namespace TaoMaster.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class DoctorService
{
    private readonly ToolchainSelectionResolver _selectionResolver;
    private readonly WindowsUserEnvironmentService _environmentService;

    public DoctorService(
        ToolchainSelectionResolver selectionResolver,
        WindowsUserEnvironmentService environmentService)
    {
        _selectionResolver = selectionResolver;
        _environmentService = environmentService;
    }

    public DoctorReport Run(ManagerState state)
    {
        var checks = new List<DoctorCheck>();
        var selection = _selectionResolver.Resolve(state);

        AddSelectionChecks(checks, state, selection);

        var userJavaHome = _environmentService.GetUserVariable(EnvironmentVariableNames.JavaHome);
        var userMavenHome = _environmentService.GetUserVariable(EnvironmentVariableNames.MavenHome);
        var userM2Home = _environmentService.GetUserVariable(EnvironmentVariableNames.M2Home);
        var userPath = _environmentService.GetUserVariable(EnvironmentVariableNames.Path);
        var expectedUserPath = _environmentService.BuildManagedUserPath(
            userPath,
            includeJavaEntry: selection.Jdk is not null,
            includeMavenEntry: selection.Maven is not null);

        if (selection.Jdk is not null)
        {
            checks.Add(PathUtilities.Comparer.Equals(selection.Jdk.HomeDirectory, userJavaHome)
                ? new DoctorCheck(DoctorCheckStatus.Pass, "java-home", "用户级 JAVA_HOME 与当前选中 JDK 一致。", userJavaHome)
                : new DoctorCheck(DoctorCheckStatus.Fail, "java-home", "用户级 JAVA_HOME 与当前选中 JDK 不一致。", $"期望: {selection.Jdk.HomeDirectory}，实际: {userJavaHome ?? "(空)"}"));
        }
        else
        {
            checks.Add(new DoctorCheck(DoctorCheckStatus.Warn, "java-home", "当前没有选中的 JDK。"));
        }

        if (selection.Maven is not null)
        {
            checks.Add(PathUtilities.Comparer.Equals(selection.Maven.HomeDirectory, userMavenHome)
                ? new DoctorCheck(DoctorCheckStatus.Pass, "maven-home", "用户级 MAVEN_HOME 与当前选中 Maven 一致。", userMavenHome)
                : new DoctorCheck(DoctorCheckStatus.Fail, "maven-home", "用户级 MAVEN_HOME 与当前选中 Maven 不一致。", $"期望: {selection.Maven.HomeDirectory}，实际: {userMavenHome ?? "(空)"}"));

            checks.Add(PathUtilities.Comparer.Equals(selection.Maven.HomeDirectory, userM2Home)
                ? new DoctorCheck(DoctorCheckStatus.Pass, "m2-home", "用户级 M2_HOME 与当前选中 Maven 一致。", userM2Home)
                : new DoctorCheck(DoctorCheckStatus.Fail, "m2-home", "用户级 M2_HOME 与当前选中 Maven 不一致。", $"期望: {selection.Maven.HomeDirectory}，实际: {userM2Home ?? "(空)"}"));
        }
        else
        {
            checks.Add(new DoctorCheck(DoctorCheckStatus.Warn, "maven-home", "当前没有选中的 Maven。"));
        }

        checks.Add(
            string.Equals(userPath ?? string.Empty, expectedUserPath, StringComparison.OrdinalIgnoreCase)
                ? new DoctorCheck(DoctorCheckStatus.Pass, "user-path", "用户级 PATH 里的受控入口顺序正确。")
                : new DoctorCheck(DoctorCheckStatus.Fail, "user-path", "用户级 PATH 里的受控入口缺失、重复或顺序不正确。", $"期望 PATH 前缀: {expectedUserPath}"));

        var effectivePath = _environmentService.BuildEffectivePathForNewProcesses(expectedUserPath);
        var variableMap = _environmentService.BuildVariableMap(
            selection.Jdk?.HomeDirectory ?? userJavaHome,
            selection.Maven?.HomeDirectory ?? userMavenHome,
            selection.Maven?.HomeDirectory ?? userM2Home);

        if (selection.Jdk is not null)
        {
            var resolvedJava = _environmentService.ResolveExecutable("java.exe", effectivePath, variableMap);
            checks.Add(ResolvedExecutableCheck(
                "java-resolve",
                "java.exe",
                resolvedJava,
                Path.Combine(selection.Jdk.HomeDirectory, "bin", "java.exe")));
        }

        if (selection.Maven is not null)
        {
            var resolvedMaven = _environmentService.ResolveExecutable("mvn.cmd", effectivePath, variableMap);
            checks.Add(ResolvedExecutableCheck(
                "maven-resolve",
                "mvn.cmd",
                resolvedMaven,
                Path.Combine(selection.Maven.HomeDirectory, "bin", "mvn.cmd")));
        }

        if (selection.Jdk is not null && selection.Maven is not null)
        {
            var probe = _environmentService.RunMavenProbe(
                selection.Jdk.HomeDirectory,
                selection.Maven.HomeDirectory,
                effectivePath,
                TimeSpan.FromSeconds(20));

            checks.Add(probe.Success
                ? new DoctorCheck(
                    DoctorCheckStatus.Pass,
                    "maven-probe",
                    "选中的 Maven 能够使用当前选中的 JDK 启动。",
                    probe.JavaVersionLine ?? probe.Output)
                : new DoctorCheck(
                    DoctorCheckStatus.Fail,
                    "maven-probe",
                    "选中的 Maven 无法使用当前选中的 JDK 正常启动。",
                    probe.Output));
        }

        return new DoctorReport(checks);
    }

    private static void AddSelectionChecks(
        ICollection<DoctorCheck> checks,
        ManagerState state,
        ActiveToolchainSelection selection)
    {
        checks.Add(string.IsNullOrWhiteSpace(state.ActiveSelection.JdkId)
            ? new DoctorCheck(DoctorCheckStatus.Warn, "selected-jdk", "状态文件中还没有选中的 JDK。")
            : selection.Jdk is not null
                ? new DoctorCheck(DoctorCheckStatus.Pass, "selected-jdk", "状态文件中的当前 JDK 选择有效。", selection.Jdk.DisplayName)
                : new DoctorCheck(DoctorCheckStatus.Fail, "selected-jdk", "状态文件中的 JDK 选择已失效。", state.ActiveSelection.JdkId));

        checks.Add(string.IsNullOrWhiteSpace(state.ActiveSelection.MavenId)
            ? new DoctorCheck(DoctorCheckStatus.Warn, "selected-maven", "状态文件中还没有选中的 Maven。")
            : selection.Maven is not null
                ? new DoctorCheck(DoctorCheckStatus.Pass, "selected-maven", "状态文件中的当前 Maven 选择有效。", selection.Maven.DisplayName)
                : new DoctorCheck(DoctorCheckStatus.Fail, "selected-maven", "状态文件中的 Maven 选择已失效。", state.ActiveSelection.MavenId));
    }

    private static DoctorCheck ResolvedExecutableCheck(
        string code,
        string executableName,
        string? actualPath,
        string expectedPath)
    {
        if (string.IsNullOrWhiteSpace(actualPath))
        {
            return new DoctorCheck(DoctorCheckStatus.Fail, code, $"未能在新进程 PATH 中解析到 {executableName}。");
        }

        return PathUtilities.Comparer.Equals(actualPath, expectedPath)
            ? new DoctorCheck(DoctorCheckStatus.Pass, code, $"{executableName} 在新进程 PATH 中解析正确。", actualPath)
            : new DoctorCheck(DoctorCheckStatus.Warn, code, $"{executableName} 在新进程 PATH 中解析到了其他位置。", $"期望: {expectedPath}，实际: {actualPath}");
    }
}
