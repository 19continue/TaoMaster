# 道驭（TaoMaster）

`道驭（TaoMaster）` 是一个面向 Windows 10 及以上系统的轻量级 JDK / Maven 联动管理工具，当前版本为 `1.0.0`。

它把这些能力收敛到同一套工具里：

- 本机 JDK / Maven 发现
- 已有安装目录导入
- Temurin JDK / Apache Maven 远程下载与安装
- 用户级环境变量切换
- Maven 对 JDK 切换的联动感知
- `doctor` 一致性诊断
- 用户级与机器级 `PATH` 冲突修复辅助
- WPF 桌面端 + CLI 双入口
- 桌面端支持 `English` 和 `简体中文`

## 当前能力

- 发现本机已安装的 JDK 和 Maven
- 导入已有的 JDK / Maven 根目录并纳入统一状态管理
- 下载并安装新的 Temurin JDK 与 Apache Maven 版本
- 切换用户级 `JAVA_HOME`、`MAVEN_HOME`、`M2_HOME`
- 维护受控 `PATH` 入口，让 Maven 跟随当前 JDK 切换
- 运行 `doctor` 诊断状态、环境变量、PATH 解析结果与 Maven/JDK 联动
- 清理用户级 `PATH` 中残留的直接 JDK / Maven 入口
- 生成机器级 `PATH` 冲突修复脚本，供管理员 PowerShell 执行
- 生成当前终端即时生效的 PowerShell / cmd 激活脚本
- 记住桌面端上次选择的界面语言

## 关键设计

版本切换不依赖把真实安装目录反复硬编码写进 `PATH`，而是统一维护受控入口：

- `JAVA_HOME` 始终指向当前选中的 JDK 根目录
- `MAVEN_HOME` 与 `M2_HOME` 始终指向当前选中的 Maven 根目录
- 用户级 `PATH` 只保留 `%JAVA_HOME%\bin` 和 `%MAVEN_HOME%\bin` 两个受控入口

这样新开的 `cmd`、PowerShell、IDE 和 `mvn` 都会读取同一套环境变量，Maven 也会自然感知 JDK 的切换结果。

## 项目结构

- `src/TaoMaster.Core`：核心模型、工作区、发现、安装、切换、诊断逻辑
- `src/TaoMaster.Cli`：命令行入口
- `src/TaoMaster.App`：WPF 桌面端
- `docs/architecture.md`：架构设计说明
- `docs/mvp-roadmap.md`：MVP 路线
- `docs/state.example.json`：状态文件示例

## 常用命令

```powershell
dotnet run --project .\src\TaoMaster.Cli -- init
dotnet run --project .\src\TaoMaster.Cli -- state
dotnet run --project .\src\TaoMaster.Cli -- discover
dotnet run --project .\src\TaoMaster.Cli -- sync
dotnet run --project .\src\TaoMaster.Cli -- list jdks
dotnet run --project .\src\TaoMaster.Cli -- list mavens
dotnet run --project .\src\TaoMaster.Cli -- import jdk "C:\Program Files\Java\jdk-17"
dotnet run --project .\src\TaoMaster.Cli -- import maven "C:\tools\apache-maven-3.9.14"
dotnet run --project .\src\TaoMaster.Cli -- use jdk temurin-17.0.18-x64
dotnet run --project .\src\TaoMaster.Cli -- use maven apache-maven-3.9.14
dotnet run --project .\src\TaoMaster.Cli -- remote jdks
dotnet run --project .\src\TaoMaster.Cli -- remote mavens
dotnet run --project .\src\TaoMaster.Cli -- install jdk --version 17
dotnet run --project .\src\TaoMaster.Cli -- install jdk --version 17 --switch
dotnet run --project .\src\TaoMaster.Cli -- install maven --version 3.9.14
dotnet run --project .\src\TaoMaster.Cli -- install maven --version 3.9.14 --switch
dotnet run --project .\src\TaoMaster.Cli -- doctor
dotnet run --project .\src\TaoMaster.Cli -- repair user-path
dotnet run --project .\src\TaoMaster.Cli -- repair machine-path-script
dotnet run --project .\src\TaoMaster.Cli -- env powershell
dotnet run --project .\src\TaoMaster.Cli -- env cmd
dotnet build .\TaoMaster.sln -m:1
dotnet run --project .\src\TaoMaster.App
```

## 桌面端说明

桌面端当前已经接入以下流程：

- 工作区状态加载
- 已安装 JDK / Maven 列表展示
- 本机同步
- 目录导入
- 远程版本刷新
- 下载并安装
- 一键切换
- Doctor 诊断展示
- 用户级 `PATH` 冲突清理
- 机器级 `PATH` 修复脚本复制
- PowerShell / cmd 激活脚本复制
- 英文 / 简体中文界面切换

## 工作区与迁移

- 默认工作区目录：`%LOCALAPPDATA%\TaoMaster`
- 状态文件路径：`%LOCALAPPDATA%\TaoMaster\state.json`
- 如果机器上存在旧的 `%LOCALAPPDATA%\JdkManager` 工作区，且新的 `TaoMaster` 工作区尚未建立，程序会尝试自动迁移旧目录

## 机器级 PATH 说明

如果机器级 `PATH` 里存在更靠前的 `java.exe` 或 `mvn.cmd`，新开的终端直接执行 `java` / `mvn` 时，仍可能优先命中系统安装。

当前策略是：

- 用户级环境变量始终由道驭受控
- `doctor` 会标出命中的机器级冲突入口
- `repair machine-path-script` 会生成管理员 PowerShell 修复脚本
- 桌面端也可以一键复制该脚本到剪贴板

脚本不会自动提权执行。它的作用是让管理员明确看到将被移除的机器级 `PATH` 段，并在确认后手动运行。

## 已验证

- `dotnet build TaoMaster.sln -m:1` 可以通过
- CLI 的发现、导入、切换、远程安装、`doctor`、`repair user-path` 已完成联调
- 机器级 `PATH` 修复脚本已经能生成
- WPF 桌面端可以正常启动

## 下一步

- 为机器级 `PATH` 修复提供更完整的回滚与日志
- 增加受管 JDK / Maven 删除能力
- 补充下载进度、失败日志与桌面端结构化诊断展示
- 继续把桌面端逻辑向 MVVM 拆分
- 增加 Core / CLI 自动化测试
