using System.Text;
using TaoMaster.Core;
using TaoMaster.Core.Discovery;
using TaoMaster.Core.Models;
using TaoMaster.Core.RemoteSources;
using TaoMaster.Core.Services;
using TaoMaster.Core.State;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("错误: 当前版本仅支持 Windows 环境。");
    return 1;
}

using var httpClient = new HttpClient();

var layout = WorkspaceLayout.CreateDefault();
var workspaceInitializer = new WorkspaceInitializer();
var stateStore = new ManagerStateStore(workspaceInitializer);
var inspector = new InstallationInspector();
var discoveryService = new LocalInstallationDiscoveryService(inspector);
var catalogService = new InstallationCatalogService(inspector);
var selectionResolver = new ToolchainSelectionResolver();
var environmentService = new WindowsUserEnvironmentService();
var activationService = new SelectionActivationService(selectionResolver, environmentService);
var shellIntegrationService = new WindowsShellIntegrationService(activationService);
var doctorService = new DoctorService(selectionResolver, environmentService);
var temurinSource = new TemurinJdkPackageSource(httpClient);
var mavenSource = new ApacheMavenPackageSource(httpClient);
var downloadService = new PackageDownloadService(httpClient);
var checksumService = new ChecksumService();
var zipExtractionService = new ZipExtractionService();
var packageInstallationService = new PackageInstallationService(downloadService, checksumService, zipExtractionService, inspector);

workspaceInitializer.EnsureCreated(layout);

try
{
    if (args.Length == 0)
    {
        PrintHelp(layout);
        return 0;
    }

    var command = args[0].ToLowerInvariant();

    switch (command)
    {
        case "layout":
            PrintLayout(layout);
            break;
        case "plan":
            PrintPlan();
            break;
        case "init":
            RunInit();
            break;
        case "state":
            PrintState(stateStore.EnsureInitialized(layout), layout);
            break;
        case "discover":
            PrintDiscoverySnapshot(discoveryService.Discover(layout));
            break;
        case "sync":
            RunSync();
            break;
        case "list":
            RunList(args);
            break;
        case "import":
            RunImport(args);
            break;
        case "use":
            RunUse(args);
            break;
        case "remove":
            RunRemove(args, deleteFiles: false);
            break;
        case "uninstall":
            RunRemove(args, deleteFiles: true);
            break;
        case "doctor":
            RunDoctor();
            break;
        case "repair":
            RunRepair(args);
            break;
        case "env":
            RunEnv(args);
            break;
        case "remote":
            await RunRemoteAsync(args);
            break;
        case "install":
            await RunInstallAsync(args);
            break;
        default:
            PrintHelp(layout);
            break;
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误: {ex.Message}");
    return 1;
}

void RunInit()
{
    var initializedState = stateStore.EnsureInitialized(layout);
    Console.WriteLine($"状态文件已初始化: {layout.StateFile}");
    Console.WriteLine($"JDK 记录数  : {initializedState.Jdks.Count}");
    Console.WriteLine($"Maven 记录数: {initializedState.Mavens.Count}");
}

void RunSync()
{
    var state = stateStore.EnsureInitialized(layout);
    var snapshot = discoveryService.Discover(layout);
    var updatedState = catalogService.MergeDiscovered(state, snapshot);

    stateStore.Save(layout, updatedState);

    Console.WriteLine("已同步本机工具链到状态文件。");
    Console.WriteLine($"JDK   : {updatedState.Jdks.Count}");
    Console.WriteLine($"Maven : {updatedState.Mavens.Count}");
    Console.WriteLine($"状态文件: {layout.StateFile}");
}

void RunList(string[] cliArgs)
{
    if (cliArgs.Length < 2)
    {
        throw new ArgumentException("请指定要列出的类型：`list jdks` 或 `list mavens`。");
    }

    var state = stateStore.EnsureInitialized(layout);
    var kind = cliArgs[1].ToLowerInvariant();

    switch (kind)
    {
        case "jdks":
            PrintInstallations("JDK", state.Jdks);
            break;
        case "mavens":
            PrintInstallations("Maven", state.Mavens);
            break;
        default:
            throw new ArgumentException("仅支持 `list jdks` 或 `list mavens`。");
    }
}

void RunImport(string[] cliArgs)
{
    if (cliArgs.Length < 3)
    {
        throw new ArgumentException("用法：`import jdk <path>` 或 `import maven <path>`。");
    }

    var kind = ParseToolchainKind(cliArgs[1]);
    var targetPath = string.Join(" ", cliArgs.Skip(2));
    var state = stateStore.EnsureInitialized(layout);
    var (updatedState, installation) = catalogService.ImportInstallation(state, kind, targetPath, layout);

    stateStore.Save(layout, updatedState);

    Console.WriteLine("导入成功。");
    Console.WriteLine($"类型 : {installation.Kind}");
    Console.WriteLine($"名称 : {installation.DisplayName}");
    Console.WriteLine($"版本 : {installation.Version}");
    Console.WriteLine($"目录 : {installation.HomeDirectory}");
    Console.WriteLine($"ID   : {installation.Id}");
}

void RunUse(string[] cliArgs)
{
    if (cliArgs.Length < 3)
    {
        throw new ArgumentException("用法：`use jdk <id>` 或 `use maven <id>`。");
    }

    var kind = ParseToolchainKind(cliArgs[1]);
    var id = string.Join(" ", cliArgs.Skip(2)).Trim();
    var state = stateStore.EnsureInitialized(layout);
    var installation = selectionResolver.GetRequiredSelection(state, kind, id);

    var updatedState = kind switch
    {
        ToolchainKind.Jdk => state with
        {
            ActiveSelection = state.ActiveSelection with { JdkId = installation.Id }
        },
        ToolchainKind.Maven => state with
        {
            ActiveSelection = state.ActiveSelection with { MavenId = installation.Id }
        },
        _ => state
    };

    var activationResult = activationService.Apply(updatedState);
    var shellIntegrationStatus = shellIntegrationService.EnsureEnabled(layout, updatedState);
    stateStore.Save(layout, updatedState);

    Console.WriteLine("切换成功。");
    Console.WriteLine($"类型 : {installation.Kind}");
    Console.WriteLine($"名称 : {installation.DisplayName}");
    Console.WriteLine($"ID   : {installation.Id}");

    if (activationResult.Selection.Jdk is not null)
    {
        Console.WriteLine($"JAVA_HOME  : {activationResult.UserJavaHome}");
    }

    if (activationResult.Selection.Maven is not null)
    {
        Console.WriteLine($"MAVEN_HOME : {activationResult.UserMavenHome}");
    }

    Console.WriteLine($"Shell sync : cmd={(shellIntegrationStatus.CmdAutoRunEnabled ? "on" : "off")}, powershell={shellIntegrationStatus.PowerShellEnabledProfileCount}/{shellIntegrationStatus.PowerShellProfileCount}");
}

void RunRemove(string[] cliArgs, bool deleteFiles)
{
    if (cliArgs.Length < 3)
    {
        throw new ArgumentException(deleteFiles
            ? "用法：`uninstall jdk <id>` 或 `uninstall maven <id>`。"
            : "用法：`remove jdk <id>` 或 `remove maven <id>`。");
    }

    var kind = ParseToolchainKind(cliArgs[1]);
    var id = string.Join(" ", cliArgs.Skip(2)).Trim();
    var state = stateStore.EnsureInitialized(layout);
    var result = catalogService.RemoveInstallation(state, kind, id, layout, deleteFiles);

    stateStore.Save(layout, result.State);
    activationService.Apply(result.State);
    shellIntegrationService.EnsureEnabled(layout, result.State);

    Console.WriteLine(deleteFiles ? "Uninstall completed." : "Removal completed.");
    Console.WriteLine($"Kind     : {result.Installation.Kind}");
    Console.WriteLine($"Name     : {result.Installation.DisplayName}");
    Console.WriteLine($"ID       : {result.Installation.Id}");
    Console.WriteLine($"Managed  : {result.Installation.IsManaged}");
    Console.WriteLine($"Location : {result.Installation.HomeDirectory}");
    Console.WriteLine($"Files deleted : {result.DeletedFiles}");
}

void RunDoctor()
{
    var state = stateStore.EnsureInitialized(layout);
    var report = doctorService.Run(state);

    Console.WriteLine("Doctor 结果");
    foreach (var check in report.Checks)
    {
        var prefix = check.Status switch
        {
            DoctorCheckStatus.Pass => "[PASS]",
            DoctorCheckStatus.Warn => "[WARN]",
            DoctorCheckStatus.Fail => "[FAIL]",
            _ => "[INFO]"
        };

        Console.WriteLine($"{prefix} {check.Code} - {check.Message}");
        if (!string.IsNullOrWhiteSpace(check.Detail))
        {
            Console.WriteLine($"       {check.Detail}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"汇总: PASS={report.Checks.Count(x => x.Status == DoctorCheckStatus.Pass)}, WARN={report.Checks.Count(x => x.Status == DoctorCheckStatus.Warn)}, FAIL={report.Checks.Count(x => x.Status == DoctorCheckStatus.Fail)}");
}

void RunRepair(string[] cliArgs)
{
    if (cliArgs.Length < 2)
    {
        throw new ArgumentException("用法：`repair user-path` 或 `repair machine-path-script`。");
    }

    var state = stateStore.EnsureInitialized(layout);
    var selection = selectionResolver.Resolve(state);

    if (cliArgs[1].Equals("user-path", StringComparison.OrdinalIgnoreCase))
    {
        var result = environmentService.RepairUserPathForManagedToolchains(
            environmentService.GetUserVariable(EnvironmentVariableNames.Path),
            includeJavaEntry: selection.Jdk is not null,
            includeMavenEntry: selection.Maven is not null);

        environmentService.SetUserVariable(EnvironmentVariableNames.Path, result.UpdatedPath);
        activationService.Apply(state);
        shellIntegrationService.EnsureEnabled(layout, state);

        Console.WriteLine("User PATH repair completed.");
        Console.WriteLine($"Removed segments: {result.RemovedSegments.Count}");

        foreach (var removedSegment in result.RemovedSegments)
        {
            Console.WriteLine($"- {removedSegment}");
        }

        if (result.RemovedSegments.Count == 0)
        {
            Console.WriteLine("No conflicting direct JDK or Maven entries were found in user PATH.");
        }

        Console.WriteLine($"Updated PATH: {result.UpdatedPath}");
        return;
    }

    if (cliArgs[1].Equals("machine-path-script", StringComparison.OrdinalIgnoreCase)
        || cliArgs[1].Equals("machine-path", StringComparison.OrdinalIgnoreCase))
    {
        var plan = environmentService.BuildMachinePathRepairPlan(
            selection.Jdk?.HomeDirectory,
            selection.Maven?.HomeDirectory);

        Console.WriteLine("Machine PATH repair analysis completed.");
        Console.WriteLine($"Detected conflicting machine PATH segments: {plan.RemovedSegments.Count}");

        foreach (var removedSegment in plan.RemovedSegments)
        {
            Console.WriteLine($"- {removedSegment}");
        }

        if (!plan.Changed)
        {
            Console.WriteLine("No conflicting machine PATH entries were detected.");
            return;
        }

        Directory.CreateDirectory(layout.ScriptRoot);

        var scriptPath = Path.Combine(layout.ScriptRoot, "repair-machine-path.ps1");
        File.WriteAllText(scriptPath, plan.PowerShellScript);

        Console.WriteLine($"Script saved: {scriptPath}");
        Console.WriteLine("Run the script from an elevated PowerShell session to update the machine PATH.");
        return;
    }

    throw new ArgumentException("用法：`repair user-path` 或 `repair machine-path-script`。");
}

void RunEnv(string[] cliArgs)
{
    if (cliArgs.Length < 2)
    {
        throw new ArgumentException("用法：`env powershell` 或 `env cmd`。");
    }

    var state = stateStore.EnsureInitialized(layout);
    Console.WriteLine(activationService.BuildShellScript(state, cliArgs[1]));
}

async Task RunRemoteAsync(string[] cliArgs)
{
    if (cliArgs.Length < 2)
    {
        throw new ArgumentException("用法：`remote jdks` 或 `remote mavens`。");
    }

    switch (cliArgs[1].ToLowerInvariant())
    {
        case "jdks":
            var releases = await temurinSource.GetAvailableFeatureReleasesAsync(CancellationToken.None);
            Console.WriteLine("Temurin 可安装 JDK 特性版本");
            foreach (var release in releases)
            {
                Console.WriteLine($"- {release}");
            }
            break;
        case "mavens":
            var versions = await mavenSource.GetCurrentVersionsAsync(CancellationToken.None);
            Console.WriteLine("Apache Maven 当前官方下载版本");
            foreach (var version in versions)
            {
                Console.WriteLine($"- {version}");
            }
            break;
        default:
            throw new ArgumentException("仅支持 `remote jdks` 或 `remote mavens`。");
    }
}

async Task RunInstallAsync(string[] cliArgs)
{
    if (cliArgs.Length < 2)
    {
        throw new ArgumentException("用法：`install jdk ...` 或 `install maven ...`。");
    }

    var kind = ParseToolchainKind(cliArgs[1]);
    var switchAfterInstall = HasFlag(cliArgs, "--switch");
    var state = stateStore.EnsureInitialized(layout);

    var package = kind switch
    {
        ToolchainKind.Jdk => await ResolveJdkPackageAsync(cliArgs),
        ToolchainKind.Maven => await ResolveMavenPackageAsync(cliArgs),
        _ => throw new ArgumentOutOfRangeException()
    };

    Console.WriteLine($"开始下载并安装: {package.DisplayName}");
    var installation = await packageInstallationService.InstallAsync(package, layout, CancellationToken.None);
    var updatedState = catalogService.RegisterInstallation(state, installation);

    if (switchAfterInstall)
    {
        updatedState = kind switch
        {
            ToolchainKind.Jdk => updatedState with
            {
                ActiveSelection = updatedState.ActiveSelection with { JdkId = installation.Id }
            },
            ToolchainKind.Maven => updatedState with
            {
                ActiveSelection = updatedState.ActiveSelection with { MavenId = installation.Id }
            },
            _ => updatedState
        };
    }

    stateStore.Save(layout, updatedState);

    if (switchAfterInstall)
    {
        activationService.Apply(updatedState);
        shellIntegrationService.EnsureEnabled(layout, updatedState);
    }

    Console.WriteLine("安装完成。");
    Console.WriteLine($"类型 : {installation.Kind}");
    Console.WriteLine($"名称 : {installation.DisplayName}");
    Console.WriteLine($"版本 : {installation.Version}");
    Console.WriteLine($"目录 : {installation.HomeDirectory}");
    Console.WriteLine($"ID   : {installation.Id}");
    Console.WriteLine($"已切换: {(switchAfterInstall ? "是" : "否")}");
}

async Task<RemotePackageDescriptor> ResolveJdkPackageAsync(string[] cliArgs)
{
    var versionText = GetOptionValue(cliArgs, "--version");
    var featureVersion = string.IsNullOrWhiteSpace(versionText)
        ? (await temurinSource.GetAvailableFeatureReleasesAsync(CancellationToken.None)).First()
        : int.Parse(versionText);
    var architecture = GetOptionValue(cliArgs, "--arch") ?? "x64";

    return await temurinSource.ResolveLatestAsync(featureVersion, architecture, CancellationToken.None);
}

async Task<RemotePackageDescriptor> ResolveMavenPackageAsync(string[] cliArgs)
{
    var version = GetOptionValue(cliArgs, "--version");
    return await mavenSource.ResolveAsync(version, CancellationToken.None);
}

static ToolchainKind ParseToolchainKind(string value) =>
    value.ToLowerInvariant() switch
    {
        "jdk" => ToolchainKind.Jdk,
        "maven" => ToolchainKind.Maven,
        _ => throw new ArgumentException("仅支持 `jdk` 或 `maven`。")
    };

static string? GetOptionValue(IReadOnlyList<string> args, string optionName)
{
    for (var index = 0; index < args.Count - 1; index++)
    {
        if (args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static bool HasFlag(IEnumerable<string> args, string flagName) =>
    args.Any(arg => arg.Equals(flagName, StringComparison.OrdinalIgnoreCase));

static void PrintHelp(WorkspaceLayout layout)
{
    Console.WriteLine("TaoMaster CLI");
    Console.WriteLine();
    Console.WriteLine("管理根目录:");
    Console.WriteLine($"  {layout.RootDirectory}");
    Console.WriteLine();
    Console.WriteLine("当前可用命令:");
    Console.WriteLine("  init");
    Console.WriteLine("  state");
    Console.WriteLine("  layout");
    Console.WriteLine("  plan");
    Console.WriteLine("  discover");
    Console.WriteLine("  sync");
    Console.WriteLine("  list jdks");
    Console.WriteLine("  list mavens");
    Console.WriteLine("  remote jdks");
    Console.WriteLine("  remote mavens");
    Console.WriteLine("  import jdk <path>");
    Console.WriteLine("  import maven <path>");
    Console.WriteLine("  use jdk <id>");
    Console.WriteLine("  use maven <id>");
    Console.WriteLine("  remove jdk <id>");
    Console.WriteLine("  remove maven <id>");
    Console.WriteLine("  uninstall jdk <id>");
    Console.WriteLine("  uninstall maven <id>");
    Console.WriteLine("  install jdk [--version 17] [--arch x64] [--switch]");
    Console.WriteLine("  install maven [--version 3.9.14] [--switch]");
    Console.WriteLine("  doctor");
    Console.WriteLine("  repair user-path");
    Console.WriteLine("  repair machine-path-script");
    Console.WriteLine("  env powershell");
    Console.WriteLine("  env cmd");
    Console.WriteLine();
    Console.WriteLine("全局切换契约:");
    Console.WriteLine("  JAVA_HOME  -> 当前选中的 JDK 根目录");
    Console.WriteLine("  MAVEN_HOME -> 当前选中的 Maven 根目录");
    Console.WriteLine("  M2_HOME    -> 当前选中的 Maven 根目录");
    Console.WriteLine("  PATH 保留 %JAVA_HOME%\\bin 和 %MAVEN_HOME%\\bin 两个受控入口");
}

static void PrintLayout(WorkspaceLayout layout)
{
    Console.WriteLine("TaoMaster 运行时目录");
    Console.WriteLine($"Root     : {layout.RootDirectory}");
    Console.WriteLine($"JDKs     : {layout.JdkRoot}");
    Console.WriteLine($"Mavens   : {layout.MavenRoot}");
    Console.WriteLine($"Cache    : {layout.CacheRoot}");
    Console.WriteLine($"Temp     : {layout.TempRoot}");
    Console.WriteLine($"Logs     : {layout.LogRoot}");
    Console.WriteLine($"Scripts  : {layout.ScriptRoot}");
    Console.WriteLine($"State    : {layout.StateFile}");
}

static void PrintPlan()
{
    Console.WriteLine("TaoMaster MVP 实施阶段");
    Console.WriteLine("1. 状态持久化与工作目录初始化");
    Console.WriteLine("2. JDK / Maven 发现与导入");
    Console.WriteLine("3. 环境变量切换与 doctor");
    Console.WriteLine("4. Temurin / Apache Maven 下载");
    Console.WriteLine("5. WPF 桌面端绑定与完整流程");
}

static void PrintState(ManagerState state, WorkspaceLayout layout)
{
    Console.WriteLine("当前状态");
    Console.WriteLine($"状态文件      : {layout.StateFile}");
    Console.WriteLine($"安装根目录    : {state.Settings.InstallRoot}");
    Console.WriteLine($"JDK 数量      : {state.Jdks.Count}");
    Console.WriteLine($"Maven 数量    : {state.Mavens.Count}");
    Console.WriteLine($"当前 JDK ID   : {state.ActiveSelection.JdkId ?? "(未选择)"}");
    Console.WriteLine($"当前 Maven ID : {state.ActiveSelection.MavenId ?? "(未选择)"}");
}

static void PrintDiscoverySnapshot(DiscoverySnapshot snapshot)
{
    Console.WriteLine("发现结果");
    Console.WriteLine();
    PrintInstallations("JDK", snapshot.Jdks);
    Console.WriteLine();
    PrintInstallations("Maven", snapshot.Mavens);
}

static void PrintInstallations(string title, IReadOnlyList<ManagedInstallation> installations)
{
    Console.WriteLine($"{title} 列表");

    if (installations.Count == 0)
    {
        Console.WriteLine("  (空)");
        return;
    }

    foreach (var installation in installations)
    {
        Console.WriteLine($"- {installation.DisplayName}");
        Console.WriteLine($"  ID      : {installation.Id}");
        Console.WriteLine($"  版本    : {installation.Version}");
        Console.WriteLine($"  厂商    : {installation.Vendor ?? "(未知)"}");
        Console.WriteLine($"  架构    : {installation.Architecture ?? "(未知)"}");
        Console.WriteLine($"  来源    : {installation.Source}");
        Console.WriteLine($"  受控    : {(installation.IsManaged ? "是" : "否")}");
        Console.WriteLine($"  目录    : {installation.HomeDirectory}");
    }
}
