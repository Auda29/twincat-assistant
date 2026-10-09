# Implementation Roadmap

Last reconciled: **2026-10-09**. [Status](STATUS.md) is the current implementation
snapshot; this document defines the order of work and milestone exit gates.
[MVP](MVP.md) describes the eventual product, not the permissions of the current
prototype. No delivery dates or effort estimates are committed.

## Where we are now

| Track | State | Evidence / next gate |
| --- | --- | --- |
| Integration foundation | Merged in PR #1 | Local CLI, reused upstreams, both MCP fixture integrations and portable safety components; not native MVP acceptance |
| Native read-only prototype | Draft PR [#2](https://github.com/Auda29/twincat-assistant/pull/2), code baseline `29b0b75` | Windows/Linux CI green; both VSIX variants build; x86 0.1.1 installed |
| x86 shell smoke test | Passed, user-reported | Menu, opening, docking, closing/reopening; local ST display observed |
| Active-editor-first UX | **Next implementation slice; not implemented** | Follow the active POU without manual refresh/read/selection; exact identity and unsaved-state acceptance required |
| Native chat and model context | Open | Real embedded WebView2/login/session/provider/MCP acceptance still required |
| amd64 shell acceptance | Blocked | No installed TcXaeShell64 is available for live validation |
| Live source writes / compiler | Outside the current milestone | Future M3/M4 work; no authorization to implement or invoke these now; runtime control remains excluded |

PR #2 stays draft while native acceptance is incomplete. Green CI or the x86
smoke test alone is not a merge/release gate. Supporting one host does not prove
the other; narrowing a milestone or merging a partial foundation requires an
explicit scope decision.

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

Implement and test in this order:

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
- [ ] M1.3: load the real authenticated OpenChamber UI in native WebView2; test
  focus, resize, safe navigation, connection errors and close/reopen behavior.
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

- [ ] Start the local agent infrastructure or connect to a compatible running
  instance; avoid unintended duplicate starts.
- [ ] Persistently associate the thread with the specific project/IDE binding.
- [ ] Connect both MCPs and test tool catalogs and actual calls.
- [ ] Check the knowledge MCP's protocol profile and `structuredContent`.
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
