# TwinCAT Assistant 0.2.0 Beta 12

## Changes since Beta 11

- Beta 12 includes four additional read-only Debug tools through Engineering
  MCP `0.5.13`. Wait up to
  120 seconds for a condition on approved symbols, including an optional
  `stableForMs` interval. The broker allows 150 seconds for its child exchange
  and 155 seconds for MCP execution over HTTP to cover the wait, session
  checks, ADS connection and teardown. Register, list or stop bounded symbol
  watches within the same Debug session. The published catalogs contain 9 Build,
  16 Editor and 15 Debug tools.
- Editor and Debug register distinct, temporary MCP servers,
  `engineering_editor` and `engineering_debug`. Analysis continues to use
  the static read-only `engineering` server. Managed Beta 6–11 configurations
  upgrade in place, preserving operator settings and history.
- Source edit and rollback approvals show a color-highlighted side-by-side
  diff in the native WPF dialog.
- Switching profiles keeps the selected chat and its queued follow-ups.
  The native owner revokes the previous grant and removes its MCP registration
  before enabling the next profile.
- Selective build preflight can read the dirty state of TwinCAT solution and
  project entries whose running document data does not expose
  `IVsPersistDocData`. If preflight reports `unsaved_project_changes`, save
  pending changes in TwinCAT XAE before requesting another build. Each build
  still requires separate native approval. The x86 host also preserves the
  full installed `EnvDTE80` platform interface for provider reflection.
  Engineering 0.5.13 accepts the standard zero-import PLC project and verifies
  dependency ownership by full project path when COM `Project.UniqueName`
  returns `E_FAIL` (`0x80004005`).

## Installation and verification

Application version `0.2.0-beta.12` uses native version `0.2.0.18`. The
supported target is TwinCAT 3.1.4024.78 with TcXaeShell 15 x86 on Windows x64.
See the [installation guide](../INSTALLATION.md) and
[profile guide](../EDITOR_PROFILE.md).

The release pins `engineering-v0.5.13` at
`aee9b6d5c39dc7b03b87725e8fec61199c29f689` with native source tree SHA-256
`520b1f5f8ff0d89c928132536e4e7f8cca8f6840c4f14ed201823004751e96cc`.
The [Engineering lock](../../upstream/engineering-lock.json) records the
receipt, archive, runtime and catalog hashes.

Both Release host builds embed the same generated adapter, SHA-256
`f9b70e296952b5156c4793bf204f16cf085a3e822000d031d40a77b1cbdbf19b`.
Verification passed 230 Node tests, 1,343 native regressions, 122 WPF/WebView2
startup checks and three backend startup checks. A separate pinned Core
integration test verified that switching Editor → Debug → Editor leaves only
the selected server registered. The published runtime passed catalog and
bound-operation checks for all three Engineering profiles.

### Isolated TwinCAT build verification

On 2026-11-27 the probe activated a private TcXaeShell 15 x86 process on
TwinCAT 3.1.4024.78 and created a synthetic project from Beckhoff's standard
PLC template. The `.plcproj` had zero `<Import>` elements before the edit and
after Save All. The probe called the rebuilt x86 host's
`EngineeringDirtyState.GetDirty` with running-document ownership checks and
dispatched requests to the provider from the hash-verified Engineering 0.5.13
x86 runtime.

| Case | Live result |
| --- | --- |
| A. Saved stock PLC template | Dirty state was `false` for `.sln`, `.tsproj` and `.plcproj`. `session.describe` reported both `build` and `plcBuild` as `supported`, with configuration `Release` and platform `TwinCAT RT (x64)`, matching COM. |
| B. Unsaved function block creation | Dirty state was `true` for `.plcproj` and the saved bytes were unchanged. `session.describe` returned `buildUnavailableReason: "unsaved_project_changes"`. |
| C. After Save All | Dirty state was `false` for `.plcproj`. `build.start` accepted the selective build and `operation.get` returned `succeeded` for operation `8ba4500f-b17f-4dfe-9e59-e6a1f663227a`. TwinCAT reported `vsBuildStateDone`, `LastBuildInfo = 0`, and compiler output with 0 errors and 0 warnings. |

The probe called `dte.Quit()` on the private process and verified that it
exited. The pre-existing TcXaeShell and WindowsHost retained their PIDs and
start times, and the temporary directory was removed. The build ran offline
against the synthetic project. Installed-host workflow acceptance remains
open.
