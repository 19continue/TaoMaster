# 道驭 TaoMaster

`道驭（TaoMaster）` 是一个面向 Windows 10 及以上系统的轻量级 JDK / Maven 联动管理工具，当前版本为 `1.0.0`。

它把这些能力收敛到同一套工具里：

- 本机 JDK / Maven 发现
- 已有安装目录导入
- Temurin JDK / Apache Maven 远程下载安装
- 用户级环境变量切换
- Maven 对 JDK 切换的联动感知
- `doctor` 一致性诊断
- WPF 桌面端 + CLI 双入口

## 当前能力

- 发现本机已有 JDK 与 Maven 安装
- 导入已有的 JDK / Maven 目录到统一状态文件
- 下载并安装新的 Temurin JDK 与 Apache Maven 版本
- 在用户级环境变量上切换 `JAVA_HOME`、`MAVEN_HOME`、`M2_HOME`
- 维护受控 `PATH` 入口，让 Maven 能跟随当前 JDK 切换
- 运行 `doctor` 诊断，检查状态选择、环境变量、PATH 解析与 Maven/JDK 联动
- 清理用户级 PATH 中残留的直接 JDK / Maven 入口，并重新应用当前选中的环境
- 生成当前终端可立即生效的 PowerShell / cmd 激活脚本
- 提供 WPF 桌面端界面
- 桌面端当前支持 `English` 与 `简体中文` 两种界面语言
- 桌面端会记住上次选择的界面语言

## 关键设计

版本切换不依赖反复把真实安装目录直接写死到 `PATH`，而是使用受控环境变量：

- `JAVA_HOME` 始终指向当前选中的 JDK 根目录
- `MAVEN_HOME` 与 `M2_HOME` 始终指向当前选中的 Maven 根目录
- `PATH` 只保留 `%JAVA_HOME%\\bin` 与 `%MAVEN_HOME%\\bin` 两个受控入口

这样新开的 `cmd`、PowerShell、IDE 与 `mvn` 会读取同一套用户级配置，Maven 也能自然感知 JDK 的切换结果。

## 项目结构

- `src/TaoMaster.Core`：核心模型、工作区目录、发现、安装、切换、诊断逻辑
- `src/TaoMaster.Cli`：命令行入口
- `src/TaoMaster.App`：WPF 桌面端
- `docs/architecture.md`：架构设计说明
- `docs/mvp-roadmap.md`：MVP 路线图
- `docs/state.example.json`：状态文件示例

## 当前命令

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
dotnet run --project .\src\TaoMaster.Cli -- env powershell
dotnet run --project .\src\TaoMaster.Cli -- env cmd
dotnet build .\TaoMaster.sln -m:1
dotnet run --project .\src\TaoMaster.App
```

## 桌面端说明

桌面端已经接入以下流程：

- 工作区状态加载
- 已安装 JDK / Maven 列表展示
- 本机同步
- 目录导入
- 远程版本刷新
- 下载并安装
- 一键切换
- Doctor 结果展示
- 用户级 PATH 冲突清理
- PowerShell / cmd 激活脚本复制
- 英文 / 简体中文界面切换
- 上次语言选择持久化

## 工作区与迁移

- 默认工作区目录已经从 `%LOCALAPPDATA%\\JdkManager` 调整为 `%LOCALAPPDATA%\\TaoMaster`
- 如果机器上已存在旧的 `%LOCALAPPDATA%\\JdkManager` 工作区，而新的 `TaoMaster` 工作区还不存在，程序会尝试自动迁移旧目录

## 当前约束

- 如果机器级 `PATH` 里存在更靠前的 `java.exe` 或 `mvn.cmd`，新开的终端直接执行 `java` / `mvn` 时，仍可能优先命中系统安装路径
- 即使存在上述情况，只要 `JAVA_HOME`、`MAVEN_HOME` 与 `M2_HOME` 正确，`doctor` 里的 Maven 探测仍能验证 Maven 是否按当前选中的 JDK 启动
- 当前远程安装源固定为 Temurin JDK 与 Apache Maven 官方分发源

## 已验证

- `dotnet build TaoMaster.sln -m:1` 可以通过
- CLI 的发现、导入、切换、远程安装与 `doctor` 已完成联调
- WPF 桌面端可以正常启动

## 下一步

- 增加 PATH 冲突修复建议或自动修复动作
- 补充更细粒度的安装进度与失败日志展示
- 为状态文件增加迁移、导出与备份能力
- 继续把桌面端逻辑往 MVVM 方向拆分
