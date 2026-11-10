# Failed project setup, 2026-11-09

The local Preview 3 report showed a connected, signed-in chat without configured
project tools. The loading view then remained visible or reported a disconnected
chat.

## Local findings and recovery

Read-only inspection established that:

- Both installed application packages, Preview 2 and Preview 3, match their full
  manifests. Each contains 22,088 files.
- The nine existing project configurations match known policies. Their
  Engineering configurations are exactly read-only, and the referenced solution
  and project files exist. No PLC source was read.
- Project configurations still reference Preview 2; general chat already uses
  Preview 3.
- Two empty project lock files dated 13:52 and 14:47 block subsequent setup
  through `open(..., 'wx')`. Suspected stale locks are not adopted automatically.

After confirming that no TcXaeShell, Visual Studio IDE or installed Assistant
runtime was running, exactly these two regular, empty lock files were opened
exclusively and moved into a private local backup. SHA-256 verified the backup.
Configurations, journals, chats, sign-ins, PLC files and installed binaries were
unchanged. The exact backup path appears only in the local recovery record,
`local-data/project-lock-recovery-*/recovery.json`.

The interruption that originally left the locks has not been established.
Previously, native setup forcibly ended its Node process after 30 seconds. An
interruption while holding the project lock can prevent its `finally` cleanup.
An isolated adoption run using the actual installed packages took 21.4 seconds;
separate full inventory checks took 15.7 and 10.3 seconds. The former deadline
leaves little margin under I/O load. These measurements do not prove a specific
timeout in the operator's run.

## Source correction

- An existing project lock returns the fixed error code `project_locked`. The
  control channel still contains no paths, content, credentials or exception
  text. Unknown errors remain `setup_failed`.
- The Host distinguishes locks, timeouts and general setup failures. Setup
  remains bounded, now at 120 seconds, accounting for full verification of both
  package versions.
- The separate WebView readiness timer pauses during setup. Setup failure ends
  the loading animation, keeps native actions reachable and shows a persistent
  message with a retry action.
- Diagnostics explicitly shows setup failure for project tools and knowledge
  search. A prepared documentation index does not establish an MCP connection.
  Chat connection status remains independent.
- Automatic lock deletion remains excluded. Permissions are not broadened, and
  unknown configurations are not repaired automatically.

## Evidence and limits

- Actual installed Preview 2 and Preview 3 Node components successfully prepared
  a synthetic project and adopted its state. No backend, provider or MCP server
  was started.
- 24 existing MCP/prompt/profile-recovery tests passed. After correcting its CLI
  file path, the new test passed separately: an active project lock remains,
  project policy stays byte-identical and the actual CLI returns only the fixed
  error code.
- The x86 Host build and 151 native regressions passed. The build used the
  existing EmbeddedChat input from the previous branch build; this does not
  establish a new source-runtime, package or release build.
- `scripts/Test-ProjectSetupFailure.ps1` exercises the actual built WPF control
  outside the IDE with a fresh private test profile. All nine checks passed:
  visible failure instead of a loading animation, reachable actions, truthful
  failure diagnostics and no false disconnect during locked or pending setup.
  No IDE or backend instance was started.

The source fix was not installed at the time of this report. The operator must
check the next actual project startup after local lock recovery. A release
requires coordinated adapter, Host and application-package builds. Integration
with the language PR must add the new Host messages to both DE/EN catalogs.
