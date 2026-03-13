# TaoMaster Architecture

## 1. Product scope

Target platform: Windows 10 and above

Core capabilities:

- discover locally installed JDKs and Maven versions
- import an existing JDK or Maven home into the manager
- download new JDK and Maven versions into a managed directory
- switch the active JDK globally for the current user
- switch the active Maven version globally for the current user
- ensure Maven follows the selected JDK
- expose the same operations in GUI and CLI

Non-goals for MVP:

- project-level `.sdkmanrc` or `.java-version` support
- automatic IDE plugin integration
- background watchers or services
- full vendor coverage on day one

## 2. User experience model

The tool is split into two entry points:

- `TaoMaster.App`: desktop UI for normal management
- `TaoMaster.Cli`: automation, shell activation, and scriptable workflows

Primary screens:

- Dashboard: current JDK, current Maven, environment health
- JDK Catalog: installed versions, import, download, remove, set active
- Maven Catalog: installed versions, import, download, remove, set active
- Downloads: active tasks, cached packages, checksum results
- Settings: install root, provider preferences, proxy, PATH repair

CLI commands planned for MVP:

```text
jdkm list jdks
jdkm list mavens
jdkm use jdk <id>
jdkm use maven <id>
jdkm import jdk <path>
jdkm import maven <path>
jdkm install jdk --vendor temurin --version 17
jdkm install maven --version 3.9.11
jdkm doctor
jdkm env powershell
jdkm env cmd
```

## 3. Lightweight architecture

The application should stay light and native:

- implementation language: C# / .NET 8
- UI: WPF
- no Electron, no embedded Chromium, no local service
- persistence: one JSON state file plus logs and download cache
- downloads: plain `HttpClient` with resumable strategy added later if needed

Project boundaries:

- `TaoMaster.Core`
  - models
  - state persistence contract
  - environment switching contract
  - detection and validation rules
  - provider abstractions
- `TaoMaster.Cli`
  - command parsing
  - shell activation output
  - non-interactive operations
- `TaoMaster.App`
  - WPF views
  - view models
  - desktop interaction flows

## 4. Runtime layout

Managed files live under `%LOCALAPPDATA%\TaoMaster`.

Recommended structure:

```text
%LOCALAPPDATA%\TaoMaster\
  state.json
  logs\
  cache\
  temp\
  scripts\
  jdks\
    temurin-17.0.12+7\
    temurin-21.0.6+7\
  mavens\
    apache-maven-3.9.11\
```

Reasons:

- no admin is required for normal operations
- managed assets stay isolated from user-installed external tools
- uninstall/cleanup is straightforward

## 5. Switch contract

This is the central design choice.

### 5.1 Environment variables

At user scope, the tool maintains:

- `JAVA_HOME=<selected JDK home>`
- `MAVEN_HOME=<selected Maven home>`
- `M2_HOME=<selected Maven home>`
- `JDKMANAGER_JAVA_ID=<selected JDK id>`
- `JDKMANAGER_MAVEN_ID=<selected Maven id>`

### 5.2 PATH strategy

`PATH` is not rewritten to physical installation folders on every switch.

Instead, the tool ensures the managed region contains only:

- `%JAVA_HOME%\bin`
- `%MAVEN_HOME%\bin`

The manager owns two contiguous managed entries near the front of the user `PATH`, for example:

```text
%JAVA_HOME%\bin;%MAVEN_HOME%\bin;...existing path...
```

The switch engine removes stale duplicates of these two entries, then reinserts them in the preferred order.

Benefits:

- switching JDK means updating `JAVA_HOME`, not rewriting every absolute path
- Maven automatically uses the current JDK because `mvn.cmd` reads Java through `JAVA_HOME`
- the PATH stays stable and deduplicated

### 5.3 Process refresh behavior

After a global switch:

- write the environment variables at user scope
- ensure the managed PATH entries exist in the correct order
- broadcast `WM_SETTINGCHANGE` for `Environment`

Effect:

- newly opened terminals inherit the new values
- newly launched IDEs and build tools see the new values
- already opened terminals do not magically change their current process environment

### 5.4 Current-session support

Because existing shells keep their own environment snapshot, the CLI also exposes session activation:

- `jdkm env powershell`
- `jdkm env cmd`

Typical usage:

```powershell
jdkm env powershell | Invoke-Expression
```

This gives immediate effect in the current shell without requiring a restart.

## 6. Why Maven follows the JDK switch

The manager does not try to patch Maven internals.

It relies on the standard Java contract Maven already follows:

- `mvn.cmd` resolves Java from `JAVA_HOME` when present
- when `JAVA_HOME` changes, new Maven processes use the new JDK
- keeping `%JAVA_HOME%\bin` on `PATH` also makes `java`, `javac`, and related tools consistent with the active selection

This is the simplest reliable model on Windows.

## 7. Discovery and import

### 7.1 JDK discovery

Discovery sources:

- current `JAVA_HOME`
- user and machine `PATH`
- common installation directories
- Windows registry keys used by common Java installers

Candidate directories are validated by:

- `<home>\bin\java.exe`
- `<home>\release`

Metadata extracted:

- version
- vendor
- architecture
- managed vs external

### 7.2 Maven discovery

Discovery sources:

- current `MAVEN_HOME` and `M2_HOME`
- common install directories
- imported managed directories

Candidate directories are validated by:

- `<home>\bin\mvn.cmd`
- expected Maven library layout

Metadata extracted:

- version
- distribution name
- managed vs external

### 7.3 Import model

Import does not move files by default.

The tool supports:

- `Register existing path`: keep the external folder in catalog
- `Copy into managed store`: duplicate into `%LOCALAPPDATA%\TaoMaster\jdks` or `mavens`

This prevents forced migration while still allowing a fully managed mode.

## 8. Download and install design

### 8.1 Provider abstraction

Create provider interfaces:

- `IJdkPackageSource`
- `IMavenPackageSource`

Responsibilities:

- list available versions
- resolve download URL for a version and architecture
- provide checksum metadata when available

Initial providers:

- JDK: Temurin/Adoptium
- Maven: Apache Maven official distribution

### 8.2 Installation flow

1. Resolve package metadata from the provider.
2. Download archive into `cache\`.
3. Verify checksum if metadata exists.
4. Extract into `temp\`.
5. Validate the extracted home.
6. Move to managed install root with a normalized id.
7. Register into `state.json`.
8. Optionally set as active.

### 8.3 Archive handling

Support first:

- JDK ZIP archives
- Maven ZIP archives

Avoid MSI-based installation in MVP to keep the workflow user-scoped and deterministic.

## 9. State model

One JSON file is enough for MVP:

- file: `%LOCALAPPDATA%\TaoMaster\state.json`

Contains:

- known JDK installations
- known Maven installations
- active selection ids
- install root and settings
- last verification result

See `docs/state.example.json`.

## 10. Remove and cleanup rules

Removing an installation from the catalog has two modes:

- `Unregister`: remove from state only
- `Delete managed files`: only allowed for managed installations

Safety rules:

- never delete an external imported directory automatically
- prevent deleting the currently active version without replacement

## 11. Error handling and diagnostics

The `doctor` command and dashboard diagnostics should report:

- missing `JAVA_HOME`
- missing `MAVEN_HOME` or broken Maven home
- managed PATH entries missing or duplicated
- active installation id missing on disk
- `java -version` and `mvn -v` mismatch against selected state

Useful repair actions:

- rebuild managed PATH entries
- reapply current selection
- reopen shell guidance

## 12. Security and robustness

Basic rules:

- verify archive checksum when the provider exposes one
- treat downloaded archives as untrusted until validation completes
- normalize install ids and sanitize destination names
- never execute binaries from an extracted package before validation

## 13. MVP implementation order

1. Core models and workspace layout
2. JSON state persistence
3. Local discovery and import
4. Environment switching service
5. CLI MVP
6. Download providers
7. WPF bindings and interaction
8. Diagnostics and repair commands

## 14. Future extensions

- project-local overrides
- multiple JDK vendors
- mirror and proxy configuration
- portable zip export/import
- scheduled provider metadata refresh
