# Assistant host targets, compatibility matrix and host acceptance

Recorded: 2026-11-05. Roadmap item 1 ("Kompatibilität eingrenzen und Zielhosts
festlegen"). This document defines the first installable alpha's host target,
the concrete compatibility matrix and the acceptance procedure per host. It
builds on the [installation variants](HOST_COMPATIBILITY.md) research.

**Nothing here is a support claim.** A host that matches the alpha target is
eligible for installation; its open acceptance gates stay open until they are
observed in that host. TcXaeShell64, TwinCAT 4026 and full Visual Studio are
not released by this work.

Machine-readable sources:

- [`native/host-targets.json`](../native/host-targets.json): targets, match
  criteria, payload/installation details, verified environment and per-gate
  acceptance state. This is the target description for the installer.
- [`scripts/Get-AssistantHostInventory.ps1`](../scripts/Get-AssistantHostInventory.ps1):
  read-only host inventory plus classification against that file.
- `native/Shared/HostIdentity.cs`: the same classification in process; shown in
  the Assistant's Diagnose panel as row **IDE-Host**.

## Current machine (read-only inventory, 2026-11-05)

Collected with `Get-AssistantHostInventory.ps1` under Windows PowerShell 5.1
and PowerShell 7.6, non-elevated. No IDE was started, attached (COM/ROT) or
stopped; the running operator Shell was only listed by PID and start time.
`tcpkg` was not found and nothing was executed. The raw record stays in ignored
local data; the values below are sanitized.

| Item | Observed value |
| --- | --- |
| Windows | Windows 11 Enterprise 25H2, build 26200.9711, amd64 client. The registry `ProductName` still reads "Windows 10 Enterprise"; the build number decides. |
| TwinCAT XAE | **3.1.4024.78**: `TcVersion` registry value, setup entry "Beckhoff TwinCAT 3.1 (Build 4024)" (installed 2026-08-14) and `TwinCAT XAE Base.dll` 3.1.4024.78 agree |
| Installation mechanism | Classic setup; no Package Manager/`tcpkg` |
| Related components | Remote Manager Components (Build 4024) 3.1.4024.11; x64 driver package 3.1.4024.76; `TCatSysManagerLib.dll` 3.4.1.14. TwinCAT 2.11 x64 Engineering is also installed and is not an Assistant host |
| Host A: TcXaeShell | Beckhoff TwinCAT XAE Shell **1.22.0.0**; `TcXaeShell.exe` 15.0.0.0, **x86**; isolated shell base `msenv.dll` 15.0.35826.140 (D15.9); registry root `Software\Beckhoff\TcXaeShell\15.0`; `TcXaeShell.DTE.15.0` registered to this executable; TwinCAT XAE Base/Plc integration present (manifest targets VS 15.0) |
| Assistant in host A | One payload `TwinCatAssistant.Native.X86` 0.1.9 (x86, IntegratedShell `[15.0,17.0)`), a local test build; no duplicate payload discoverable |
| Host B: Visual Studio Community 2019 | 16.11.62 (`devenv.exe` 16.11.37827.8, x86); `VisualStudio.DTE.16.0` registered to it; TwinCAT XAE Base/Plc/Safety integration folders present (manifest targets VS 16.0, same date as the 4024.78 setup). No Assistant payload. Not launched: whether TwinCAT loads there is unobserved |
| Other Visual Studio | Build Tools 2022 17.14.44 (no `devenv.exe`, not a host) |
| Not present | TcXaeShell64, Visual Studio 2022/2026 IDE, TwinCAT 4026 |
| Prerequisites | .NET Framework release 533509 (4.8.1 or later); WebView2 runtime 156.0.4314.58 (machine-wide) |

The documented baseline from 2026-10-09 (TcXaeShell 15.0, x86, XAE 3.1.4024.78)
is confirmed. The Shell is now identified exactly: XAE Shell 1.22.0.0 on the
VS 15.9 isolated shell. The second engineering host (TwinCAT in VS2019
Community) was not recorded before. It is classified `unaccepted`.

## First installable alpha: target host

| Field | Value |
| --- | --- |
| Target id | `tcxaeshell-x86-tc4024` |
| Host | TwinCAT XAE Shell (`TcXaeShell.exe` 15.0.0.0, VS 15 isolated shell), 32-bit process |
| TwinCAT | 3.1 Build 4024, classic setup; **verified build 3.1.4024.78 only** |
| Windows | Verified on Windows 11 Enterprise 25H2 (26200), amd64. Other Windows versions have no record |
| Payload | `TwinCatAssistant.Native.X86`, VSIX target `Microsoft.VisualStudio.IntegratedShell [15.0,17.0)`, x86 |
| Installation | Unpacked into `<ShellRoot>\Common7\IDE\Extensions\TwinCatAssistant.Native.X86`, then update the timestamp of the existing empty `extensions.configurationchanged` marker. Requires elevation and a closed Shell. `/UpdateConfiguration` is not supported by this Shell. VSIXInstaller is untested |
| Prerequisites | .NET Framework 4.7.2 or later; an installed WebView2 runtime (156.0.4314.58 verified; the embedded SDK is 1.0.4258.31) |

Another 4024 sub-build in the same Shell is classified
`alpha-target-unverified-build` and refused by default. It becomes a verified
build only by repeating the acceptance below on that build and adding it to
`twinCat.verifiedBuilds`.

## Compatibility matrix

Classification values: `alpha-target` (installer-eligible), `alpha-target-unverified-build`,
`unaccepted`, `blocked`, `out-of-scope`, `not-usable` (no TwinCAT integration),
`prerequisite-missing`, `unknown`. Only `alpha-target` is installer-eligible.

| TwinCAT | Host / base | Arch | Windows | Payload | Classification | Evidence | Needed before release |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 3.1.4024.78 classic | TcXaeShell 1.22.0.0 / VS 15.9 isolated shell | x86 | Win 11 Ent 25H2 26200 | X86 | **alpha-target** | Partial live acceptance (gates below) | Close open gates; installer install/update/rollback |
| 3.1.4024.x, other | TcXaeShell / VS 15 | x86 | none | X86 | alpha-target-unverified-build | none | Own acceptance record per build |
| 3.1.4024.78 classic | Visual Studio Community 2019 16.11.62 | x86 | present here, not launched | none | unaccepted | Integration files only | VSIX target for VS Community/Professional/Enterprise `[16.0,17.0)`, per-user vs. all-users route, full acceptance |
| 3.1.4024 | Visual Studio 2017 | x86 | none | none | unaccepted | none | Same as VS2019 with `[15.0,16.0)` |
| 3.1.4026.x Package Manager | TcXaeShell (VS 15) | x86 | none | X86 | unaccepted | none | Separate machine/VM (4026 over 4024 is a migration); full acceptance |
| 3.1.4026.x Package Manager | TcXaeShell64 (VS 2022 based) | amd64 | none | Amd64 (placeholder target) | blocked | No host available | Real Shell64 installation-target id/version and extension directory; x64 WebView2 loader; full acceptance |
| 3.1.4026.x | Visual Studio 2022 | amd64 | none | Amd64 | unaccepted | none | Edition-specific targets, full acceptance |
| 3.1.4026.x (build open) | Visual Studio 2026 | amd64 | none | none | out-of-scope | none | Payload for version 18 |

### Technical differences to examine per variant

- **TcXaeShell64:** a 64-bit process. `AssistantPackage` rejects the x86 payload
  there (`IntPtr.Size`), so only the Amd64 payload can load. It is compiled
  against VS SDK 17 and needs the x64 WebView2 loader and a 4026 interop
  assembly. Its manifest's `Microsoft.VisualStudio.Community [17.0,18.0)` is a
  build placeholder. The Shell64 installation-target identity, extension
  directory, registry root, cache refresh behavior and `TcXaeShell.DTE.17.0`
  registration must be read from a real installation.
- **TwinCAT 4026:** installed and integrated through Package Manager. A
  configured integration is not proof of an installed integration package. The
  Automation Interface/interop version and PLC editor behavior (document
  monikers, RDT entries, member editors) may differ and must be observed.
  Installing 4026 on the existing 4024 machine is a migration and is not
  permitted for testing.
- **Full Visual Studio:** the x86 payload's `IntegratedShell` target does not
  match Community/Professional/Enterprise. Version 16 is inside the version
  range but not the product id. Per-user extensions live under
  `%LOCALAPPDATA%\Microsoft\VisualStudio\<major>.0_<instance>\Extensions`
  (scanned by the inventory). The ROT probe already accepts
  `VisualStudio.DTE.*` monikers. Binding with non-TwinCAT projects in the same
  IDE is unobserved.
- **Unsaved editor content** is a per-host gate. The current evidence (DTE
  `Saved`, RDT `IsDocDataDirty`, Automation Interface snapshot) was collected
  only in TcXaeShell x86 / 4024.78 and only for one unsaved comment.
- **Editor profile / inline completion (roadmap items 5, 7):** both depend on
  the same editor/RDT access. The host target that gets an editor profile must
  have closed `unsaved-content` and `member-context` gates. Neither is closed
  on any host yet.

## Acceptance state of the alpha target

Source of truth: `targets[0].acceptance` in `native/host-targets.json`.

| Gate | Status | Open part |
| --- | --- | --- |
| install | partial | Only coordinated manual copies 0.1.1–0.1.9; no installer install/update/rollback |
| package-load | passed-user-reported | — |
| docking | passed-user-reported | Observed without a loaded solution |
| focus-resize | open | Keyboard focus, resize and theme switch not recorded |
| webview-chat | passed-user-reported | — |
| project-binding | passed-user-reported | — |
| active-pou | passed-user-reported | — |
| member-context | partial | No recorded live member-editor acceptance |
| unsaved-content | partial | Exact buffer-text semantics unverified |
| attachments | passed-user-reported | — |
| engineering-mcp | synthetic-only | Installed project acceptance |
| knowledge-mcp | synthetic-only | Operator corpus in the installed host |
| multi-ide | partial | In-process UI/chat isolation of two IDEs |
| cold-start-restored-editors | open | [Restored-editor hang](SOLUTION_STARTUP_INVESTIGATION.md) observed without the Assistant loaded; cause unproven |
| shutdown | partial | Installed IDE close with a restored pane |
| update-rollback | open | Depends on the installer (roadmap item 3) |

## Host acceptance procedure

Run on the target host with a synthetic project (for example `ContextChecks`
with PLC_A/PLC_B and same-name FBs, methods, actions and a property). No
customer project and no PLC action are involved. Do not build, activate,
download, log in or save anything except the synthetic test edits. The
operator performs the UI steps; this work does not perform them.

1. **Record the host.** Run the inventory with the IDE closed and attach the
   JSON (ignored `local-data/`). The host must be classified `alpha-target`.
   Copy the Assistant's Diagnose report and check its **IDE-Host** row.
2. **Install/update** with the installer (item 3). Verify payload hashes, a
   single discoverable payload, and the preserved previous payload outside the
   extension search path. Operator data in `%LOCALAPPDATA%\TwinCatAssistant`
   must be unchanged.
3. **Start and window:** menu, single-click opening (measure the duration),
   docking, floating, resize, keyboard focus into/out of the WebView, theme switch.
4. **Chat:** sign-in, provider consent, model selection and one real reply;
   close/reopen the pane and the IDE; the selected session and project survive.
5. **Binding and editor:** PLC_A/PLC_B following, method/action/property
   editors, non-ST and no editor, solution close/reopen, same-path reopen and
   two IDE instances.
6. **Unsaved content:** make an unsaved synthetic change, attach it and record
   whether the chat sees the saved, snapshot or editor-buffer text. Record the
   observed labels exactly.
7. **Attachments and MCPs:** explicit POU attachment; an engineering MCP call
   and a Knowledge answer with sources using the operator corpus.
8. **Cold start with restored editors.** Close the IDE normally with two PLC
   editors and the docked Assistant pane open. Start the IDE cold (first start
   after logon) and open the solution. Record the time until "Preparing
   Solution" ends, whether the toolbox initializes, and whether the pane binds
   to the restored editor. Then repeat with the pane closed. For an
   Assistant-disabled comparison, the operator moves the payload folder outside
   `Extensions` with the IDE closed and updates the marker. This needs elevation
   and is reversible. Do not delete `.suo` files or toolbox caches. Use a Shell
   activity log only on a controlled reproduction.
9. **Shutdown:** close the IDE normally; no owned Assistant process remains.
   Unrelated IDEs and TwinCAT services stay untouched.
10. **Update and rollback** to the previous payload (item 3); configuration,
    chats, logins and project bindings remain.

Record input, expected and observed result and exact versions per step. Then
update the gate status in `native/host-targets.json`. A green inventory,
static test or build does not change a gate status.

## Installer integration (contract)

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Get-AssistantHostInventory.ps1 `
  -RequireAlphaTarget -OutputPath <ignored-path>\host-inventory.json
```

- Exit code `0`: at least one host is eligible. Exit code `3`: no eligible host.
  Any other code is an error and must abort the installation.
- `evaluation.hosts[]` gives `hostId`, `kind`, `architecture`, `targetId`,
  `classification`, `installerDecision` (`eligible`/`refuse`), `reasons`,
  `warnings`, per-gate `gates` and `openGates`. `facts.hosts[]` with the same
  `id` gives `installRoot`, `executable`, `assistantPayloads[]`,
  `configurationChangedMarker` and `running[]`.
- Install only into hosts with `installerDecision = eligible`. Use the target's
  `payload.installDirectory` and `payload.configurationChangedMarker`, both
  relative to `installRoot`.
- `running[]` not empty: ask the operator to close that IDE and re-run the
  inventory. Never terminate an IDE.
- Warnings such as "N Assistant payloads are discoverable", a payload
  architecture mismatch or an unexpected installation mechanism must be resolved
  or shown to the operator; never add a second payload next to an existing one.
- `-InventoryPath` re-evaluates a recorded document (any platform), for example
  in CI or for support.
- Paths below the user profile are reported as `%USERPROFILE%`. Process command
  lines, solution paths and AMS Net IDs are not collected.

## Verification performed for this record

- Live read-only inventory on this machine under Windows PowerShell 5.1 and
  PowerShell 7.6. The set of IDE processes was identical before and after.
- `node --test tests/host-inventory.test.mjs`: 12 checks, also with
  `TC_TEST_POWERSHELL=pwsh`. They cover target/manifest contracts, a
  read-only source contract, synthetic classification of every matrix row and
  failure mode, re-evaluation and exit codes, and the live collection when
  running on Windows.
- `native/Tests`: 13 host-classification regressions (145 checks in total).
- Both IdeHost payloads compile with the new Diagnose row. The compiled reader
  was loaded in a 32-bit PowerShell outside any IDE: it read TwinCAT
  3.1.4024.78 from the registry and classified the process as `unknown`.
  The payload was not installed, and the IDE-Host row has not been seen in a
  real IDE.

## Open

- All open gates above, on the alpha target itself.
- TcXaeShell64, TwinCAT 4026 and VS2022/2026: no host available on this
  machine. A separate machine or VM is required; the existing 4024 installation
  must not be migrated.
- TwinCAT inside the installed VS2019 Community: not launched. Whether it loads
  TwinCAT and the Assistant would need a new payload target and an operator
  decision.
- Windows versions other than Windows 11 25H2/26200 and other 4024 builds have
  no record.
- The WebView2 minimum runtime for SDK 1.0.4258.31 is not established. Only
  observed runtime versions are recorded.
