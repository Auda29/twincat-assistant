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
directories participate in restoration. The last selected chat is preferred;
the most recently updated active conversation is the fallback. Selecting an
older conversation revalidates its workspace against this
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

### Agreed project chat navigation, 2026-10-30

Implemented on `feat/project-chat-navigation` on 2026-11-02, initially as a
compact WPF picker. The agreed follow-up replaces that popup with a collapsible
native chat sidebar. The embedded source UI continues to omit the generic
OpenChamber sidebar and its project/folder controls, statistics, info and updater.
Existing model/provider settings remain reachable through the native menu.

- Each PLC project may have multiple persistent conversations, with one shown
  at a time. Separate topics do not need to share one ever-growing transcript.
- The clock toggles a left-hand list of this PLC project's conversations,
  showing titles, last activity and observed running responses. The header has
  the project name, **Neuer Chat** and title search.
- In a wide tool window the list sits beside the conversation and remains open
  during selection. In a narrow window it overlays the conversation within the
  tool window and closes after selection. Escape and an outside click dismiss
  the overlay. Opening the list does not mount general repository controls.
- The plus button explicitly starts another conversation in the current project.
- Reopening or switching back to a project restores its last selected
  conversation. Selection must survive IDE/runtime restarts and is distinct
  from a conversation merely receiving a newer update.
- Changing the active POU, action, method or property keeps the selected
  conversation. Context metadata and the agreed attachment behavior follow the
  editor independently.
- General chats remain separate and accessible through the existing menu.

The navigation provides inline rename and **Titel automatisch generieren**.
**Als erledigt markieren** moves a chat to the initially collapsed **Erledigte
Chats** section; **Wieder öffnen** returns it to the active list. These are UI
terms for the original archive/restore session actions and stored archive flag;
conversation IDs, contents and sent attachments are preserved without a data
migration. Marking a chat as done describes the conversation's work state,
not engineering approval of its contents. The selected chat is highlighted.
Switching chats does not send an abort request. Chats with running responses
cannot be marked as done; the action rechecks current host status before
submitting the original archive request. There is no permanent-delete command.
New chats use the original default-title path instead of forcing the PLC name
as a manually supplied title.

The original Core title generator remains in use. It starts from the first
admitted user prompt while the chat still has the exact original fallback
title. The earlier native host supplied the PLC name as an explicit title, so
those existing conversations are considered named and do not automatically get
retitled. New chats created by the installed `dfb50b1` use the original untitled
creation path. The follow-up exposes **Titel automatisch generieren** in each
active conversation's row menu. It uses OpenChamber's original complete-history
loader, completed-turn selection, small-model title request and guarded title
save. Busy/archived chats are excluded. A missing completed exchange or failed
request preserves the existing title. Generation starts only on an explicit
click, not while listing conversations; there is no bulk rename. The first
title/attachment follow-up was installed locally at `5720c5a` on 2026-11-02.
The corrections below were installed locally at `1140c66` on 2026-11-02.
The operator subsequently reported that the remaining tested functions work,
covering the existing-chat attachment, stable button sizing and generated-title
follow-up. This is user-reported acceptance of that installed state, separate
from the next navigation redesign and its automated fixture evidence.

The same follow-up corrects automatic attachment bookkeeping. A failed fresh
editor read or unconfirmed composer queue no longer counts as a successful
attachment. The host retries at most three times per session/page/revision with
a minimum one-second interval, then offers an explicit retry action. A new page,
changed POU revision or restored project context gets its own attempt state.
After confirmed staging, removing the chip still suppresses replay in the same
unchanged context. PRG/PROGRAM uses the same attachment path as FB/FUNCTION;
the parser already accepted all three. A synthetic WebView case makes the first
fresh PRG read fail, then verifies recovery without changing its revision.

The existing-conversation regression uses completed synthetic user/assistant
messages instead of testing only empty chats. It reproduced a missing attachment
with the prior composer implementation: after switching sessions, the original
guest-attachment callback retained the initial reference setter. That setter is
now scoped to a draft identity, so the callback must depend on it. The overlay
updates that dependency; it keeps captured async draft operations associated with
their original chat. Automatic attachment also waits for committed history/input
readiness. Native source attachment preserves the project permission key and
active-object metadata, rather than temporarily changing history scope to a
source revision or clearing the active context.

The attachment button reserves the measured space for every current label in
each responsive mode. Changing between attaching, staged and update states keeps
the clock, new-chat and overflow controls at the same rendered positions. Compact
mode remains icon-only. The picker de-duplicates a repeated leading error
sentence while retaining useful controlled host details.

The reported generated title contained an unbalanced `】【。` sequence in the
stored title itself; WPF did not introduce those characters. Generated-title
validation preserves Unicode and checks control/replacement/unpaired-surrogate
characters and malformed paired CJK punctuation. One malformed punctuation
result gets one further request through the original generator. Repeated invalid
output preserves the existing title. Manual titles and stored historical titles
are not bulk-rewritten. Empty history, unavailable history, a busy chat, failed
provider requests and invalid generated titles have distinct controlled error
codes and German messages.

`Test-ExistingChat.ps1` exercises real history loading, the actual original title
pipeline and durable title updates with canned model HTTP responses. Its two
completed messages are synthetic and seeded only into a guarded private fixture
database. No customer transcript is copied or submitted. The fixture also
overrides native preferences before startup so its auto-attach setting never
writes the operator's preferences. It runs as part of the combined source test
and can be selected directly with `-TestExistingChat`.

Selection metadata is stored atomically as
`native-chat/threads/project-<profile-and-project-hash>.selection` below the
Assistant's local application-data directory. The key includes the operator
state root, solution and PLC-project paths. It excludes IDE PID, listener port,
model and MCP configuration. Conversation IDs and bodies stay in the existing
OpenCode database; archive state remains owned by the existing OpenChamber
session actions. A metadata lookup failure does not create a replacement chat.

The sidebar's open/closed preference is separate presentation metadata in
`native-chat/threads/project-<profile-and-project-hash>.view`, keyed by the same
stable profile, solution and PLC-project identity. PID, listener port and a
project's display name do not determine this preference. Invalid preference
files fall back to the default view and cannot authorize chat operations.

Text/file drafts continue to use the original per-chat stores. Linked composer
references are scoped to the same runtime/directory/session identity, and a
manual reference still in the input mailbox is retained for its original chat.
Neither is transferred to the newly selected chat. Linked-reference drafts are
kept in memory for chat switches; this does not add durable snapshot-draft
storage across a full browser or IDE restart. Sent attachments remain part of
the durable transcript as before.

Each history request has its own bounded completion slot. Project/page changes
invalidate native navigation actions; late metadata cannot reopen a closed bar or
authorize a management action in a different project. General history has a
separate menu entry and remains filtered out of the PLC list. Live IDE
ownership and exact project/workspace checks still apply to selection and
management. No model request or agent permission change is needed to manage
conversation metadata.

The implementation reuses the existing conversation storage, paged history
loading and project binding checks, preserving existing chats and IDs. It remembers selection under the
stable operator/project identity, independently of IDE PID, port and model.
History errors retain their retry behavior and must not create replacement
conversations. Project chats share their configured project tools and Knowledge
connection; opening another chat must not create a separate documentation index.

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

The first agreed composer cleanup uses the existing attachment control's
`filesOnly` mode in the embedded TwinCAT chat, including its narrow layout. A
paperclip opens the original local-file picker directly; the attachment menu
for GitHub/GitLab, Linear and other integrations is no longer offered there.
POU attachments still use the native context bar and existing composer chips.
The generic permission auto-accept shield is no longer rendered in the embedded
composer, and its keyboard shortcut does not cycle permission modes there.
This does not alter stored permission choices, tool rights or permission request
dialogs. The later writing agent's change-application workflow remains a separate
decision. These UI changes await the next combined build/test pass.

The next agreed cleanup covers model/profile controls. The native model picker
starts with its metadata popups disabled and exposes a **Details** toggle. Closing
the picker resets that temporary choice. Model selection, favorites and effort
selection retain their original implementation. The native picker omits **Run
on several models**. Its profile selection, cycling and mentions use the single
`twincat-readonly` profile, displayed as **Analyse** in German. The original ID,
tool permissions, stored conversations and protected configuration format remain
unchanged. Existing built-in profiles are not deleted from upstream sources.

| Surface | Intended model/profile behavior |
| --- | --- |
| TwinCAT embedded WebView, wide and narrow layout | Explicit model details, Analyse profile only, no parallel-model picker action. |
| Standalone web | Existing OpenChamber behavior. |
| Electron desktop | Existing OpenChamber behavior. |
| VS Code | Existing OpenChamber behavior. |
| Hosted mobile | Existing OpenChamber behavior. |
| Capacitor mobile | Existing OpenChamber behavior. |

These additions await combined validation of details off/on/reopen, keyboard
selection, profile restoration, both responsive layouts and retained model/effort
choices. No operator profile or installed runtime is changed by preparing them.

The native side rail is limited to **Chat information** and no longer offers the
panel-configuration dialog. Rail shortcuts use the same single-panel selection.
The native panel filters out other restored tabs without deleting their saved
data; an open unsupported panel is redirected to chat information. Other
OpenChamber runtimes retain their existing panel selection and behavior.

Chat information keeps the existing model, context usage, message counts and
cost overview. Last-turn token/cache statistics, the detailed context breakdown
and raw messages sit behind a **Details** disclosure, initially closed and reset
when changing chat or workspace. The existing calculations, raw-message preview
and JSON copy path remain in use. These local changes still await the combined
build and runtime tests, including restored tabs and panel shortcuts.

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

The next startup change makes the app overlay publish its own committed handoff
state. Native embedding removes that overlay synchronously when the app is ready,
without waiting for a fade or animation frames in a hidden WebView. The native
splash stays visible until both this handoff and the selected history are ready.
Standalone web, Electron, VS Code, hosted mobile and Capacitor keep their existing
startup animation; only the native host activates the TwinCAT embedding flag.

## Docked IDE panels and browser composition

The native chat uses `WebView2CompositionControl` from the existing pinned
WebView2 SDK, including when recreating the browser after process failure.
The windowed `WebView2` inherits `HwndHost` and can draw over WPF auto-hide panes,
leaving only the upper part of Properties or Toolbox visible. The composition
control participates in WPF drawing order, clipping and input routing. Microsoft
documents it as the replacement for this
[WPF airspace issue](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf).
The SDK version, chat API, private browser profile and navigation policy stay
unchanged. Browser composition can have a lower frame rate than HWND hosting;
scrolling, selection and text input remain part of installed IDE acceptance.

The 2026-11-02 local check builds X86 and Amd64 and passes 97 native policy/context
cases, 52 WPF view cases and 98 source-runtime WebView2 integration cases with
both real MCPs and synthetic projects/help. The new regression renders the real
browser and overlapping WPF panels into one WPF bitmap at 390 and 900 pixels.
Pixel checks confirm both surfaces, WPF hit testing reaches each panel's button,
and hiding it reveals the same browser document and chat session. This does not
combine an independent browser screenshot over the native capture, since that
would conceal an HWND airspace failure.

The panels in this regression are synthetic Properties/Toolbox stand-ins.
The change was installed as part of the local `dfb50b1` test build on
2026-11-02. The operator subsequently accepted the requested docking fix in
TwinCAT; the supplied Properties screenshot shows the IDE pane drawing above
the chat. This acceptance does not establish every floating-pane, DPI or
long-running input/scrolling scenario.

## Thread copy and Markdown export

The native overflow menu exposes **Gesamten Chat kopieren** and **Chat als Markdown
speichern**. Both use the existing OpenChamber Markdown formatter and its complete
history loader, including messages older than the rendered/virtualized viewport.
This exports the stored conversation and attached context supported by that
formatter; unsent composer drafts and image binaries are outside the transcript.
There is no new backend, session store or model request. These native commands
are unavailable for an unselected chat or a distribution without the source API.
Other web/desktop/mobile surfaces retain their existing export controls.

The native host performs clipboard and file operations only after a menu action.
Saving uses the standard Markdown save dialog and an atomic UTF-8 write. A failed
history read, changed session/directory/document or oversized result fails without
a partial export. Results are limited to eight million UTF-16 code units; they are
never silently truncated. The bridge passes text and a safe suggested filename;
it does not expose a native filesystem API or change the download-deny policy.

The overlay handoff and thread-export additions were merged through PR #12 and
installed on 2026-10-30 at `8eebfb0`. On 2026-11-02, the operator manually
accepted both copying the chat history and saving its Markdown export in the
installed Assistant. Manual acceptance of these two features is complete.

Startup presentation and branding are covered by the combined checks below,
including delayed history readiness, page reload and both themes in WebView2.

The Knowledge and engineering MCPs retain their existing implementation. The
shared Knowledge broker remains in use. Its cold corpus validation is a separate
cost, unaffected by building OpenCode from source.
The prolonged connection seen with the prepared index on 2026-10-30 remains an
[open investigation](KNOWLEDGE_SETUP.md#open-investigation-slow-knowledge-connection).

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

The 2026-11-02 collapsible-sidebar candidate passes 97 Node cases, eight browser
contracts, both native host builds, 126 native policy/context/chat cases,
210 WPF host checks and 94 sidebar-component checks. The component fixture
renders light and dark views at 340, 390, 768 and 1100 px, including loading,
errors, search, inline rename, running responses and completed conversations.
The actual source WebView2 fixture passes 160 combined checks with both MCPs
and private synthetic projects/history/help. It verifies sidebar/WebView bounds,
focus and clock behavior in both width modes, persisted open state, separation
of general/project preferences, completion/reopening under existing IDs, drafts,
and restoration after an actual owned runtime restart. Existing-chat PRG
attachment and title generation are retained, with synthetic model responses.
The 49 monitored operator/profile/chat files remain unchanged during these
isolated checks. These are local results; the new sidebar still needs installed
manual IDE acceptance. No paid model call or PLC source change is involved.

The existing-chat/layout/title correction passes 146 combined real-source
WebView checks and a separate 37-check existing-chat run with synthetic completed
history and model HTTP responses. The prior callback implementation fails the
existing-chat attachment case; changing only readiness/budget checks does not
fix it. Updating the guest callback's setter dependency restores the reference
to the selected conversation. The positive title test verifies actual durable
Unicode title updates, one invalid-output retry, preservation after repeated
invalid output, and distinct provider-failure reporting. Both themes pass.

Both native builds and 106 native policy/context checks pass. WPF fixtures verify
the actual neighbouring toolbar bounds across the five labels and responsive
widths, error de-duplication and unchanged Unicode titles. The layout regression
fails against the prior build. The 97 Node cases, eight browser contracts,
UI/web type checks, overlay lint and four original composer test files pass.
No customer transcript or paid model request is used for these tests. Real IDE
acceptance of this correction follows deployment separately.

The title/automatic-attachment follow-up passes 97 Node cases, eight browser
contracts, both native builds, 106 native policy/context cases, 52 WPF checks,
62 picker UI checks and 138 combined real-source WebView2 checks with both MCPs
and synthetic data. The combined run verifies PRG attachment after a deliberately
failed first fresh editor read, and that the new native title action rejects an
empty conversation without changing its stored title or session identity.
The 21 original OpenChamber title tests pass, covering completed-turn context,
title cleanup, generation, failure/cancellation, manual edits and runtime changes.
UI/web type checks and overlay lint pass. No paid model request was made; actual
provider-generated titles and the updated menu still require installed manual
acceptance. This run also passes both themes, but does not explain the earlier
installed combined-run theme timeout or prove that intermittent issue resolved.

The 2026-11-02 navigation change passes 96 Node component cases, eight browser
contracts, both native host builds, 100 shared native policy/context cases and
52 WPF view checks. UI and web TypeScript checks, source-overlay lint and four
original composer test files pass. The picker UI fixture passes 41 checks for
light/dark layouts, search, actions, errors, keyboard handling and stale results.

The combined real-source WebView2 run passes 131 checks with both MCPs and
synthetic projects/help. An additional 62-check focused navigation run also
exercises the final picker New action. These runs verify durable rename,
archive/restore under the original IDs, separate PLC/general history, per-chat
text/manual-reference drafts, and restoration of an older selected chat after
another chat gets a newer update and the actual owned runtime restarts. History
failures preserve conversations; project changes close the picker and reject
stale actions. Readbacks from older selection epochs cannot overwrite a newer
native observation.

The integration fixture keeps its picker frame hidden so backend tests do not
capture the operator's mouse; the separate 41-case UI fixture renders the actual
popup. These are local results, not GitHub CI. No real provider request, PLC edit
or operator-database migration is performed by these fixtures.

The local test build `dfb50b1` was installed on 2026-11-02 with all 13 native
payload hashes verified and 38 protected operator files unchanged during the
installation and isolated tests. The installed navigation run passed 62 checks;
a separate installed theme/docking probe passed 32 checks. The installed
combined run passed 96 checks before timing out on the light-to-dark transition
after the resize/capture sequence. That run failed; its cause remains unresolved
despite the independent dark-light-dark probe passing. This is separate from
the earlier 131-check source-worktree result.

After installation, the operator reported that the requested manual checks work:
project-chat navigation and management, per-chat drafts/attachments, restoration
after restarting the IDE, and Properties/Toolbox overlap. This is the manual
functional acceptance for the local X86 build. It does not close the combined
theme-test timeout or establish exhaustive keyboard, DPI, floating-pane or
long-running acceptance. The repeated project names in historical chat titles
were reported separately and are explained by the earlier explicit title rule.

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

## Optional session information and pinned chat navigation, 2026-11-05

Session information remains available through a dedicated button in the right
rail. In the embedded Assistant it starts hidden, including on existing profiles.
The choice is stored through the existing profile settings registry, separately
from the general OpenChamber panel preference. Widening the window does not
activate it. When explicitly opened, it uses the existing inline panel when it
fits and the existing overlay otherwise. Dismissing the overlay also disables
automatic inline display. The detailed Chat information surface remains separate.

The native chat sidebar has an explicit pin button. A pinned bar survives chat
selection, creation, Escape and browser focus changes. It docks beside the chat
when at least 320 px remain for the conversation; at smaller widths it remains
an overlay until explicitly collapsed or unpinned. Existing automatic docking
from 760 px is unchanged. Pinning and open/closed state are separate preferences
per PLC project and operator profile. Older view-state files remain readable.
General-chat pinning lasts for the current host session and does not change a
PLC project's preference. This candidate has not been installed in the operator
IDE or accepted there yet.
Local checks for this candidate: both host architectures build, 132 native
checks, 99 WPF sidebar checks, 91 Host/WebView navigation checks, 26 adapter
checks, 14 settings-registry tests and 12 visibility-hook tests pass. Full UI type checking and the source
UI build pass. These fixtures do not establish installed IDE acceptance.
