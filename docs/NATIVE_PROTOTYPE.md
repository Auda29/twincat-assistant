# Native Windows prototype

This branch adds a **read-only development prototype**, not an accepted TwinCAT
release. Both 32-bit and 64-bit shells are targets. Build success is not host
installation, editor correctness, or production approval.

## Components

- `native/WindowsHost`: out-of-process .NET 8 Windows helper. JSON files are
  opened with `CreateFileW` and `FILE_FLAG_OPEN_REPARSE_POINT`, checked by handle
  for regular-file type, directory/reparse attributes and the 64 KiB limit, and
  read without write/delete sharing. This is a leaf-object check, not a promise
  that every ancestor is free of junctions. A newly created state root gets a
  protected current-user-only inheritable DACL before credentials are written.
- Windows supervision uses an unnamed Job Object with
  `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. The root starts suspended, joins the job,
  then resumes. Stop, root exit, stdin EOF, and helper termination revoke the
  entire owned tree. The launch plan and secrets travel over stdin, not a file
  or the helper command line. Only the reviewed runtime environment reaches the
  child. Unrelated IDEs and TwinCAT services are never selected for termination.
- `native/ReadOnlyProbe`: .NET Framework 4.7.2 STA diagnostic client. It attaches
  through the Running Object Table to an explicitly selected existing IDE PID;
  it does not start, restart, open, save, or close an IDE or solution. Output
  contains metadata and character counts, **not declaration/implementation**.
- `native/IdeHost/X86` and `native/IdeHost/Amd64`: separate classic VSIX payloads
  compiled against VS SDK 15 and 17 respectively, with a shared WPF tool window
  and WebView2 host. Managed assemblies are AnyCPU; package initialization rejects
  the wrong process architecture. The payload contains WebView2 native loaders.
  The installed SDK does not need to match the IDE process bitness.
- Shared engineering reader: DTE solution/project discovery, explicit
  `ITcProjectRoot.NestedProject` traversal and full tree enumeration (not just
  `ChildCount`, which can expose only the PLC instance). ST reads are local.
  Target/revision fingerprints include the IDE PID, solution, project and tree
  identity. This is not a durable identity or a future write receipt.

Discovery is bounded to 200 ST targets, 2,000 tree visits per project, depth 24,
and 500 saved-source files/directories. Large projects may produce a partial
catalog. The prototype exposes top-level PROGRAM, FUNCTION_BLOCK and FUNCTION
objects, not a complete member/method/action editor. COM and bounded filesystem
work currently run on the IDE UI thread; latency acceptance remains open.

## Chat and source boundary

The tool window embeds the existing OpenChamber UI; it does not rebuild chat,
authentication, sessions or agent orchestration. Start the reviewed launcher
separately, select its port, and sign in through OpenChamber's own login.
Navigation/frame navigation is restricted to that exact `http://127.0.0.1:PORT`
origin. External intercepted resource requests, popups, downloads and browser
permission requests are blocked. Host objects and WebView messages are disabled.
This is not a network sandbox or permission enforcement for OpenChamber's server.

`Read locally` displays ST only in a separate read-only WPF control. Source is
not injected into the page, chat, model, or MCP. The native solution is **not**
automatically attached to an OpenChamber session. Closing/switching a solution
clears the local target selection and source display. Automatic native session
binding and native-to-MCP transport are not implemented.

The foundation still uses both existing MCP integrations unchanged: knowledge
documentation and the monorepo's explicitly read-only saved-file backend.
`doctor` and the existing pipe executable still report native engineering
unavailable; they are not wired to this tool window's COM reader.

## Dirty-state semantics

The reader attempts exact saved POU correlation through a live XML POU UUID and
name, never name alone. It compares declaration and implementation, checks a
matching DTE document's `Saved` flag, and, in-process, asks the exact RDT document
for `IVsPersistDocData.IsDocDataDirty` without opening/locking/saving it.

- `known-dirty`: explicit document dirtiness or correlated live/saved mismatch.
- `known-clean`: exact saved-source equality **and** an authoritative matching
  RDT clean result, with no failed evidence.
- `unknown`: missing identity, saved source, document correlation, or failed reads.

These are prototype evidence rules, **not verified TwinCAT editor semantics**.
Every state returns `WritesAllowed = false`. No proposals, builds, apply,
rollback, ADS actions, activation, download, online changes or forcing are exposed.
A saved XML read or a green policy regression cannot close unsaved-editor gates.

## Build and safe diagnostics

Requirements: Windows, SDK `8.0.425`, .NET 8 runtime, .NET Framework 4.7.2 or
later, Visual Studio 2022 MSBuild, and a local TwinCAT interop assembly. References
can be selected with `-TwinCatInteropPath`; no Beckhoff binary is committed.
Packaging currently copies the locally installed interop assembly: review vendor
redistribution/licensing before sharing any VSIX. This is not a signed installer.

```powershell
./scripts/Build-Native.ps1 -DotNetRoot 'C:\absolute\sdk-root'
./scripts/Get-NativeEnvironment.ps1 -OutputPath ./local-data/native-environment.local.json
./native/ReadOnlyProbe/bin/Release/net472/ReadOnlyProbe.exe --list
./native/ReadOnlyProbe/bin/Release/net472/ReadOnlyProbe.exe --pid YOUR_IDE_PID
node --test tests/integration/windows-runtime.test.mjs
```

The probe output includes real solution/object metadata. Redirect it only to
ignored local data; do not commit or publish it. Do not run `--projects` output
as a claim of complete inventory: it is a bounded structural diagnostic.

The helper default is `native/WindowsHost/bin/Release/net8.0-windows/WindowsHost.exe`.
An operator may select an absolute trusted path with `TC_ASSISTANT_WINDOWS_HELPER`.
A missing helper fails closed. A crashed Node owner leaves `launcher.lock`:
confirm ownership/cleanup before manually removing that exact stale lock.

Build outputs are separate:

- `native/IdeHost/X86/bin/Release/net472/TwinCatAssistant.IdeHost.vsix`
- `native/IdeHost/Amd64/bin/Release/net472/TwinCatAssistant.IdeHost.vsix`

**Do not install either payload into the user's active IDE without coordination.**
The x86 manifest targets IntegratedShell 15–16; the amd64 manifest uses the VS 17
Community SDK target as a build-time placeholder. Neither is proof that a
particular Beckhoff shell accepts its VSIX installation target/prerequisites.
TcXaeShell64 target registration, install/uninstall, dock/reopen, WebView2 loader
resolution and restart coordination must be tested on the actual host. No
extension files, IDE registry, or running user project were modified in this work.

## Sanitized local evidence (2026-10-09)

- Installed/running host: TcXaeShell 15.0, x86, non-elevated. XAE base 3.1.4024.78.
- Installed WebView2 runtime: 154.0.4258.62. Node: 24.16.0.
- Build tools: VS 2022 Build Tools 17.14; isolated, SHA-512-verified SDK 8.0.425.
- Both VSIX variants compile/package. This does not prove installation or docking.
- Read-only ROT attachment to the existing shell succeeds. Nested-project
  discovery reaches the 200-target cap; declaration and implementation of one
  real ST object are read without outputting source. Dirty state is `unknown`.
- Windows Job Object tests cover cancellation, root exit, helper crash, Node
  owner death, an unrelated surviving process, literal argument quoting,
  duplicate start and pre-start cancellation.
- Real pinned OpenChamber 2.1.1/OpenCode 2.0.23 starts on Windows with protected UI,
  login and isolated session creation. This test uses no customer project, MCP,
  corpus, provider credential or model call. Shutdown ownership is verified.
- Twelve portable dirty-evidence regressions test policy rules only.
- Local checks pass: 29 Node tests, 7 built-panel Chromium contracts, 34 portable
  engineering regressions on Windows, and the separate real-runtime bootstrap.
  The Windows pipe server permits replacement OS instances after an expired
  client's still-open handle; it still serves only one connection at a time.

Still required: unsaved editor tracer on a synthetic project, exact source/RDT
correlation, two-IDE and same-path reopen tests, real docked host installation,
WebView2 focus/navigation/retention checks, real MCP+chat/model-context acceptance,
and the existing upstream vulnerability remediation. 64-bit live-host evidence
is absent. Continue with [the acceptance checklist](WINDOWS_ACCEPTANCE.md).
