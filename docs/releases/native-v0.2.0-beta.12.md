# TwinCAT Assistant 0.2.0 Beta 12

Published on **2026-11-27** as [TwinCAT Assistant 0.2.0 Beta 12](https://github.com/Auda29/twincat-assistant/releases/tag/native-v0.2.0-beta.12).
Application `0.2.0-beta.12` uses native version `0.2.0.18`.

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

The supported target is TwinCAT 3.1.4024.78 with TcXaeShell 15 x86 on Windows x64.
.NET 8 runtime, .NET Framework, WebView2 and access to a supported model provider
are required. TwinCAT 4026 / Shell64 is not an enabled installer target.

1. Download the installer and `SHA256.txt` linked below. Compare the setup hash
   with `Get-FileHash -Algorithm SHA256 <path-to-installer>` before running it.
2. Close TcXaeShell and run the setup EXE. Confirm UAC with the Windows account
   that uses the Assistant. Update a managed installation in its existing
   directory; no uninstall, Git checkout, npm or local compilation is required.
   Old unpackaged prototypes have separate
   [migration instructions](../PROTOTYPE_PROFILE_MIGRATION_20261109.md).
3. Reopen TwinCAT, open the Assistant window, connect your provider and select a
   model. For documentation search, prepare your local InfoSys help through
   [Knowledge setup](../KNOWLEDGE_SETUP.md).
4. Use Analysis for read-only questions. Select Editor or Debug in a bound
   project chat as described in the [profile guide](../EDITOR_PROFILE.md).
   Each edit, rollback, creation and build requires separate native approval.

See the [development installation guide](../INSTALLATION.md) for source builds.

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

## Published build and downloads

The release tag and installer source are
`201a9f0ccbbbc099ef5edc6a3675168c2f70536b`. The complete official
`scripts/Build-Release.ps1` pipeline exited successfully after all **23 stages**,
including packaged startup with both MCPs, restart/state preservation,
installer recovery fixtures and extraction verification of **21,623 files**.
This was a local Windows source build, not a hosted installer build.

- [Windows x64 installer](https://github.com/Auda29/twincat-assistant/releases/download/native-v0.2.0-beta.12/TwinCAT-Assistant-0.2.0-beta.12-x64-setup.exe)
- [SHA-256 checksums](https://github.com/Auda29/twincat-assistant/releases/download/native-v0.2.0-beta.12/SHA256.txt)
- [Build provenance](https://github.com/Auda29/twincat-assistant/releases/download/native-v0.2.0-beta.12/BUILD_PROVENANCE.json)

All three published downloads matched the local release files by size and
SHA-256. The installer is 217,273,856 bytes with SHA-256
`7e41cb09d4ea18a195be51b5a6a92414f8a716fc2c460cb7c3196980a95439ec`.
Provenance records the verified Engineering 0.5.13 and Knowledge 0.1.2 pins,
`sourceClean: true`, `realHostInstallation: false` and `realProviderLogin: false`.
The release process did not install the application or perform a real provider
login. Full installed-host workflows, real update/rollback with retained login,
and real-corpus extraction quality/performance remain open.
