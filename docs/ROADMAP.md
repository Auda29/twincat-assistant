# Implementation Roadmap

Priorities updated: **2026-11-12**. Older implementation/milestone snapshots retain
their dates. [Status](STATUS.md) records evidence; this document defines work order
and exit gates. [MVP](MVP.md) describes the eventual product, not current prototype
permissions. No delivery dates or effort estimates are committed.

The [solution startup investigation](SOLUTION_STARTUP_INVESTIGATION.md) remains
open. Host acceptance must include cold startup with restored PLC editors and the
Assistant window. The observed toolbox wait has not been attributed to Assistant.

## Agreed priorities (2026-11-02)

Release decision, 2026-11-12: Beta 5 keeps the ST source-development code but
disables its release tools, UI and accessors. Selective builds, diagnostics,
navigation, typed Knowledge tools and the pinned-history fix form the usable
release scope. Real-host acceptance follows publication; normal-ST transactions
and durable source rollback remain separate open work.

Development update, 2026-11-12: MCP-first P1 work is in three separate PRs.
Typed Knowledge tools, selective PLC builds, exact native diagnostic navigation,
normal-ST live read/preview and the session-bound Editor profile are implemented
as private candidates. Normal-ST apply/rollback remains blocked by the lack of
a verified exact-slot transaction; existing XML-buffer writes do not close that
gap. No merge, public release, installation or live acceptance is implied. See
[the integration record](MCP_P1_INTEGRATION_20261112.md),
[Editor profile](EDITOR_PROFILE.md) and
[the transaction blocker](NORMAL_ST_TRANSACTIONS.md).

Priority update, 2026-11-10: next focus is TwinCAT 4026 with TcXaeShell64, followed
by explicit project builds and navigable compiler diagnostics. Full Visual Studio
2022 follows Shell64. See the [implementation boundaries and next slice](SHELL64_AND_BUILD_PLAN.md).
4026/Shell64 is not yet installed; no installation/migration or compatibility
acceptance is implied. The seven original items below remain as the scope list,
not the current execution order.

Integration, 2026-11-10: Beta 1 combines product cleanup and saved DE/EN selection
with current startup fixes. The operator confirmed Preview 6 startup in installed
TcXaeShell. Other Host gates and visible language acceptance remain open; items 5
and 7 are not implemented. The dated entries below remain historical evidence.

Language implementation, 2026-11-09: item 6 is implemented on the separate product
branch. Host/Chat share German/English and save the selection; Host restores it
across backend-address changes. Catalog/WPF/browser checks are in
[Language support](LANGUAGE_SUPPORT.md). Installed-shell acceptance of that state
remains open. This work did not begin items 5 or 7.

Cleanup, 2026-11-09: the product branch removes more server services/unneeded
packages, simplifies settings and replaces product text. The
[scope/evidence record](PRODUCT_CLEANUP_20261109.md) also identifies retained shared
helpers/client entry points. That state is separate from published Preview 3.
Item 2 remains incremental cleanup; that work did not begin items 5–7.

Integration, 2026-11-06: private native test configuration, bounded reuse of full
verification per startup and P2 preflight before profile-recovery writes are
integrated. 159 Node tests without skips, 151 native regressions, 29 configuration
checks and complete package smoke with both MCPs/restart pass.
[Evidence](INTEGRATED_ACCEPTANCE_20261106.md) distinguishes this from open Host/UI
acceptance. Fresh shell profiles still load the global prototype; extension
isolation is unproven. The installer therefore refuses the test override on the
shared Host. The working installation remains restored. Items 5–7 have not begun;
the four completion assignments are not all accepted.

Host checkpoint, 2026-11-05: read-only inventory confirms TcXaeShell x86 with
TwinCAT 3.1.4024.78 as the first alpha installation target.
[Acceptance matrix](HOST_ACCEPTANCE_MATRIX.md) and
[`host-targets.json`](../native/host-targets.json) record versions, criteria and
open acceptance. TwinCAT in VS2019 is installed but unaccepted. Item 1 stays open
until real-target checks are complete; alpha classification does not replace them.

Implementation on the same date: installer registration, update and payload
rollback are verified on synthetic hosts. Host resolves runtime startup from the
selected package. Managed-configuration migration between verified sibling
versions is implemented; see [relocation limits](INSTALLER_RELOCATION.md).
Complete package/installer builds pass locally. Knowledge's owner approved
bundling, with approval recorded in the repository. [Build evidence](ALPHA_DELIVERY_BUILD.md)
includes actual package/MCP startup and embedded-payload checks. The actual
[Windows CI run](https://github.com/Auda29/twincat-assistant/actions/runs/42034006930)
passed package startup, both MCPs, restart, EXE build and embedded hashes. Real
Host installation/update/rollback remain open; item 3 is not complete.
Relocatable MCP input builds are implemented/verified; see
[Application inputs](APPLICATION_INPUTS.md).

Analysis prompt C1 is integrated in both configuration paths with migration and
contract tests. [Prompt acceptance](PROMPT_ACCEPTANCE_20261105.md) establishes
17 real responses, 34 x86-WPF-WebView2 renderings and 40 exact copy results.
Installed IDE display and actual TwinCAT paste remain for item 4. Old C0 does not
recognize C1; returning via installer requires reviewed selective policy migration.
See [installer contract](INSTALLER_CONTRACT.md) and [profile prompts](PROFILE_PROMPTS.md).

Further evidence, 2026-11-05: item 2's [delivery boundary](DELIVERY_SCOPE.md) is
implemented and verified with a complete real package. [Isolated Host acceptance](HOST_ACCEPTANCE_20261105.md)
establishes direct editor/project reads in the target Host; visible UI, member
editors and first-after-logon remain open. The integrated follow-up
[`0.2.0-preview.2`](releases/alpha-0.2.0-preview.2.md) is not yet installed or
accepted at this checkpoint. Items 5–7 have not begun.

The following future work was agreed in this order. The list itself does not
mark implementation/acceptance or commit one release date for all seven items.

1. **Define supported hosts and compatibility boundaries.** Select the first
   installable alpha combination, starting with practically checked TcXaeShell
   x86. Investigate TwinCAT 4026, the 64-bit shell and Visual Studio integration.
   Maintain exact TwinCAT/XAE build, shell/VS version, process architecture and
   Windows version. Determine adaptations/packages per combination and verify
   installation, startup, docking, WebView/Chat, project/editor binding, POU/member
   detection, attachments and both MCPs in each actual Host. Record differences
   in unsaved-buffer access explicitly. Successful builds/existing x86 acceptance
   do not establish other-host compatibility. The initial installer may serve the
   verified Host while others receive separate implementation/acceptance. Feed
   findings into editor-profile and inline-completion design.

2. **Prepare the backend for delivery and incremental cleanup.** Separate startup
   from developer checkouts, standardize installation paths and bound a built
   application with a verified manifest. Use the [server inventory](BACKEND_SERVER_INVENTORY.md)
   to remove unused functionality/routes/dependencies incrementally. Rename
   visible/internal OpenCode/OpenChamber identifiers where they denote our Core,
   Chat and Host modules while preserving attribution/licenses/notices. Retain
   providers, logins, models, streaming, cancellation, agent/tool execution, both
   MCPs and conversation persistence. Change paths/keys/formats only with verified
   migration preserving chats/bindings/sign-ins. Complete cleanup is not required
   for the first installer.

3. **Deliver an easy CI-built installation.** Produce a versioned installable
   package from verified CI builds, considering Host/Core/Chat, both MCPs and
   prerequisites together. Require no Git checkout, npm or local compilation.
   Define package format, initial IDE/architecture and runtimes before implementation.
   Verify first installation, update and return to the prior version, preserving
   configuration/chats/sign-ins. Document user actions based on
   [Installation](INSTALLATION.md) and [startup contract](START_CONTRACT.md).

4. **Review profile system prompts.** Start with Analysis and shared future-profile
   rules. Verify readable TwinCAT/ST rendering: appropriate code blocks/indentation,
   distinct declaration/implementation/action/method/property and complete copyable
   excerpts. Distinguish saved code, attached snapshots and verified editor content.
   Test representative questions, sources, uncertainty and actual profile tools/
   permissions. Prompt review may start independently before installer completion.

5. **Add an Editor profile alongside Analysis.** Permit controlled changes in the
   exact bound PLC project while Analysis remains read-only. Reuse agent/tool
   logic; add preview/diff, approval, original-state checks and restoration under
   [Engineering Safety](ENGINEERING_SAFETY.md). Account for unsaved changes/project
   switching. The profile alone grants no writes or PLC runtime actions.

6. **Add localization, initially German and English.** Consistently translate Host
   and Chat menus/dialogs/status/errors, expose and save language choice, centralize
   text and support later languages. Verify completeness/presentation in both.
   Source/conversation contents remain original; account for response language
   in profile-prompt review.

7. **Add inline code completion.** Offer suggestions in the TwinCAT editor separately
   from Chat. Define supported editors/ST objects, trigger, context, model connection
   and acceptable latency. Require explicit accept/discard and invalidate/cancel
   after typing or editor changes. Unsaved text/transmission needs an explicit
   contract; never accept/save suggestions automatically.

The next major milestone is an easily installed version with reliable Analysis.
Editor profile and inline completion build on it. The
[Knowledge startup investigation](KNOWLEDGE_STARTUP_INVESTIGATION.md) remains
separate and open. Write/compiler milestones retain their own acceptance.

## Current implementation branch

As of 2026-10-29, `feat/twincat-runtime-ui` builds the pinned OpenCode source
unchanged and adds a source contract to OpenChamber's existing composer. Project
history restoration, configuration-independent identity, legacy data compatibility
and context/attachment observations are implemented. See
[Runtime integration](RUNTIME_INTEGRATION.md) and the
[stepwise plan](TWINCAT_RUNTIME_UI_PLAN.md). Selecting modules to remove remains
a joint next step. Native installation and engineering milestones stay separate.

## Where we are now

| Track | State | Evidence / next gate |
| --- | --- | --- |
| Integration foundation | Merged in PR #1 | Local CLI, reused upstreams, both MCP fixture integrations and portable safety components; not native MVP acceptance |
| Native read-only prototype | PR [#2](https://github.com/Auda29/twincat-assistant/pull/2) merged on 2026-10-23 | Foundation merged; x86 0.1.2 installed; both 0.1.2 payloads build locally |
| x86 shell smoke test | Passed, user-reported | Menu, opening, docking, closing/reopening; local ST display observed |
| Active-editor-first UX | Implemented in 0.1.2; core x86 flow verified | User/screenshot evidence for same-name PLC_A/PLC_B following and unsaved status; 7 live reader lifecycle/isolation checks; extended host acceptance remains open |
| Native chat and model context | Installed 0.1.5; scoped x86 chat goal accepted | User-confirmed project refusal/return, explicit POU reference, provider consent/model selection and correct real response; MCP and extended acceptance remain open |
| Native project MCPs | Preview 0.1.8 installed in x86 | All 13 payload hashes and the 0.1.7 rollback copy verified; both MCP paths configured and tested with synthetic data. Actual corpus and installed startup/project acceptance remain open |
| amd64 shell acceptance | Blocked | No installed TcXaeShell64 is available for live validation |
| Live source writes / compiler | Outside the current milestone | Future M3/M4 work; no authorization to implement or invoke these now; runtime control remains excluded |

PR #2 merged the native foundation. Native MVP/release acceptance remains open.
Green CI or the x86 smoke test does not establish that acceptance. Supporting
one host does not prove the other.

## Decisions recorded on 2026-10-09

1. **Read-only first.** Current work permits no source writes, implicit saves,
   compiler invocation, activation, download, online change, forcing or runtime
   control. Portable write-policy code is not live write capability.
2. **Two architecture targets.** TcXaeShell/x86 and TcXaeShell64/amd64 share host
   logic but require separate installation and acceptance evidence.
3. **Active-editor-first, chat-first UX.** The current WPF buttons and 200-object
   selector are diagnostic scaffolding, not the intended product interface.
   Normal use must not require Refresh, Read locally or reselecting the FB already
   open in the editor. The assistant should follow the active POU locally and
   present a compact, persistent context/dirty-state indicator beside reused chat.
4. **Discovery is secondary.** Increasing the 200-target cap or adding pagination
   is not the next main deliverable. Optional project search/browsing can follow;
   active-object resolution must not depend on a complete preloaded catalog.
5. **Local context is not disclosure consent.** Automatic following must not
   silently send source to a model, MCP or persisted session, or retarget a thread
   or future approval. Ambiguous binding must clear stale context and fail closed.

## Next slice: M1.2 active-editor following

Implementation is present in candidate `0.1.2`, which is installed in x86.
The user confirms active following and unsaved-change status in the real native
window. Seven live direct-reader lifecycle/isolation checks also pass. Extended
host lifecycle, cold-start/responsiveness and amd64 acceptance remain open.
See [implementation/evidence](ACTIVE_EDITOR.md).
The slice is assessed against these requirements:

1. Resolve the selected IDE, solution, PLC project and active ST POU exactly.
   Names or tab titles alone are insufficient; unsupported/ambiguous editors
   must be visibly unbound rather than guessed.
2. Subscribe to editor/solution lifecycle events. Coalesce redundant events and
   keep COM work on its supported thread without rescanning the entire catalog
   on every activation. Revalidate identity before publishing any delayed result.
3. Read the active object's local context automatically. Distinguish actual
   unsaved editor text from saved-file or Automation Interface snapshots; retain
   `unknown` where exact document/RDT evidence is unavailable.
4. Replace normal diagnostic controls with a compact persistent context status;
   keep manual inventory/read controls optional for diagnostics. Chat remains
   OpenChamber's UI, not a parallel implementation.
5. Verify with a separate synthetic project: switch between two POUs, make
   unsaved changes, switch to a non-ST/no editor, close/reopen the solution,
   reopen the same path, and exercise two IDE instances. No build or PLC action.

**Exit evidence:** exact active-object following without dropdown/refresh/read
steps, no stale or wrong-object display, persistent uncertainty/disclosure status,
and no source transport or writes. Record measured cold-start behavior separately;
the reported first-opening delay is not yet a proven lost-click defect.

## Completed local foundation (not native MVP acceptance)

- [x] Isolated CLI/profile, version-gated loopback start and POSIX supervision.
- [x] Real direct calls to modern knowledge and legacy saved-project MCPs using
  synthetic fixtures and the production bounded client.
- [x] Read-only context extension, reproducible bundle and browser SDK-contract tests.
- [x] Portable proposal/approval/apply/rollback core and authenticated diagnostic pipe.
- [x] Local build/test commands and native acceptance handoff.

The full OpenChamber/model workflow and native host validation remain separate
gates. No diagnostic fixture is a live TwinCAT engineering backend.

## M0: Repository and Target Environment

**Outcome:** A clear baseline without claiming already implemented product features.

- [x] Prepare a private repository and add the MVP and architecture plans.
- [x] Record the x86 TcXaeShell/XAE and local SDK/WebView2 baseline.
- [ ] Record a real TcXaeShell64/amd64 baseline and its prerequisites.
- [x] Record upstream version pins, MCP revisions and protocol configuration.
- [ ] Supply and validate an authorized real documentation corpus.
- [ ] Define a suitable synthetic ST test project and manual test cases.
- [ ] Complete license/redistribution review, including the packaged vendor interop.

Real customer/machine projects, corpus data, credentials, and user state remain
outside the repository.

## M1: Read-Only Native Host and Editor Context (current milestone)

**Outcome:** A usable native read-only host follows the correct active editor,
with actual embedded chat and reliable evidence boundaries on both architectures.

- [x] M1.1/x86: install the prototype and verify menu/open/dock/close/reopen
  (user-reported smoke evidence; keyboard/focus/resize/cold-start checks remain).
- [x] M1.1/x86: display a manually selected ST object locally. This does not prove
  active-editor binding or unsaved-text correctness.
- [ ] M1.2: implement and accept active-editor-first local context, as specified above.
- [x] M1.2 implementation: direct active-POU binding, lifecycle invalidation,
  provenance/uncertainty and compact UI with optional diagnostics.
- [x] M1.2 x86 delivery: install candidate 0.1.2 with verified files and preserve
  the old payload; observe extension/package loading and a visible tool window.
- [x] M1.2 core x86 acceptance: observed PLC_A/PLC_B automatic header following
  and unsaved-comment status; live reader isolation, unsupported/no editor,
  solution close and same-path reopen checks.
- [ ] M1.2 extended acceptance: verify display clearing through all solution/
  window lifecycle cases, focus, cold-start and large-object responsiveness;
  establish broader actual unsaved-buffer semantics before claiming those.
- [ ] M1.3: load the real authenticated OpenChamber UI in native WebView2; test
  focus, resize, safe navigation, connection errors and close/reopen behavior.
- [x] M1.3 connection implementation: protected native runtime start/reuse,
  current-user encrypted local password, real readiness/navigation/error states
  and owned shutdown. Real transport/credential checks pass; installed WebView
  login and full conversation acceptance remain separate.
- [x] M1.3 x86 first-run/login-page smoke: user confirms local password setup and
  the real OpenChamber login page in the installed 0.1.3 window. Authenticated
  conversation and the wider focus/lifecycle matrix remain open.
- [ ] Complete the x86 synthetic-editor, lifecycle and identity acceptance record.
- [ ] Validate amd64 installation targets and repeat the host/editor/WebView2
  acceptance separately in a real TcXaeShell64.

No write, rollback or compiler test belongs to this milestone. Such tests move to
M3/M4 and need separate scope/approval. There is no production runtime connection.

If reliable dirty-state detection or conflict control is unavailable, agent
writes remain disabled. A working chat view alone does not satisfy the agreed
MVP. Any fallback host requires an explicit decision and must not silently be
presented as shell compatibility.

## M2: Project-Bound Agent Context and Both MCPs

**Outcome:** The reused chat/agent workflow can use the correct native context and
both existing MCPs under an explicitly authorized disclosure policy, still read-only.

The completed scoped x86 chat-integration goal delivers installed 0.1.5: a compact native host,
persistent exact IDE/project/session binding, explicit POU composer snapshots,
provider consent and bounded reconnect. The operator confirms the actual model
reply to an explicitly attached synthetic POU. See [implementation and evidence](CHAT_INTEGRATION.md).
Preview 0.1.8 adds both native-to-MCP connections with the existing saved-file
engineering backend and per-project workspaces. Actual tool calls and structured
Knowledge sources pass synthetic-corpus/project checks; no model call is made.
No operator corpus is configured yet. The live/unsaved backend, real corpus and
complete M2 installed acceptance remain separate. See [native MCP integration](NATIVE_MCP.md).

- [ ] Start the local agent infrastructure or connect to a compatible running
  instance; avoid unintended duplicate starts.
- [ ] Persistently associate the thread with the specific project/IDE binding.
- [x] Connect both MCPs and test tool catalogs and actual calls with synthetic
  projects/corpus in the protected native runtime (0.1.8 preview).
- [x] Check the knowledge MCP's protocol profile and `structuredContent` with
  the pinned server and a synthetic activated corpus.
- [ ] Configure an actual documentation corpus and accept installed project/MCP
  behavior with the operator; component checks do not close this gate.
- [ ] Connect accepted native context to the monorepo MCP/session boundary;
  show what context is attached and distinguish saved/live/unsaved evidence.
- [ ] Make startup/reconnection a normal host lifecycle, not a user-managed port
  and refresh workflow; retain authenticated supervision and ownership.
- [ ] Add optional bounded project search/browsing with visible completeness;
  do not require it for the active-editor workflow.
- [ ] Verify activity views, sources, follow-up questions, and cancellation.
- [ ] Close and reopen the thread; revalidate its binding.
- [ ] Test solution switching and multiple IDE instances.

## M3: Controlled ST Writes

**Outcome:** An agent can use exactly the promised write scope.

- [ ] Connect the live engineering backend in the monorepo.
- [ ] Define write contracts for an existing ST target object.
- [ ] Integrate object-level diffs and approval.
- [ ] Recheck the baseline state immediately before applying a change.
- [ ] Block dirty target objects; do not implicitly save someone else's changes.
- [ ] Log the agent's before/after states and actual outcome.
- [ ] Implement controlled rollback with a post-change state check.
- [ ] Restrict general file/shell tools for the engineering agent profile and
  test attempted bypasses.
- [ ] Handle cancellation, timeouts, and process termination without automatic
  write retries.

## M4: Build and Full MVP Acceptance

**Outcome:** Chat, MCPs, writes, and real validation form a complete engineering
workflow.

- [ ] Bind build execution to the specific project binding.
- [ ] Capture build completion, timeouts, and fresh error/warning messages.
- [ ] Provide navigable diagnostics and build output in the thread.
- [ ] Test a successful build and an intentionally failing build.
- [ ] Optionally integrate existing runtime diagnostics in read-only mode.
- [ ] Verify all [MVP acceptance criteria](MVP.md#acceptance-criteria).
- [ ] Document installation, startup, limitations, and tested versions.

## After the MVP

Only after acceptance and explicit prioritization:

- Edit methods, properties, actions, and new blocks.
- Handle multiple objects as a controlled change set.
- Expand beyond the accepted active-POU workflow and improve conflict handling;
  unsaved-state correctness for the current object is already a pre-MVP gate.
- Improve semantic reference/call analysis.
- Support additional Visual Studio/TwinCAT hosts.
- Add HMI and I/O engineering.
- Evaluate advanced runtime features under a separate safety concept.

## Testing and Evidence Principle

Each feasibility result records the host/upstream version, input, expected
result, and observed result. Synthetic tests do not prove full TwinCAT product
coverage. A practical shell validation cannot be replaced by an ordinary web
browser test.
