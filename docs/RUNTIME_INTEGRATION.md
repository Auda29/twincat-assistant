# TwinCAT runtime integration

This branch implements the first two agreed groups from
[the specialization plan](TWINCAT_RUNTIME_UI_PLAN.md). OpenCode 2.0.23 is built
from its unchanged source at `0fd7e2829449b052abf0078666669302923d77af`.
OpenChamber 2.1.1 at `e302062e3be0686986594fddabdafd8a97c129e5` receives a small
source overlay connecting its existing composer and stores to the native host.
No shell, Git, file or other functionality is removed from upstream sources.
The Assistant's existing read-only permissions remain in force.

Build inputs and commands are described in [upstream](../upstream/README.md).

## Existing data and project identity

OpenCode remains the owner of `data/opencode/opencode.db` below the configured
operator state directory. The channel, schema version and session IDs are
unchanged. Existing provider records and OpenChamber preferences keep their
original paths. This step has no chat database conversion and does not read
operator credential contents.

New project indexes use canonical solution and PLC-project paths, excluding
model choice, MCP configuration and IDE process ID. The index adopts the most
recent matching 0.1.9 workspace in place. All matching historical configuration
directories participate in restoration, ordered by the actual session update
time. Selecting an older conversation revalidates its workspace against this
exact project and supplies the current reviewed MCP configuration there. Existing
directories, session IDs and chat records stay in place. No automatic project
move/rename migration is introduced.

Generated engineering policy is checked against the exact project. A settings
change updates the managed MCP configuration under a project lock. A small
journal makes an interrupted two-file configuration update recoverable. Unknown
policy edits are rejected. Global provider/model permission remains separate.

Native bindings are keyed by operator state root and session ID. The current
listener origin and IDE lifetime remain authorization checks, with live IDE
ownership retained. Legacy bindings keyed by origin/port are adopted without
deleting their original files.

Restoration reads the durable, paged session list through the existing runtime
instead of interpreting an empty UI bootstrap cache as an empty history. An
unavailable history reports an error and offers retry. It does not create a new
chat. Each asynchronous result is checked against the current page/project.

## Context and attachments

The native host provides only PLC project name, active object name, tree path
and source observation type as automatic metadata, including the object kind,
owning POU and exact member path. The existing composer
captures it in synthetic context when composing or queueing a message. It says
explicitly that this is metadata, contains no source, and does not verify the
unsaved editor buffer. General chats and foreign-project chats receive none.

Explicit attachment and the existing optional automatic attachment setting
still stage snapshots through OpenChamber's original reference queue. The
original composer remains authoritative for the visible reference list.

- `staging`: the native reference is waiting in the existing input queue.
- `staged`: the composer confirms the reference is present for the next message.
- `submitted`: the original send operation confirmed admission of that reference.
- `none`: no current staged reference or confirmed submission is observed.

Removing a chip clears the staged observation. A failed send does not create a
successful-submission observation. Sent references do not masquerade as pending
references. Changing session or losing the binding revokes automatic metadata.
The native bar shows `Wird angehängt`, `Vorgemerkt`, or `Erneut anhängen` as
appropriate. After a page reload, the transcript remains the source for earlier
sent references; the bar does not invent a new submission observation.

The automatic attachment uses one stable composer slot (`Aktiver Baustein`).
Its metadata retains the exact POU reference and revision separately from that
slot and its display title. Source composer observations preserve those fields
for staged and submitted references. A same-title POU in another document or
tree path cannot inherit the earlier POU's attachment state. The source
contract is authoritative; the pinned distribution retains its markup-based
chip readback for compatibility.

Active actions, methods, properties and property Get/Set accessors are resolved
from the active document/frame and its exact TwinCAT tree node. A member frame
may expose its owning POU through DTE; the frame's member suffix is then resolved
only within that owner. Conflicting document/frame member identities are rejected.
The bar and attachments use qualified names such as `FB_Motor.Reset` and badges
`ACT`, `METH`, `PROP`, `GET` or `SET`. A property preview includes its accessor
declarations and ST implementations, each labelled separately. Reading another
member never substitutes the parent POU body. Saved-source evidence compares
both owner and member IDs; missing correlation remains unknown. These are still
Automation Interface snapshots, not verified unsaved editor buffers.

Composer references carry `following` or `manual` origin. The original chip row
labels them **Automatisch** or **Manuell**, with translated explanations. **Beibehalten**
freezes the displayed automatic revision as a manual reference; a stale click
cannot pin a different revision that arrived meanwhile. Later editor changes
replace only the automatic slot. Losing active context or disabling automatic
attachment clears that slot and preserves manual snapshots. Automatic staging
waits for pending manual items rather than overwriting the original input queue.

The native loading screen shows the `TwinCAT Assistant` wordmark while the
browser initializes, authenticates and restores the project conversation.
Operational adapter readiness remains separate from `presentationReady`, so
automation can select and bind the conversation while the splash is visible.
The source contract observes the main chat's committed session/directory and
its existing history-loader result, including successful empty history and the
load-error/retry surface. Pinned columns cannot publish readiness for the main
chat. Config initialization, mounted chat markup and a current native binding
are also required. The older distribution retains markup-based readback.

The context bar and chat appear together after this gate. It does not wait for
Knowledge readiness or hidden-WebView animation frames. Navigation failure or
the existing 60-second readiness timeout exposes the native reconnect action;
manual sign-in remains available after the existing automatic attempts. A page
reload resets the presentation gate. Later conversation switches keep the
upstream timeline behavior.

The web HTML fallback and React splash variant use the same TwinCAT wordmark
and blue loading track, with reduced-motion support and theme tokens. Native
IDE embedding uses the presentation gate. Standalone web uses the new splash
branding without native project gating. Electron, VS Code, hosted mobile and
Capacitor are not shipped or rebuilt here; the shared splash variant would
inherit the branding when built with this overlay, without native gating.

Startup presentation and branding are covered by the combined checks below,
including delayed history readiness, page reload and both themes in WebView2.

The Knowledge and engineering MCPs retain their existing implementation. The
shared Knowledge broker remains in use. Its cold corpus validation is a separate
cost, unaffected by building OpenCode from source.

The prepared documentation index and the MCP connection are separate states.
The setup panel keeps its index-ready heading while reporting the live connection
below it. MCP catalog observations are scoped to the selected workspace and carry
explicit observed/stale flags. Provider lookup failures do not erase a successful
MCP read; failed MCP reads retain the last catalog as stale instead of reporting
an empty or disconnected catalog. Unknown status does not request a reconnect.
Periodic index-status reads no longer disconnect a working MCP. Confirmed failure
uses the existing bounded reconnect policy; explicit setup or reconnect can force
one. Reconnect completions are scoped to their page, workspace, corpus and request.

Metadata is captured before asynchronous attachment resolution. Queued inputs
retain the object they referred to when queued; delivery does not replace it
with the editor's later active object.

## Validation scope

The 2026-10-30 combined pass builds both native hosts and the updated source UI.
It passes 92 Node tests, eight browser checks, 97 shared native policy/context
regressions, 52 WPF checks, and 83 actual WebView2 integration checks. The latter
uses both real MCPs with synthetic editor/project/help fixtures. It covers
delayed history readiness, provider/catalog read failures without unnecessary
reconnection, exact member metadata, manual/automatic coexistence, keep/remove,
page reload, history recovery and both themes at 390/768/1100 pixels. The policy
tests additionally cover member monikers, owner/member GUID correlation and
pending-manual queue preservation. Source UI/web type checks, overlay lint,
four original composer test files and three runtime/data-copy integration tests
pass. Dead-code analysis reports the existing upstream backlog.

Real IDE checks must still verify TwinCAT's member monikers and accessor COM
interfaces. These fixture checks do not establish live editor, compiler, model
or machine acceptance. Historical results below refer to earlier commits.

Tests cover the actual upstream provider/integration/session-runner/MCP code,
the original composer assembly, source builds, separate-data-copy compatibility,
project workspace adoption and settings changes, and the compiled native
WebView2 integration. Provider fixtures use synthetic keys and do not make paid
model calls. The compatibility test preserves an actual distribution-created
session, durable user input and provider record across a source-runtime start
on a separate copied profile.

Local source builds and runtime tests do not establish that every real provider
login works, or validate PLC compilation, ADS, live editor buffers or machine
behavior. Installation into the user's running TwinCAT environment remains a
separate action.

The 2026-10-29 local run passed 67 Node component tests, eight browser checks,
80 shared native policy checks, 43 compiled WebView2 checks with both real MCPs
and a small documentation fixture, the separate-profile compatibility test,
and 119 original OpenCode regression cases with one upstream case skipped.
Original composer assembly tests, UI/web type checks and overlay lint passed.
Dead-code analysis reported the upstream backlog; no modules were removed.

### Combined native UI and runtime acceptance

The 2026-10-29 integration combines runtime head `8e374354` with the main UI,
Knowledge preloading/reconnection, English slash commands and loading screen
from `1eea3ba`. It reuses the verified unchanged OpenCode build and rebuilds
OpenChamber with the combined source contract. Both X86 and Amd64 hosts compile.
The combined checks pass 77 Node component cases, eight browser cases,
80 native policy cases, 51 WPF checks, 52 compiled WebView2 checks using the
source runtime with both real MCPs, and 48 compiled WebView2 checks using the
reference distribution. The four runtime/data-copy/MCP integration cases,
UI and web type checks, source-contract lint and four original composer test
files also pass. Fixture profiles, provider records and PLC sources are
synthetic; these checks do not modify the operator profile or establish live
IDE/editor or PLC/compiler acceptance. Deployment remains separate.
