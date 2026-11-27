# TwinCAT Assistant 0.2.0 Beta 12

## Changes since Beta 11

- Engineering MCP `0.5.12` adds four read-only Debug tools. Wait up to
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

## Installation and verification

Application version `0.2.0-beta.12` uses native version `0.2.0.18`. The
supported target is TwinCAT 3.1.4024.78 with TcXaeShell 15 x86 on Windows x64.
See the [installation guide](../INSTALLATION.md) and
[profile guide](../EDITOR_PROFILE.md).

The release pins `engineering-v0.5.12` at
`419816d8335ca857926751dd189ec8b48ba8e7a1` with native source tree SHA-256
`6db399e31e7feebdbd3132711a2cd0b3d8264eb3a69df1857c5c822314c986d8`.
The [Engineering lock](../../upstream/engineering-lock.json) records the
receipt, archive, runtime and catalog hashes.

Both Release host builds embed the same generated adapter, SHA-256
`f9b70e296952b5156c4793bf204f16cf085a3e822000d031d40a77b1cbdbf19b`.
Verification passed 230 Node tests, 1,334 native regressions, 122 WPF/WebView2
startup checks and three backend startup checks. A separate pinned Core
integration test verified that switching Editor → Debug → Editor leaves only
the selected server registered. The published runtime passed catalog and
bound-operation checks for all three Engineering profiles.

### Isolated TwinCAT build verification

The probe activated a private TcXaeShell 15 x86 process on TwinCAT
3.1.4024.78 and generated a synthetic PLC project. It called the rebuilt
`EngineeringDirtyState.GetDirty` and the published Engineering 0.5.12
provider. The private process exited and its temporary project was removed.

| Case | Live result |
| --- | --- |
| Saved stock PLC template | Dirty state was `false` for `.sln`, `.tsproj` and `.plcproj`. The template has no build import, so the provider reported `plcBuild: "unsupported"` with `unsafe_build_project`. |
| Unsaved function block creation | The `.plcproj` was dirty and its saved bytes were unchanged. The provider returned `unsaved_project_changes`. |
| After Save All, with the allowed vendor import added to the temporary fixture | The PLC project was clean and COM returned `Release\|TwinCAT RT (x64)`. `session.describe` reported `build_dependency_evidence_unavailable`, and `build.start` returned `unsupported` with the same reason. TwinCAT remained at `vsBuildStateNotStarted`. |

The exact PLC entry in `BuildDependencies` existed, but its
`Project.UniqueName` getter returned COM `E_FAIL` (`0x80004005`). The pinned
provider therefore could not verify dependency ownership. A zero-error
selective build through the provider remains unverified.
