# Implementation status

**Product cleanup branch, 2026-11-09.** The next source/runtime change removes
unused server services, specializes the foreground CLI, prunes unreachable
feature files and native dependencies, and applies TwinCAT Assistant identity
to product text, the logo and the About page. See
[the cleanup record](PRODUCT_CLEANUP_20261109.md) for its exact scope and checks.
It is separate from the published Preview 3 and has not been installed on the
operator workstation. Remaining shared helpers and generic client entry points
are explicitly recorded; this does not close Roadmap 2 or installed UI gates.

**Preview 3 follow-up, 2026-11-09.** The first-menu-click defect has been traced
to the installed SDK 15 synchronous tool-window creation fallback and fixed with
an explicit frame-show call. The x86 host is rebuilt as `0.2.0.3`; the complete
`0.2.0-preview.3` application package passes protected startup, both real MCP
probes on synthetic files, restart, state preservation and unchanged-package
verification. See the [Preview 3 release notes](releases/native-v0.2.0-preview.3.md).
The installer retains the graphical setup and automatic administrator request.
This candidate uses a local Windows package/installer build, reusing the pinned
Core/Chat/MCP inputs; it is not claimed as a new hosted-CI installer artifact.

The unmanaged workstation prototype was removed with verified backups before
ordinary installer use. The operator installed Preview 2 Setup 1. Its inherited
developer-path profile was rejected by packaged policy verification; a bounded,
backed-up migration changed only twenty managed configuration/receipt files for
the global profile and nine project workspaces. Strict installed profile
verification now reports current configuration. Chats, credentials, cookies and
PLC source files were not migrated or reset. See the [local migration record](PROTOTYPE_PROFILE_MIGRATION_20261109.md).
Live reconnection, the installed Preview 3 first-click behavior, real managed
update/rollback and first-after-logon remain unconfirmed. Points 5–7 remain open.

**Publication completed, 2026-11-09.** PR #15 merged as `b82f19a`; the saved
preparation helper/handoff was integrated as `edd68ea`. The exact source
`3292137` passed the actual [Windows installer pipeline](https://github.com/Auda29/twincat-assistant/actions/runs/42220003076)
and portable/source checks. The [0.2.0 Preview 2 alpha release](https://github.com/Auda29/twincat-assistant/releases/tag/native-v0.2.0-preview.2)
is published with its CI-built EXE, SHA256.txt and CI provenance. The downloaded
EXE is 240,836,096 bytes, SHA-256
`91b490bf40b6c8f9c64868de1a4b84e34fff188a97b967c08f19b75682472402`,
matching the CI checksum; provenance binds source `3292137`, base `b0bf7c2` and
application input manifest `6ee4eb9b...`.

Only `main` and the still-needed recovery branch remain on the remote. Local
worktrees, build outputs and recovery backups were retained. The normal working
checkout is still `f6e018a`/runtime `491ed3a`; no new workstation installation
was performed. Existing unmanaged prototypes require a verified one-time
migration before ordinary installer use. Installed host/UI/member/paste,
real update/rollback and first-after-logon acceptance remain open for Roadmap
1–4; publication does not close them. Points 5–7 were not started.

The dated entries below retain the earlier integration/acceptance history.

**Resumed on operator request, 2026-11-06.** The isolated integration checkout now
contains the private native test configuration, completed scoped package
verification, and the P2 recovery preflight fix. Its complete serial Node suite
passes 159/159 with no skips; the rebuilt x86 host passes 151 native regressions
and 29 private-configuration checks. The fresh complete package passes real
protected startup, both MCPs on synthetic data, restart and state preservation.
See [the integration record](INTEGRATED_ACCEPTANCE_20261106.md) for artifact
identity and remaining checks. Hosted portable/source CI for `c1f080e` has now
passed all three jobs. The subsequent native maintenance guard `c5790d6` passes
65 synthetic guard checks and 29 private-configuration checks and requires its
own final package/startup verification. The
[local maintenance proposal](LOCAL_MAINTENANCE_ACCEPTANCE_PLAN.md) is being
prepared without changing the working installation. Roadmap items 1, 3 and 4
retain their uncompleted installed-host/UI gates.

The normal working installation was restored after yesterday's experiment.
The original native prototype, original user configuration and verified backend
source remain active; the primary checkout stays on recovery `f6e018a`, with
runtime source `491ed3a`. Today builds and tests use a separate checkout and
synthetic state. See [the recovery record](LOCAL_RUNTIME_RECOVERY_20261105.md).
The [pause handoff](PAUSE_20261105.md) remains a historical snapshot.

Actual installation update, **2026-11-05**: the first CI C0 EXE was installed
with operator-confirmed same-user UAC into the real x86 alpha host. All 22443
application inventory entries and the installed extension snapshot were checked;
the original 13-file prototype remains unchanged in backup. The actual installed
x86 configuration loader selected the package and separate synthetic state.
Its native session reached protected backend readiness twice; restart preserved
the sentinel and an independent observer recorded launcher exit 0. These were
outside-IDE checks, not visible host acceptance. C1 update/rollback and IDE gates
remain open. See [the actual installation record](HOST_INSTALLATION_20261105.md).

Completion-round update, **2026-11-05**: the real
[Windows CI run 42034006930](https://github.com/Auda29/twincat-assistant/actions/runs/42034006930)
passed reviewed input import, protected package startup, both actual MCP probes
on synthetic data, restart/state preservation, EXE build and embedded inventory
verification. Its downloaded EXE matches the CI checksum. This proves that
candidate's CI chain, not a real host installation. The later integrated
candidate is `0.2.0-preview.2`, native version `0.2.0.2`; its own package/CI
evidence must be recorded separately.

The [delivery scope](DELIVERY_SCOPE.md) now blocks excluded runtime endpoints,
removes the redundant distribution Core and server tests, and replaces our
product labels while retaining third-party contracts and notices. The
[direct-host evidence](HOST_ACCEPTANCE_20261105.md) covers isolated project
switches, dirty/save semantics, oversized POU references, reopen and two IDEs.
It does not close visible UI, member-editor, actual install/update/rollback or
first-after-logon gates. The integrated C1 prompt has 17 real model answers,
34 pinned-renderer renderings in an x86 WPF WebView2 fixture and 40 exact
code-copy payloads; the offline evidence verifier, 21 combined
prompt/migration/recovery tests and the subsequent complete serial Node suite
(145/145, no skips, 166.79 seconds) pass. Installed IDE UI and actual TwinCAT paste
remain open. C1 is forward-compatible with exact scoped C0 policies; the old C0
runtime cannot read C1. Installer rollback policy compatibility is still being
implemented and is not proven by the prompt tests. See
[prompt acceptance](PROMPT_ACCEPTANCE_20261105.md).
The separate hosted [Portable checks run 42042008010](https://github.com/Auda29/twincat-assistant/actions/runs/42042008010)
completed successfully for exact source `491ed3aa687248a46021830d93e9c0e09e209a99`:
Linux components, Windows lifecycle and source-runtime jobs all passed. This
includes the fixture portability/Windows temp fixes, actual source build,
runtime/data compatibility and original provider/login/streaming/tool/MCP
regressions. It precedes the new installer policy-downgrade implementation,
which still needs its own tests and final package CI.
[The handoff](ALPHA_CI_HOST_HANDOFF.md) contains the isolated
state, backup, UAC and unmanaged-prototype recovery boundaries.

Integration update, **2026-11-05**: the Windows installer candidate now builds
from an explicit application package and implements host registration, update
and rollback for the alpha target. Synthetically tested transactions preserve
operator data and restore the previous extension after staging/swap failures.
The native host now consumes the selected packaged runtime and validates its
entrypoints before launch. The combined implementation passes all 129 Node
tests, and the x86 host builds. Manifest-checked sibling package relocation now
supports existing global and project MCP configuration on update and rollback;
both versions must recognize the precise prompt policy. Relocatable MCP input
builds are implemented and real Engineering/Knowledge relocation probes passed.
The owner has confirmed Knowledge bundling permission, recorded under
`upstream/permissions/knowledge-0.1.1.txt`. Both MCP runtimes and exact runtime
notices are staged. The full application and 524 MB installer candidate now
build; real protected startup, both MCP calls, preserved restart state and all
22,442 embedded file hashes pass. Real-host acceptance remains open. See the
[local delivery build record](ALPHA_DELIVERY_BUILD.md),
[application inputs](APPLICATION_INPUTS.md),
[installer contract](INSTALLER_CONTRACT.md) and [relocation limits](INSTALLER_RELOCATION.md).

The revised Analysis system prompt is integrated into global and project chats,
with exact recognition/migration of the two previous managed prompts. Thirteen
prompt/migration tests pass; actual model answer quality and embedded renderer
acceptance remain open. The large-object saved-reference fallback is now also
covered by the system prompt. A rollback to an older prompt implementation needs
compatible managed configurations; extension-file rollback alone does not prove
this. See [profile prompts](PROFILE_PROMPTS.md).

Host inventory update, **2026-11-05**: read-only machine discovery confirms
TwinCAT 3.1.4024.78 and TcXaeShell 15.0.0.0/x86 as the first alpha installation
target. Full VS2019 with XAE integration is present but unaccepted; Shell64,
4026 and full VS2022/2026 are not installed. The
[host acceptance matrix](HOST_ACCEPTANCE_MATRIX.md) records remaining live
gates, including restored-editor cold startup. The inventory script, target
description and native diagnostic row are implemented; no host payload was
installed or real-host acceptance performed for this update. After integration
with the large-object reference fallback, 12 host-inventory tests and 151 native
policy/context/chat regressions pass. This does not close roadmap item 1.

Updated: **2026-10-29**. Operator package: `0.1.0-dev.1`; installed x86 prototype:
`0.1.9`, installed after operator coordination. The source prerelease version is
`0.1.9`, described in [release notes](releases/native-v0.1.9.md). It adds automatic
documentation setup and a shared Knowledge connection to the read-only MCP
integration in 0.1.8 and the UI redesign in 0.1.6.
These versions describe different
components, not a TwinCAT product release.

Candidate **0.1.4** on `feat/project-bound-chat` implements the next chat-integration
goal. It retains OpenChamber's chat, adds an exact persistent project/session
binding, explicit POU composer attachments, provider consent and bounded
reconnection. Local checks pass: 42 Node tests, 8 browser contracts, 69 native
policy/context/chat checks, 21 WPF view checks, 20 compiled-control/WebView2/runtime
integration checks and 3 Windows/native Node runtime checks. Both VSIX targets build
without warnings/errors. These are synthetic/component checks, with no model call.
The x86 update is installed with all 13 payload hashes verified and 0.1.3 backed
up outside the extension search path. A normally launched `ContextChecks` host
loads the command/tool window, resolves both same-name POUs distinctly and starts
the local listener. Current Linux and Windows CI checks pass at `6310628`.
The operator confirms the compact header, PLC_B project-chat binding and visible
FB_Motor composer reference after an explicit attachment in the installed host,
without submitting a message. The operator also confirms the PLC_A mismatch
refusal and restored PLC_B assignment after switching back. The subsequent 0.1.5
provider fix and real model response below complete the scoped x86 chat goal.
Computer Use fails with
`native pipe unavailable` / `os error 2`; component checks remain separate evidence.
See [the integration contract](CHAT_INTEGRATION.md).

## Current position and next action

- `feat/twincat-runtime-ui` now builds the pinned OpenCode implementation
  unchanged and connects the existing OpenChamber composer through a source
  contract. Durable project identity, authoritative history restoration, legacy
  chat compatibility and explicit attachment observations are implemented.
  Runtime/provider/MCP checks use isolated profiles and synthetic credentials.
  This source candidate has not replaced the installed release. Module removal
  remains a joint next step. See [Runtime integration](RUNTIME_INTEGRATION.md).

- Native 0.1.9 on `feat/knowledge-auto-setup` adds bounded local help discovery,
  source/version selection, background setup, cancellation/retry, changed-source
  detection and Knowledge reconnection without replacing the project chat.
  Import, validation and activation reuse the pinned Knowledge CLI. The local
  German InfoSys `.47` archive is imported and activated with 49,970 articles;
  the actual MCP status and protected native runtime connection pass. Fixtures
  use synthetic archives and no model request. Exact implementation and
  verification boundaries are in [documentation setup](KNOWLEDGE_SETUP.md).
  The x86 0.1.9 candidate is installed with all 13 payload hashes and the 0.1.8
  rollback copy verified. The operator profile now selects the managed German
  documentation with provider/model policy preserved. The operator confirmed
  installed Knowledge readiness, then found missing model-visible search details.
  A follow-up Node adapter now shares one backend across native chat contexts,
  avoids a disposable full-load process during protocol negotiation and retains
  structured results as complete model-visible text. Search and source-topic
  retrieval pass with the full corpus. Three warm searches take 617 ms and one
  topic read 452 ms; a fresh full load still takes about 108 seconds. The running
  IDE needs one restart to select the adapter; VSIX payloads are unchanged.
  The operator reports that the combined model test now reads saved project
  code and library references, opens the matching documentation using its
  returned topic ID and cites it. Live PLC behavior and resolved library
  versions remain outside this evidence.
  Local checks pass: 59 Node, 8 browser, 33 WPF view
  and 43 compiled-control/runtime checks; both VSIX targets and the Windows
  ownership helper build without warnings/errors.

- Preview release 0.1.8 connects the existing Knowledge and TwinCAT MCPs to native
  project chats. Project identity is independent of supported POU attachment;
  each exact saved PLC project gets a private immutable OpenCode workspace.
  General chats have Knowledge tools only. The operator has no documentation
  corpus configured; the UI and corpus-status tool report that state. Real MCP
  process checks pass with two synthetic PLCs and a synthetic activated corpus,
  including same-name source isolation, foreign-project refusal and structured
  documentation sources. Compiled native tests cover project switching,
  reconnection and project-only context without making a model call. Local
  checks pass: 51 Node tests, 8 browser contracts, 80 native shared checks,
  28 WPF view checks and 37 compiled-control/runtime checks without a corpus;
  both VSIX targets build without warnings/errors. Linux and Windows CI pass for
  the implementation. Distribution is source-only. On 2026-10-28, the local x86
  payload is updated to 0.1.8 with all 13 payload hashes and the 0.1.7 rollback
  copy verified. The old payload remains outside extension discovery. Both MCP
  paths are configured without a corpus, preserving existing host settings.
  Actual calls through these configured server paths read a synthetic saved POU
  and return Knowledge corpus status `not_available`, without a model request.
  The IDE remained closed; installed startup/project-context acceptance and a
  real documentation corpus remain open. See
  [setup, boundaries and checks](NATIVE_MCP.md) and
  [release notes](releases/native-v0.1.8.md).

- Candidate 0.1.7 on `fix/restored-editor-binding` binds native editor services
  when the shell restores its docked Assistant pane. Version 0.1.6 only bound
  those services through the menu command, so automatic chat startup could leave
  the pane without a tracker despite a readable active ST POU. Both 0.1.7 host
  packages build without warnings/errors. Local checks pass: 47 Node tests,
  8 browser contracts, 28 WPF view checks and 31 compiled-control/runtime checks.
  The new compiled-pane regression
  reproduces the omission in installed 0.1.6 and passes in 0.1.7; missing-service
  binding also clears stale local source. The operator confirms that rerunning
  the existing Assistant menu command restores the active FB in installed 0.1.6.
  The x86 0.1.7 fix is then installed with all 13 payload hashes verified and
  0.1.6 preserved outside extension discovery. Automatic restoration after
  restarting the installed IDE still needs acceptance. See
  [active-editor details](ACTIVE_EDITOR.md).

- Candidate 0.1.6 implements the selected mockup A. One native context bar follows
  OpenChamber's palette, with ST preview on the POU name and diagnostics in the
  overflow menu. Local sign-in, project-chat creation/reopening and connected
  provider enablement are automatic. Automatic POU attachment is an optional,
  persisted setting, off by default; provider transfer still requires submitting
  a message. Local checks pass: 47 Node, 8 browser, 76 native policy/context/chat,
  26 WPF view, 31 compiled-control/WebView2/runtime and 2 native Node runtime
  checks. Synthetic fixtures make no model call. The x86 package is installed
  with all 13 payload hashes verified; 0.1.5 is preserved outside extension
  discovery. Only the existing empty extension marker's timestamp was refreshed.
  The IDE stayed closed, so startup and live UI acceptance of this candidate
  remain open. See [the current
  workflow](CHAT_INTEGRATION.md).

- Candidate 0.1.5 fixes the reproduced provider/model consent deadlock in 0.1.4:
  an authenticated provider hidden by `provider.use` deny can be explicitly
  selected/granted before choosing a model. The operator's OpenAI sign-in is
  preserved. It also fixes the wrong config-store export in the embedded adapter;
  the strengthened fixture verifies the actual picker catalog without injecting
  a synthetic model selection. Local checks pass: 44 Node, 8 browser, 69 native,
  21 WPF view, 22 compiled-control/runtime and 3 Windows/native runtime checks.
  Both VSIX variants build without warnings/errors. The unchanged Windows helper
  was built to a separate local output because its current binary belongs to the
  running chat. The x86 0.1.5 update is now installed with all 13 hashes verified
  and 0.1.4 backed up outside extension discovery. Its Assistant command/window,
  distinct PLC_A/PLC_B direct reads and chat listener pass the installed smoke.
  Linux and Windows CI pass at `6902f0c`. The operator confirms that OpenAI is
  visible in the new selector, explicit consent succeeds and a chat model can
  be selected. The operator confirms the actual reply `bRun := TRUE;` after
  explicitly attaching PLC_B/FB_Motor and asking for its assigned value. The
  scoped x86 chat-integration goal is complete; this does not close the wider
  native MVP, extended lifecycle, amd64 or MCP/corpus acceptance gates.

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
  login page are user-confirmed. The subsequent 0.1.5 core chat/provider flow is
  accepted as recorded above. MCP installed acceptance remains open; this is not a complete
  chat/agent MVP acceptance.
- Installed 0.1.5 core chat/provider flow is accepted in `ContextChecks`;
  native-to-MCP transport is implemented in candidate 0.1.8. Local component tests also cover explicit
  composer attachment and session binding.
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
- Native active-source reads are Automation Interface snapshots. The current custom editor
  did not expose an EnvDTE TextDocument; exact unsaved editor-buffer text is not
  verified. Methods/actions and non-ST objects have no native source attachment;
  candidate 0.1.8 can retain their PLC project for saved-file MCP access.
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
  first-run/login-page and basic project-bound provider conversation are accepted
  in the tested x86 flow. Wider lifecycle/provider combinations and MCP acceptance
  remain open.
- General unsaved-editor/source/RDT correlation and revision acceptance. The
  synthetic x86 test detected the unsaved comment as `known-dirty`; exact editor
  buffer semantics beyond that observed case remain unverified.
- Live monorepo engineering adapter, operator review integration, live apply or
  rollback, and project-bound compiler diagnostics.
- A supplied Beckhoff corpus or a real provider conversation using both MCPs.
- Native-to-MCP context/session transport. The existing pipe executable and `doctor` remain
  unconnected to the IDE reader and deliberately report native unavailability.
- Full extension install/reload and chat persistence acceptance in OpenChamber.
- Production storage retention, installer/update delivery, signing and release.

Continue with [Windows acceptance](WINDOWS_ACCEPTANCE.md); do not enable writes
because the portable tests pass. No deployment, online change, forcing, PLC
start/stop or runtime write is exposed by this foundation.
## 2026-11-05 local panel and navigation candidate

The embedded session information panel now starts hidden and has a dedicated
right-rail toggle. Its own profile preference survives a service/page restart.
The native project chat bar now supports explicit pinning, including persistent
project preferences and narrow-window focus handling. See
[the runtime behavior](RUNTIME_INTEGRATION.md#optional-session-information-and-pinned-chat-navigation-2026-11-05).

Local verification: X86 and Amd64 hosts build; 132 native policy/context/chat
checks, 99 WPF sidebar checks, 91 actual Host/WebView/runtime navigation checks,
26 adapter checks, 14 upstream settings-registry tests and 12 visibility-hook tests pass. The full UI
TypeScript check and source UI build pass. Existing VSTHRD010 warnings in
KnowledgeIntegration and DiagnosticsIntegration remain. WPF and WebView captures
were inspected for the pinned sidebar and the wide/narrow session panel.
Fixtures use private state and synthetic projects, with no model or PLC action.
This candidate is not pushed or installed and has no live IDE acceptance yet.

The [cold solution-load investigation](SOLUTION_STARTUP_INVESTIGATION.md) remains
open. The captured main thread waited in TwinCAT/Visual Studio toolbox
initialization while restoring a PLC editor. The operator's successful restart
skipped document restoration; this does not establish or exclude an indirect
Assistant effect.
