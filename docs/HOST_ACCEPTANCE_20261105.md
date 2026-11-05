# Isolated x86 host acceptance, 2026-11-05

This record supplements HOST_ACCEPTANCE_MATRIX.md, WINDOWS_ACCEPTANCE.md,
ACTIVE_EDITOR.md and SOLUTION_STARTUP_INVESTIGATION.md. It does not close their
installed UI gates. Global ROADMAP, STATUS and host-targets.json are unchanged.

## Environment and boundary

- Windows 11 Enterprise 25H2, build 26200.9711, non-elevated.
- TcXaeShell 1.22.0.0, executable 15.0.0.0, x86, VS shell base
  15.0.35826.140. TwinCAT XAE 3.1.4024.78, interop 3.4.1.14.
- Installed Assistant manifest 0.1.9. WebView2 runtime 156.0.4314.58.
- Baseline source commit `cebd35a`. The standalone net472/x86 acceptance runner
  links this checkout's production Shared reader/policy sources. It builds using
  isolated SDK selection 10.0.303 and locked NuGet dependencies. No productive
  SDK8 pin, installed extension or installer selector was changed.
- Installed `TwinCatAssistant.IdeHost.dll`: 392704 bytes, SHA256
  `E3D1226CF26D6E3F43C1595016928A7E0F675C5232F424394EF87DF5A5361F00`.
  Delivery application's host DLL: 407040 bytes, SHA256
  `DC1DB4409A98C66A20ACE20D4BE6F237144B682E39A455750BFC42EAA2B70179`.
  Both DLL file versions say 1.0.0.0, so that value alone cannot identify payloads.
  The new delivery payload is not installed and was not accepted here.

No AGENTS.md was found in the worktree or its ancestor directories. Memory
registry search returned no relevant prior guidance. No worker was delegated.
No PLC, ADS, compiler, runtime, activation, migration, login, provider call,
extension installation or real engineering-project action was performed.

## Actual isolation result

`TcXaeShell.exe /RootSuffix AssistantAcceptance<guid>` works on this installation.
The direct DTE RegistryRoot confirmed a distinct
`Software\Beckhoff\TcXaeShell\15.0_IsoShell_AssistantAcceptance<guid>` before
any solution operation. The runner obtains ROT objects only for its newly
launched exact PID, checks the returned HWND's PID, then checks the suffix and
absence of restored documents. It never enumerates unrelated DTE objects.

This isolates Shell settings and synthetic solution data. It is **not** an
isolated Assistant installation or operator-data profile. The activity log shows
discovery of the existing global extension directory. NativeChatRuntime still
uses the shared LocalApplicationData/TwinCatAssistant configuration. Consequently
these runs deliberately never open the Assistant pane, WebView, chat or provider
UI. A copied Shell installation was unnecessary to establish profile isolation
and would not by itself isolate that application data.

The operator PID 5180 was absent at the first live inventory and was never
attached to, restarted or closed. Every test launch records its own PID, UTC
time and suffix. Subsequent inventory protects any pre-existing processes,
including the earlier owned test processes.

## Observations and exit gates

Each runner JSON line records input/expectation, observation and UTC time.
Sanitized metadata is in [the evidence record](evidence/host-acceptance-20261105.json).
Raw activity logs, file fingerprints and synthetic source remain in ignored
`local-data/host-acceptance-<guid>` directories.

| Gate / input | Expectation | Actual observation / limit |
| --- | --- | --- |
| Host inventory | Exact x86 4024.78 target | Live read-only inventory classified alpha-target; no new support variants |
| Isolated Shell | Own PID and unique profile before COM mutation | Confirmed in actual DTE.RegistryRoot on every recorded run |
| Active POU / project switch | PLC_A and PLC_B same-name FB have different identity and FALSE/TRUE body | Passed direct reader in real host; does not prove installed tracker/header events |
| Dirty/saved snapshot | AI edit is dirty; synthetic save preserves body | `Saved=false`, `known-dirty`; after save `Saved=true`, `unknown` without RDT evidence. Snapshot notice explicitly says unsaved editor text is not verified |
| Large saved-reference | Real host source over attachment limit becomes exact reference, with no source body | 20043-character implementation passed production CreateAttachment policy on real reader output; installed composer and saved-object MCP resolution untested |
| Member editors | Exact Action, Method, Property context, no parent substitution | Action was created and saved in synthetic TcPOU. Run blocked before its member result. Exact blocking COM call was not instrumented in that run. No member-editor success; methods/properties not reached |
| No editor | Reader rejects missing active source | Passed in successful real-host baseline run |
| Non-ST / same-path reopen / two IDEs | Clear old identity; distinguish owned IDEs | Passed real direct-reader checks in final run. PID29440 kept PLC_A unchanged while PID3304 read PLC_B. Installed pane/chat isolation remains separate |
| Shutdown | Own host exits normally | Baseline PID35064 and final PIDs29440/3304 exited normally. Member-run PID7404 later closed normally on recovery. Earlier PIDs7768/30380 remain without a main window after normal close requests. No restored Assistant-pane shutdown acceptance |
| Focus, resize, docking, theme, WebView | Visible native interaction | Blocked: Computer Use `list_apps` returned `native pipe unavailable ... os error 2`; no alternative desktop automation used |
| Editor keystrokes, undo, exact dirty buffer | Compare actual editor text / saved / snapshot | Open. AI source setters are not keystrokes or undo acceptance |
| Restored windows / cold first-after-logon | Restored PLC editors, toolbox and Assistant binding after actual new logon | Not performed. All starts here were same-session starts, never substituted for first-after-logon |
| Install/update/rollback and final payload | Parent-coordinated installer transaction | Outside worker mutation scope; installed and delivery hashes differ |

Other installed-host chat, attachment, Engineering MCP and Knowledge gates are
not changed by these direct-reader checks. No 4026, Shell64 or full VS support
is asserted.

Two harness defects were corrected from actual observations: localized nested
PLC projects are resolved via `ITcProjectRoot.NestedProject`, not the assumed
English `PLC_A Project`; an STA OLE message filter retries rejected COM calls
for at most 15 seconds. This limit bounds rejected-call retries, not all COM
execution. A host can still block a synchronous editor call. Member-opening
tests therefore require explicit `--include-member-editors` opt-in and a working
operator/UI recovery route. They are not part of the unattended default run.

## Repeatable direct-host run

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build-HostAcceptance.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-IsolatedHostAcceptance.ps1
```

The wrapper fingerprints the installed payload, top-level configuration,
native-chat and persistent operator-state files. It excludes runtime caches and
logs and records unreadable files explicitly. Fingerprints contain digests, not
secret values. Active OpenCode SQLite DB/WAL/SHM changed concurrently during the
runs. Therefore the wrapper returns 4 when its before/after comparison differs,
even if `runnerExit` is 0. This is not a proof of data preservation or damage.
Inspect both results. Payload and configuration hashes were stable in the
completed baseline comparison; no restoration of live operator data was attempted.

Normal recovery accepts only an owned launch record, validates PID, process name
and start time, and sends WM_CLOSE. It never kills an IDE:

```powershell
native/HostAcceptance/bin/Release/net472/HostAcceptance.exe --close-owned <absolute-own-run-path>/runner.jsonl
```

If a Shell has revoked ROT and has no main window, normal-close automation may
be unable to complete. Keep its ownership record for operator/parent recovery;
do not terminate an unknown process or treat this as a passed shutdown.

## Next actual logon capture, no autostart

`scripts/Capture-HostColdStart.ps1` provides manual Prepare, Launch and Observe
phases. It registers no task/Run key, and never logs off, restarts or authenticates
the operator. **It has not been used for a cold-start acceptance here.**

1. After the parent coordinates the final installed payload, use only a successful
   owned synthetic run's solution and exact recorded suffix. With a working UI,
   open two synthetic PLC editors and the Assistant pane. Record toolbox and pane
   state and normally close that owned Shell. Preserve its `.suo`. No other IDE
   is to be closed for this procedure.
2. Run Prepare below after all Shells have been normally closed. It requires the
   solution, saved `.suo`, ownership record and explicit operator confirmation.
   It pins the complete installed payload, executable, synthetic project/source
   and saved IDE state by SHA256. Do not prepare against a different payload and
   carry that preparation over an update.
3. At the next actual new Windows logon, before starting any TwinCAT Shell, run
   Launch with the actual logon UTC timestamp and explicit first-Shell attestation.
   The script refuses a logon before preparation, an already running Shell, and
   changed inputs. No process-history audit is assumed: first-after-logon remains
   operator-attested unless separately corroborated. A warm restart cannot supply
   this evidence.
4. Only observe the returned owned PID. Record the UTC time Preparing Solution
   actually ends, toolbox readiness, restored editors and Assistant binding.
   Run Observe with those values. Preserve the activity log and record a timeout
   as a timeout, not as an invented completion time. A separate pane-closed
   comparison requires its own prepared state and a subsequent real logon.

```powershell
$run = '<absolute-own-successful-run-directory>'
powershell.exe -NoProfile -File scripts/Capture-HostColdStart.ps1 -Mode Prepare -PreparedRun $run -OperatorConfirmsRestoredEditorsPrepared
# Run only after the next actual logon; replace the time with its actual UTC time.
powershell.exe -NoProfile -File scripts/Capture-HostColdStart.ps1 -Mode Launch -PreparedRun $run -OperatorLogonUtc '<actual-logon-UTC>' -OperatorAttestsFirstShellSinceLogon
powershell.exe -NoProfile -File scripts/Capture-HostColdStart.ps1 -Mode Observe -PreparedRun $run -CapturePath '<returned-capture-directory>' -PreparingEndedUtc '<observed-end-UTC>' -ToolboxReady yes -EditorsRestored yes -AssistantBound yes
```

Launch records host/XAE/payload versions, input hashes, owned PID, session ID,
process start and launch timestamps. Observe writes operator UI observations
separately; it never changes host-targets.json or automatically passes a gate.
Source identity evidence and logon attestation remain distinct.

## Verification

- Acceptance runner build: zero warnings/errors with SDK10.0.303 and locked restore.
- Native policy/context/chat suite: 151 passed, explicitly not live UI acceptance.
- New capture safety guards plus host inventory suite: 17 passed on Windows.
- No extension installation, push, merge, release or PR.
