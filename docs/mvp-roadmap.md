# MVP Roadmap

## Phase 1: Core foundation

- create `WorkspaceLayout`
- define managed installation models
- persist and load `state.json`
- define install id normalization rules

Exit criteria:

- app can boot and show empty state
- CLI can print layout and current persisted selection

## Phase 2: Discovery and import

- scan local JDK homes
- scan local Maven homes
- validate imported directories
- register external installations in state

Exit criteria:

- CLI can list discovered/imported JDK and Maven entries
- invalid homes are rejected with clear messages

## Phase 3: Switching engine

- write user environment variables
- maintain the manager-owned PATH entries
- broadcast environment change
- implement `doctor`

Exit criteria:

- switching JDK changes `java -version` in a newly opened shell
- switching Maven changes `mvn -v` in a newly opened shell
- `mvn -v` reports the same Java home/version as the selected JDK

## Phase 4: Download and install

- add Temurin provider
- add Apache Maven provider
- download ZIP packages
- verify checksums
- extract and register installations

Exit criteria:

- user can install one new JDK and one new Maven version from the app
- installed versions appear in the catalog and can be activated

## Phase 5: Desktop UX

- bind WPF dashboard to state
- implement list/detail actions
- add progress UI for downloads
- add repair actions for common environment issues

Exit criteria:

- user can complete the full flow in GUI without touching the terminal

## Phase 6: Packaging

- publish single-file desktop build
- publish CLI binary
- define upgrade path for `state.json`

Exit criteria:

- local install package can be copied to another Windows 10+ machine
- first-run experience can initialize the workspace cleanly
