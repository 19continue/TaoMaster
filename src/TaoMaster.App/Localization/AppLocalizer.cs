using System.Globalization;
using TaoMaster.Core;
using TaoMaster.Core.Models;

namespace TaoMaster.App.Localization;

public enum AppLanguage
{
    English,
    SimplifiedChinese
}

public sealed record LanguageOption(AppLanguage Language, string DisplayName);

public sealed class AppLocalizer
{
    private static readonly IReadOnlyList<LanguageOption> SupportedLanguageList =
    [
        new LanguageOption(AppLanguage.English, "English"),
        new LanguageOption(AppLanguage.SimplifiedChinese, "简体中文")
    ];

    private static readonly IReadOnlyDictionary<string, string> EnglishStrings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["windowTitle"] = ProductInfo.EnglishName,
            ["headerSubtitle"] = "Windows toolchain manager for JDK and Maven",
            ["headerDescription"] = "Local discovery, managed installs, global switching, and Maven/JDK consistency checks in one desktop surface.",
            ["workspaceRootLabel"] = "Workspace Root",
            ["stateFileLabel"] = "State File",
            ["languageLabel"] = "Language",
            ["activeJdkTitle"] = "Active JDK",
            ["activeMavenTitle"] = "Active Maven",
            ["javaHomeLabel"] = "JAVA_HOME",
            ["mavenHomeLabel"] = "MAVEN_HOME / M2_HOME",
            ["installedJdksTitle"] = "Installed JDKs",
            ["installedJdksDescription"] = "Sync local installations, import an existing folder, switch to a selected JDK, or remove/uninstall managed entries.",
            ["syncLocalButton"] = "Sync Local",
            ["useSelectedJdkButton"] = "Use Selected JDK",
            ["removeSelectedButton"] = "Remove Selected",
            ["uninstallSelectedButton"] = "Uninstall Selected",
            ["browseButton"] = "Browse",
            ["importJdkButton"] = "Import JDK",
            ["installedMavensTitle"] = "Installed Mavens",
            ["installedMavensDescription"] = "Import an existing Maven home, switch to a selected Maven distribution, or remove/uninstall managed entries.",
            ["useSelectedMavenButton"] = "Use Selected Maven",
            ["importMavenButton"] = "Import Maven",
            ["remoteInstallTitle"] = "Install From Remote",
            ["remoteInstallDescription"] = "Fetch official Temurin JDK and Apache Maven ZIP distributions, verify checksums, extract into the managed workspace, and optionally switch immediately.",
            ["temurinJdkLabel"] = "Temurin JDK",
            ["apacheMavenLabel"] = "Apache Maven",
            ["installButton"] = "Install",
            ["installUseButton"] = "Install + Use",
            ["doctorTitle"] = "Doctor",
            ["doctorDescription"] = "Validate the active selections, user-scoped environment variables, PATH entries, and Maven/JDK runtime consistency.",
            ["runDoctorButton"] = "Run Doctor",
            ["repairUserPathButton"] = "Repair User PATH",
            ["copyMachinePathScriptButton"] = "Copy Machine PATH Script",
            ["refreshRemoteButton"] = "Refresh Remote Versions",
            ["sessionActivationTitle"] = "Current Session Activation",
            ["sessionActivationDescription"] = "The desktop app writes user-scoped variables for new shells. Use these scripts when you need the current shell session to update immediately.",
            ["copyPowerShellButton"] = "Copy PowerShell Script",
            ["copyCmdButton"] = "Copy cmd Script",
            ["powerShellPreviewLabel"] = "PowerShell preview",
            ["statusTitle"] = "Status",
            ["statusDescription"] = "Latest operation result and current UI state.",
            ["workingTitle"] = "Working",
            ["nonePlaceholder"] = "(none)",
            ["doctorPlaceholder"] = "Run Doctor to display diagnostics.",
            ["shellPreviewEmpty"] = "No activation script is available yet.",
            ["shellPreviewUnavailable"] = "Activation preview unavailable: {0}",
            ["errorTitle"] = "Operation Failed",
            ["warningTitle"] = "Action Required",
            ["readyStatus"] = "Ready.",
            ["languageChangedStatus"] = "Interface language switched to {0}.",
            ["workspaceLoadedStatus"] = "Workspace loaded. {0} JDK(s), {1} Maven installation(s).",
            ["remoteVersionsRefreshedStatus"] = "Remote versions refreshed.",
            ["syncCompletedStatus"] = "Local sync completed. {0} JDK(s), {1} Maven installation(s).",
            ["jdkImportedStatus"] = "Imported JDK: {0}",
            ["mavenImportedStatus"] = "Imported Maven: {0}",
            ["jdkSwitchedStatus"] = "Active JDK switched to {0}.",
            ["mavenSwitchedStatus"] = "Active Maven switched to {0}.",
            ["jdkRemovedStatus"] = "Removed JDK from state: {0}",
            ["mavenRemovedStatus"] = "Removed Maven from state: {0}",
            ["jdkUninstalledStatus"] = "Uninstalled managed JDK: {0}",
            ["mavenUninstalledStatus"] = "Uninstalled managed Maven: {0}",
            ["jdkInstalledStatus"] = "Installed JDK: {0}",
            ["jdkInstalledAndActivatedStatus"] = "Installed and activated JDK: {0}",
            ["mavenInstalledStatus"] = "Installed Maven: {0}",
            ["mavenInstalledAndActivatedStatus"] = "Installed and activated Maven: {0}",
            ["doctorCompletedStatus"] = "Doctor completed. PASS={0}, WARN={1}, FAIL={2}",
            ["powerShellScriptCopiedStatus"] = "PowerShell activation script copied.",
            ["cmdScriptCopiedStatus"] = "cmd activation script copied.",
            ["statusOperationFailed"] = "Operation failed: {0}",
            ["busyLoadingWorkspace"] = "Loading workspace state and remote versions...",
            ["busyRefreshingRemoteVersions"] = "Refreshing remote versions...",
            ["busySyncingLocalInstallations"] = "Syncing local JDK and Maven installations...",
            ["busyImportingJdk"] = "Importing the selected JDK home...",
            ["busyImportingMaven"] = "Importing the selected Maven home...",
            ["busySwitchingJdk"] = "Applying the selected JDK to the user environment...",
            ["busySwitchingMaven"] = "Applying the selected Maven to the user environment...",
            ["busyRemovingJdk"] = "Removing the selected JDK from TaoMaster state...",
            ["busyRemovingMaven"] = "Removing the selected Maven from TaoMaster state...",
            ["busyUninstallingJdk"] = "Uninstalling the selected managed JDK and deleting its files...",
            ["busyUninstallingMaven"] = "Uninstalling the selected managed Maven and deleting its files...",
            ["busyInstallingJdk"] = "Downloading and installing the selected Temurin JDK...",
            ["busyInstallingMaven"] = "Downloading and installing the selected Maven distribution...",
            ["busyRunningDoctor"] = "Running environment and Maven/JDK diagnostics...",
            ["busyRepairingUserPath"] = "Removing conflicting direct JDK and Maven entries from user PATH...",
            ["busyPreparingMachinePathScript"] = "Preparing the administrator PowerShell script for machine PATH repair...",
            ["busyCopyingPowerShellScript"] = "Preparing the PowerShell activation script...",
            ["busyCopyingCmdScript"] = "Preparing the cmd activation script...",
            ["userPathRepairedStatus"] = "User PATH repaired. Removed {0} conflicting entries.",
            ["userPathAlreadyCleanStatus"] = "User PATH already has no conflicting direct JDK or Maven entries.",
            ["machinePathScriptCopiedStatus"] = "Machine PATH repair script copied to the clipboard.",
            ["machinePathAlreadyCleanStatus"] = "Machine PATH already has no conflicting JDK or Maven entries.",
            ["validationSelectJdk"] = "Select a JDK first.",
            ["validationSelectMaven"] = "Select a Maven installation first.",
            ["validationChooseJdkFolder"] = "Choose a valid JDK home directory first.",
            ["validationChooseMavenFolder"] = "Choose a valid Maven home directory first.",
            ["validationSelectRemoteJdkVersion"] = "Select a Temurin JDK feature version first.",
            ["validationSelectRemoteMavenVersion"] = "Select a Maven version first.",
            ["shellScriptUnavailableWarning"] = "No activation script is available for the current selection yet.",
            ["removeConfirmTitle"] = "Confirm Removal",
            ["removeConfirmMessage"] = "Remove `{0}` from TaoMaster state only? Files on disk will be kept.",
            ["uninstallConfirmTitle"] = "Confirm Uninstall",
            ["uninstallConfirmMessage"] = "Uninstall managed `{0}` and delete this directory?{1}{2}",
            ["browseJdkDescription"] = "Select a JDK home directory",
            ["browseMavenDescription"] = "Select a Maven home directory",
            ["doctorHeader"] = "Doctor Report",
            ["doctorSummary"] = "Summary: PASS={0}, WARN={1}, FAIL={2}",
            ["detailSelectionLabel"] = "Selection",
            ["detailIdLabel"] = "ID",
            ["detailExpectedLabel"] = "Expected",
            ["detailActualLabel"] = "Actual",
            ["detailScopeLabel"] = "Scope",
            ["detailPathEntryLabel"] = "PATH entry",
            ["detailRecommendationLabel"] = "Recommendation",
            ["detailOutputLabel"] = "Output",
            ["pathScopeMachine"] = "Machine PATH",
            ["pathScopeUser"] = "User PATH",
            ["doctorNoActionNeeded"] = "No action needed.",
            ["doctorRepairUserPathRecommendation"] = "Run `repair user-path` or use the desktop button to clean conflicting user PATH entries.",
            ["doctorRepairMachinePathRecommendation"] = "A machine PATH entry is winning first. Generate the administrator repair script or remove/reorder it with administrator privileges.",
            ["doctor.selected-jdk.pass"] = "Active JDK selection is valid.",
            ["doctor.selected-jdk.warn"] = "No JDK is selected in state.",
            ["doctor.selected-jdk.fail"] = "The saved JDK selection points to a missing installation.",
            ["doctor.selected-maven.pass"] = "Active Maven selection is valid.",
            ["doctor.selected-maven.warn"] = "No Maven is selected in state.",
            ["doctor.selected-maven.fail"] = "The saved Maven selection points to a missing installation.",
            ["doctor.java-home.pass"] = "User JAVA_HOME matches the selected JDK.",
            ["doctor.java-home.warn"] = "No JDK is selected, so JAVA_HOME is not validated.",
            ["doctor.java-home.fail"] = "User JAVA_HOME does not match the selected JDK.",
            ["doctor.maven-home.pass"] = "User MAVEN_HOME matches the selected Maven.",
            ["doctor.maven-home.warn"] = "No Maven is selected, so MAVEN_HOME is not validated.",
            ["doctor.maven-home.fail"] = "User MAVEN_HOME does not match the selected Maven.",
            ["doctor.m2-home.pass"] = "User M2_HOME matches the selected Maven.",
            ["doctor.m2-home.warn"] = "No Maven is selected, so M2_HOME is not validated.",
            ["doctor.m2-home.fail"] = "User M2_HOME does not match the selected Maven.",
            ["doctor.user-path.pass"] = "User PATH contains the managed entries in the expected order.",
            ["doctor.user-path.warn"] = "User PATH could not be validated.",
            ["doctor.user-path.fail"] = "User PATH is missing managed entries, contains duplicates, or has an unexpected order.",
            ["doctor.java-resolve.pass"] = "java.exe resolves to the selected JDK in a new process.",
            ["doctor.java-resolve.warn"] = "java.exe resolves to a different location in a new process.",
            ["doctor.java-resolve.fail"] = "java.exe cannot be resolved from a new process PATH.",
            ["doctor.maven-resolve.pass"] = "mvn.cmd resolves to the selected Maven in a new process.",
            ["doctor.maven-resolve.warn"] = "mvn.cmd resolves to a different location in a new process.",
            ["doctor.maven-resolve.fail"] = "mvn.cmd cannot be resolved from a new process PATH.",
            ["doctor.maven-probe.pass"] = "The selected Maven starts with the selected JDK.",
            ["doctor.maven-probe.warn"] = "The Maven probe returned warnings.",
            ["doctor.maven-probe.fail"] = "The selected Maven could not start with the selected JDK."
        };

    private static readonly IReadOnlyDictionary<string, string> SimplifiedChineseStrings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["windowTitle"] = ProductInfo.ChineseName,
            ["headerSubtitle"] = "面向 JDK 与 Maven 的 Windows 工具链管理器",
            ["headerDescription"] = "把本机发现、受控安装、全局切换以及 Maven/JDK 一致性检查集中到同一个桌面入口。",
            ["workspaceRootLabel"] = "工作区根目录",
            ["stateFileLabel"] = "状态文件",
            ["languageLabel"] = "语言",
            ["activeJdkTitle"] = "当前 JDK",
            ["activeMavenTitle"] = "当前 Maven",
            ["javaHomeLabel"] = "JAVA_HOME",
            ["mavenHomeLabel"] = "MAVEN_HOME / M2_HOME",
            ["installedJdksTitle"] = "已安装 JDK",
            ["installedJdksDescription"] = "同步本机安装、导入已有目录，切换到选中的 JDK，或移除/卸载受管项。",
            ["syncLocalButton"] = "同步本机",
            ["useSelectedJdkButton"] = "使用选中 JDK",
            ["removeSelectedButton"] = "移除选中项",
            ["uninstallSelectedButton"] = "卸载选中项",
            ["browseButton"] = "浏览",
            ["importJdkButton"] = "导入 JDK",
            ["installedMavensTitle"] = "已安装 Maven",
            ["installedMavensDescription"] = "导入已有 Maven 根目录，切换到选中的 Maven，或移除/卸载受管项。",
            ["useSelectedMavenButton"] = "使用选中 Maven",
            ["importMavenButton"] = "导入 Maven",
            ["remoteInstallTitle"] = "远程安装",
            ["remoteInstallDescription"] = "从官方源下载 Temurin JDK 与 Apache Maven ZIP 包，校验摘要、解压到受管工作区，并可在安装后立即切换。",
            ["temurinJdkLabel"] = "Temurin JDK",
            ["apacheMavenLabel"] = "Apache Maven",
            ["installButton"] = "安装",
            ["installUseButton"] = "安装并切换",
            ["doctorTitle"] = "Doctor",
            ["doctorDescription"] = "校验当前选择、用户级环境变量、PATH 入口，以及 Maven/JDK 的运行时一致性。",
            ["runDoctorButton"] = "运行 Doctor",
            ["repairUserPathButton"] = "清理用户 PATH",
            ["copyMachinePathScriptButton"] = "复制机器 PATH 修复脚本",
            ["refreshRemoteButton"] = "刷新远程版本",
            ["sessionActivationTitle"] = "当前会话激活",
            ["sessionActivationDescription"] = "桌面端会为新开的终端写入用户级环境变量。如需让当前终端立即生效，可直接使用下面的脚本。",
            ["copyPowerShellButton"] = "复制 PowerShell 脚本",
            ["copyCmdButton"] = "复制 cmd 脚本",
            ["powerShellPreviewLabel"] = "PowerShell 预览",
            ["statusTitle"] = "状态",
            ["statusDescription"] = "显示最近一次操作结果和当前界面状态。",
            ["workingTitle"] = "处理中",
            ["nonePlaceholder"] = "（未选择）",
            ["doctorPlaceholder"] = "运行 Doctor 后会在这里显示诊断结果。",
            ["shellPreviewEmpty"] = "当前还没有可用的激活脚本。",
            ["shellPreviewUnavailable"] = "激活脚本预览不可用：{0}",
            ["errorTitle"] = "操作失败",
            ["warningTitle"] = "需要处理",
            ["readyStatus"] = "就绪。",
            ["languageChangedStatus"] = "界面语言已切换为 {0}。",
            ["workspaceLoadedStatus"] = "工作区已加载，共 {0} 个 JDK、{1} 个 Maven。",
            ["remoteVersionsRefreshedStatus"] = "远程版本列表已刷新。",
            ["syncCompletedStatus"] = "本机同步完成，共 {0} 个 JDK、{1} 个 Maven。",
            ["jdkImportedStatus"] = "已导入 JDK：{0}",
            ["mavenImportedStatus"] = "已导入 Maven：{0}",
            ["jdkSwitchedStatus"] = "当前 JDK 已切换到：{0}",
            ["mavenSwitchedStatus"] = "当前 Maven 已切换到：{0}",
            ["jdkRemovedStatus"] = "已从状态中移除 JDK：{0}",
            ["mavenRemovedStatus"] = "已从状态中移除 Maven：{0}",
            ["jdkUninstalledStatus"] = "已卸载受管 JDK：{0}",
            ["mavenUninstalledStatus"] = "已卸载受管 Maven：{0}",
            ["jdkInstalledStatus"] = "已安装 JDK：{0}",
            ["jdkInstalledAndActivatedStatus"] = "已安装并切换到 JDK：{0}",
            ["mavenInstalledStatus"] = "已安装 Maven：{0}",
            ["mavenInstalledAndActivatedStatus"] = "已安装并切换到 Maven：{0}",
            ["doctorCompletedStatus"] = "Doctor 完成。PASS={0}，WARN={1}，FAIL={2}",
            ["powerShellScriptCopiedStatus"] = "PowerShell 激活脚本已复制。",
            ["cmdScriptCopiedStatus"] = "cmd 激活脚本已复制。",
            ["statusOperationFailed"] = "操作失败：{0}",
            ["busyLoadingWorkspace"] = "正在加载工作区状态和远程版本...",
            ["busyRefreshingRemoteVersions"] = "正在刷新远程版本列表...",
            ["busySyncingLocalInstallations"] = "正在同步本机 JDK 和 Maven 安装...",
            ["busyImportingJdk"] = "正在导入所选 JDK 根目录...",
            ["busyImportingMaven"] = "正在导入所选 Maven 根目录...",
            ["busySwitchingJdk"] = "正在把所选 JDK 应用到用户环境变量...",
            ["busySwitchingMaven"] = "正在把所选 Maven 应用到用户环境变量...",
            ["busyRemovingJdk"] = "正在从道驭状态中移除所选 JDK...",
            ["busyRemovingMaven"] = "正在从道驭状态中移除所选 Maven...",
            ["busyUninstallingJdk"] = "正在卸载所选受管 JDK 并删除目录...",
            ["busyUninstallingMaven"] = "正在卸载所选受管 Maven 并删除目录...",
            ["busyInstallingJdk"] = "正在下载并安装所选 Temurin JDK...",
            ["busyInstallingMaven"] = "正在下载并安装所选 Maven 发行版...",
            ["busyRunningDoctor"] = "正在执行环境变量和 Maven/JDK 诊断...",
            ["busyRepairingUserPath"] = "正在清理用户 PATH 中直接写死的 JDK 和 Maven 入口...",
            ["busyPreparingMachinePathScript"] = "正在生成机器级 PATH 管理员 PowerShell 修复脚本...",
            ["busyCopyingPowerShellScript"] = "正在准备 PowerShell 激活脚本...",
            ["busyCopyingCmdScript"] = "正在准备 cmd 激活脚本...",
            ["userPathRepairedStatus"] = "用户 PATH 已清理，移除了 {0} 个冲突入口。",
            ["userPathAlreadyCleanStatus"] = "用户 PATH 中没有需要清理的直接 JDK 或 Maven 入口。",
            ["machinePathScriptCopiedStatus"] = "机器级 PATH 修复脚本已复制到剪贴板。",
            ["machinePathAlreadyCleanStatus"] = "机器级 PATH 中没有检测到需要处理的 JDK 或 Maven 冲突入口。",
            ["validationSelectJdk"] = "请先选择一个 JDK。",
            ["validationSelectMaven"] = "请先选择一个 Maven 安装。",
            ["validationChooseJdkFolder"] = "请先选择有效的 JDK 根目录。",
            ["validationChooseMavenFolder"] = "请先选择有效的 Maven 根目录。",
            ["validationSelectRemoteJdkVersion"] = "请先选择一个 Temurin JDK 特性版本。",
            ["validationSelectRemoteMavenVersion"] = "请先选择一个 Maven 版本。",
            ["shellScriptUnavailableWarning"] = "当前选择还没有可用的激活脚本。",
            ["removeConfirmTitle"] = "确认移除",
            ["removeConfirmMessage"] = "只从道驭状态中移除 `{0}` 吗？磁盘上的文件会保留。",
            ["uninstallConfirmTitle"] = "确认卸载",
            ["uninstallConfirmMessage"] = "要卸载受管 `{0}` 并删除下面的目录吗？{1}{2}",
            ["browseJdkDescription"] = "选择一个 JDK 根目录",
            ["browseMavenDescription"] = "选择一个 Maven 根目录",
            ["doctorHeader"] = "Doctor 结果",
            ["doctorSummary"] = "汇总：PASS={0}，WARN={1}，FAIL={2}",
            ["detailSelectionLabel"] = "当前选择",
            ["detailIdLabel"] = "ID",
            ["detailExpectedLabel"] = "期望",
            ["detailActualLabel"] = "实际",
            ["detailScopeLabel"] = "作用域",
            ["detailPathEntryLabel"] = "PATH 项",
            ["detailRecommendationLabel"] = "建议",
            ["detailOutputLabel"] = "输出",
            ["pathScopeMachine"] = "机器 PATH",
            ["pathScopeUser"] = "用户 PATH",
            ["doctorNoActionNeeded"] = "无需处理。",
            ["doctorRepairUserPathRecommendation"] = "可运行 `repair user-path` 或使用桌面按钮清理用户 PATH 冲突入口。",
            ["doctorRepairMachinePathRecommendation"] = "当前是机器 PATH 入口抢先命中，可先生成并以管理员身份运行修复脚本，或手动调整系统环境变量顺序。",
            ["doctor.selected-jdk.pass"] = "当前 JDK 选择有效。",
            ["doctor.selected-jdk.warn"] = "状态中尚未选择 JDK。",
            ["doctor.selected-jdk.fail"] = "保存的 JDK 选择指向了不存在的安装。",
            ["doctor.selected-maven.pass"] = "当前 Maven 选择有效。",
            ["doctor.selected-maven.warn"] = "状态中尚未选择 Maven。",
            ["doctor.selected-maven.fail"] = "保存的 Maven 选择指向了不存在的安装。",
            ["doctor.java-home.pass"] = "用户 JAVA_HOME 与当前选中的 JDK 一致。",
            ["doctor.java-home.warn"] = "当前未选择 JDK，因此未校验 JAVA_HOME。",
            ["doctor.java-home.fail"] = "用户 JAVA_HOME 与当前选中的 JDK 不一致。",
            ["doctor.maven-home.pass"] = "用户 MAVEN_HOME 与当前选中的 Maven 一致。",
            ["doctor.maven-home.warn"] = "当前未选择 Maven，因此未校验 MAVEN_HOME。",
            ["doctor.maven-home.fail"] = "用户 MAVEN_HOME 与当前选中的 Maven 不一致。",
            ["doctor.m2-home.pass"] = "用户 M2_HOME 与当前选中的 Maven 一致。",
            ["doctor.m2-home.warn"] = "当前未选择 Maven，因此未校验 M2_HOME。",
            ["doctor.m2-home.fail"] = "用户 M2_HOME 与当前选中的 Maven 不一致。",
            ["doctor.user-path.pass"] = "用户 PATH 中的受控入口顺序正确。",
            ["doctor.user-path.warn"] = "无法完整校验用户 PATH。",
            ["doctor.user-path.fail"] = "用户 PATH 缺少受控入口、存在重复项，或顺序不正确。",
            ["doctor.java-resolve.pass"] = "新进程里的 java.exe 解析到了当前选中的 JDK。",
            ["doctor.java-resolve.warn"] = "新进程里的 java.exe 解析到了其他位置。",
            ["doctor.java-resolve.fail"] = "无法在新进程 PATH 中解析到 java.exe。",
            ["doctor.maven-resolve.pass"] = "新进程里的 mvn.cmd 解析到了当前选中的 Maven。",
            ["doctor.maven-resolve.warn"] = "新进程里的 mvn.cmd 解析到了其他位置。",
            ["doctor.maven-resolve.fail"] = "无法在新进程 PATH 中解析到 mvn.cmd。",
            ["doctor.maven-probe.pass"] = "所选 Maven 可以使用当前选中的 JDK 正常启动。",
            ["doctor.maven-probe.warn"] = "Maven 探测返回了警告。",
            ["doctor.maven-probe.fail"] = "所选 Maven 无法使用当前选中的 JDK 正常启动。"
        };

    private readonly IReadOnlyDictionary<string, string> _strings;

    public AppLocalizer(AppLanguage language)
    {
        Language = language;
        _strings = language == AppLanguage.SimplifiedChinese
            ? SimplifiedChineseStrings
            : EnglishStrings;
    }

    public AppLanguage Language { get; }

    public static IReadOnlyList<LanguageOption> SupportedLanguages => SupportedLanguageList;

    public string this[string key] =>
        _strings.TryGetValue(key, out var value)
            ? value
            : key;

    public string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, this[key], args);

    public string GetDoctorMessage(string code, DoctorCheckStatus status)
    {
        var key = $"doctor.{code}.{status.ToString().ToLowerInvariant()}";
        return this[key];
    }

    public string GetProductDisplayName() =>
        Language == AppLanguage.SimplifiedChinese
            ? ProductInfo.ChineseName
            : ProductInfo.EnglishName;

    public static bool TryParseLanguage(string? value, out AppLanguage language)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            language = default;
            return false;
        }

        return Enum.TryParse(value, ignoreCase: true, out language);
    }

    public static AppLanguage DetectDefaultLanguage(CultureInfo culture) =>
        culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.SimplifiedChinese
            : AppLanguage.English;
}
