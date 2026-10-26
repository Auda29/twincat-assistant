# Implementation status

Updated: **2026-10-26**. Operator package: `0.1.0-dev.1`; installed x86 prototype:
`0.1.3`, installed after operator coordination. Native changes are on
`feat/active-editor-context`, based on merged baseline `24e5d48`.
These versions describe different
components, not a TwinCAT product release.

Candidate **0.1.4** on `feat/project-bound-chat` implements the next chat-integration
goal. It retains OpenChamber's chat, adds an exact persistent project/session
binding, explicit POU composer attachments, provider consent and bounded
reconnection. Local checks pass: 42 Node tests, 8 browser contracts, 69 native
policy/context/chat checks, 21 WPF view checks, 20 compiled-control/WebView2/runtime
integration checks and 2 native Node runtime checks. Both VSIX targets build
without warnings/errors. These are synthetic/component checks, with no model call.
The installed host is still 0.1.3 until the
coordinated update; do not confuse local candidate checks with installed acceptance.
See [the integration contract](CHAT_INTEGRATION.md).

## Current position and next action

- Foundation PR #1 and native foundation PR [#2](https://github.com/Auda29/twincat-assistant/pull/2)
  are merged. PR #2 merged on 2026-10-23; this is not native MVP acceptance.
- Both `0.1.3` VSIX variants build locally without warnings/errors. Current local
  checks pass: 36 Node tests, 8 production-panel browser contracts, 49 native
  policy/context/chat checks and 20 synthetic WPF view checks. Real native Node
  transport and C# owner/credential checks also pass. Earlier CI evidence belongs
  to the merged foundation. Check the workflow runs for the release commit for
  its CI result; local results do not establish native acceptance.
- A source follow-up to `native-v0.1.3` addresses missing first-login chat roots:
  the launcher prepares both isolated chat roots before startup. A synthetic
  authenticated browser reproduced the missing-directory error and confirmed
  its removal after repair; real-runtime regressions cover the directory-scoped
  bootstrap reads. After repair of the running operator profile, the user
  confirmed that **try again** removed the error in the installed x86 Assistant
  on 2026-10-26, but subsequently reported its recurrence with both roots still
  present. A full page reload with **Connect chat** then kept the message absent
  for about a minute according to the user. Real WPF/WebView2 synthetic checks
  also pass across three full page loads. The precise recurrence request was not
  captured; extended lifecycle acceptance remains open. See
  [native chat](NATIVE_CHAT.md#workspace-initialization-follow-up).
- The x86 0.1.3 package is installed after coordinated shell close/UAC approval.
  All 13 files were verified and the old payload was preserved outside the
  extension search path. Extension discovery/package load and a visible tool
  window are observed. Earlier user-reported dock/reopen/local-ST smoke evidence
  belongs to 0.1.1. Core active/dirty evidence below belongs to 0.1.2.
- In the normally launched synthetic host, opening two same-name POUs through
  the IDE command successfully exercises distinct direct-reader identities and
  source content. User confirmation and screenshots show automatic PLC_A/PLC_B
  header changes and `Changes: unsaved changes` after a real unsaved comment.
  The external probe also sees `DocumentSaved = false`, `known-dirty` and the
  changed Automation Interface snapshot in that case.
- Seven additional live direct-reader checks pass: two actual IDE PIDs with the
  same solution/POU, separate saved/unsaved contents, non-ST/no-editor rejection,
  solution close and same-path reopen. The first IDE's user edit remained intact.
  These checks do not prove all in-process display/lifecycle behavior.
- Candidate `0.1.2` implements automatic active-POU following directly through
  the document's TwinCAT ProjectItem, independently of the bounded catalog.
  It checks the native document frame, solution, PLC project and object identity,
  and rejects late reads across lifecycle generations. The native window shows
  compact context/provenance/uncertainty; Refresh/Read/dropdown controls are now
  under Diagnostics.
- Candidate `0.1.3` fixes Connect chat navigating to a stopped service and claiming
  success too early. It can start the protected pinned runtime from a configured
  local profile, sets up a DPAPI-protected UI password in the native window, and
  observes real navigation success/failure and service loss. Real runtime start,
  login, port-conflict refusal and owned shutdown are tested. Native source is
  not attached and a fresh profile has no model/provider enabled.
- Installed x86 0.1.3 first-run password setup and display of the real OpenChamber
  login page are user-confirmed. Real provider conversation/native session and
  MCP binding remain open; this is not a complete chat/agent MVP acceptance.
- Installed 0.1.4 chat/provider acceptance and native-to-MCP transport remain open;
  local candidate tests cover explicit composer attachment and session binding.
  Extended lifecycle and
  reliable active/unsaved-editor correlation remain open. No live amd64 host
  is installed for acceptance. All native writes/builds remain unavailable.

The scoped M1.2 implementation and core x86 active/dirty display are verified.
Extended in-process lifecycle, cold-start and large-object responsiveness checks
remain open. Automated window control is unavailable; the user supplied the
visual core evidence. A direct Automation Interface editor-open call crashed a
test host; the later IDE-command route succeeds.
See [active editor implementation and acceptance](ACTIVE_EDITOR.md)
and [Roadmap: M1.2](ROADMAP.md#next-slice-m12-active-editor-following).
For the new connection behavior and first-run setup, see [native chat](NATIVE_CHAT.md).

## Known prototype limitations

- The diagnostic catalog still stops at 200 ST targets. Missing FBs in that
  selector do not indicate missing objects. In `0.1.2`, its completeness warning
  has a separate persistent status and manual reads do not replace active context.
  Automatic following does not enumerate or depend on this catalog.
- Automatic reads are Automation Interface snapshots. The current custom editor
  did not expose an EnvDTE TextDocument; exact unsaved editor-buffer text is not
  verified. Methods/actions and non-ST objects are visibly unbound.
- COM reads and bounded saved-source correlation currently run on the IDE UI
  thread. Active reads are coalesced at 180 ms with a 1-second fallback poll of
  only the active node. Installed responsiveness/cold-start acceptance is open.
- Cold opening seemed slow. The user clarified that a second click may merely
  have coincided with completion; neither a lost command nor acceptable latency
  has been established. Measure it before changing the opening path.
- Classic x86 shell rejects `/UpdateConfiguration`, even with exit code 0.
  Approved refresh of the existing empty install marker triggered discovery;
  the original menu also required a corrected VSCT parent and merge revision.
- Actual WebView2 rendering, keyboard/focus, native session retention, two-IDE
  isolation and same-path reopen behavior have not been fully accepted.

## Implemented

- Node operator commands `init`, `doctor`, and foreground `start`.
- Exclusive private state initialization, policy integrity checks, isolated
  HOME/XDG paths, restricted inherited environment, explicit source-code
  disclosure consent before provider access, and exact read-tool permissions.
- Loopback-only, password-protected OpenChamber/OpenCode launch with pinned
  version checks, duplicate-start locking, POSIX process-group cleanup and
  Windows Job Object ownership through a native helper.
- A bounded production MCP probe, exercised against both real pinned servers:
  modern documentation protocol and legacy saved-project engineering protocol.
- An installable OpenChamber panel displaying host directory/session context.
  Explicit context attachment, session rebinding and error states; no native
  bridge, file, service or model capability is requested by this panel.
- Portable .NET 8 domain contracts for exact object/baseline-bound proposals,
  operator approval, expiry, one-use serialized apply, pre-write backups,
  readback, partial/unknown outcomes and separately approved rollback.
- Authenticated, bounded, current-user named-pipe diagnostics. Only
  `get-context`, `get-status`, and `read-target` are exposed. The executable
  reports native engineering **unavailable**; domain writes are not on the wire.
- A read-only Windows prototype: secure native JSON leaf reads, private state
  DACLs, environment inventory, PID-selected ROT probe, shared DTE/Automation
  Interface reader, and separate SDK 15/x86 and SDK 17/amd64 VSIX builds with
  WPF/WebView2. See [native prototype](NATIVE_PROTOTYPE.md) for limits and evidence.
- Active-document-first local ST reads, selection/RDT/document/solution event
  subscriptions, generation-guarded publication and cleanup, plus a compact native
  context view with a separate local preview and optional manual diagnostics.

## Evidence and limits

`npm run check` exercises Node contracts and the actual built panel in Chromium
through the real SDK postMessage boundary and a labelled test host. This is not
installation acceptance in the real OpenChamber UI or WebView2.

The .NET regression runner exercises production domain code, private files and
real local named pipes. Its in-memory engineering adapter is synthetic and
exists only in tests. No COM, Automation Interface or compiler result is faked.

The upstream integration suite uses real installed OpenChamber, OpenCode and
MCP processes. Project and corpus fixtures are synthetic; a successful fixture
call does not establish Beckhoff documentation coverage, a live IDE binding,
model-context acceptance or machine safety. OpenCode `2.0.23` registers MCP
plugins lazily: listing agents warms the location before polling `/api/mcp`.
The integration test uses that read-only operation, not a model request, and
asserts discovery of both configured servers. The exact commands are in
[installation](INSTALLATION.md). Native MVP acceptance boxes remain open.

## CI boundary

`Portable checks` runs the root audit, Node tests, production-panel browser
contracts and the portable .NET runner on Linux. A separate Windows job builds
the JSON/Job Object helper and exercises operator/process-tree regressions plus
the real pinned runtime's authenticated bootstrap. Shared dirty-evidence tests
run on both. CI does not install TwinCAT, build/load the vendor-dependent VSIXs,
clone private MCP repositories, or validate unsaved editors/model answers. A
green lifecycle job is not native acceptance. See
[security audit](SECURITY_AUDIT.md) for release blockers.

## Not implemented / external acceptance required

- Complete x86 lifecycle/focus/resize/cold-start acceptance beyond the confirmed
  smoke test; actual installation and all native acceptance in TcXaeShell64.
- Extended installed lifecycle/focus/responsiveness acceptance of the active-editor
  context and compact host UI beyond the observed x86 core flow. Native WebView2
  first-run/login-page flow is observed; authenticated conversation/session and
  provider/MCP acceptance remain open.
- General unsaved-editor/source/RDT correlation and revision acceptance. The
  synthetic x86 test detected the unsaved comment as `known-dirty`; exact editor
  buffer semantics beyond that observed case remain unverified.
- Live monorepo engineering adapter, operator review integration, live apply or
  rollback, and project-bound compiler diagnostics.
- A supplied Beckhoff corpus or a real provider conversation using both MCPs.
- Native-to-MCP/session binding. The existing pipe executable and `doctor` remain
  unconnected to the IDE reader and deliberately report native unavailability.
- Full extension install/reload and chat persistence acceptance in OpenChamber.
- Production storage retention, installer/update delivery, signing and release.

Continue with [Windows acceptance](WINDOWS_ACCEPTANCE.md); do not enable writes
because the portable tests pass. No deployment, online change, forcing, PLC
start/stop or runtime write is exposed by this foundation.
