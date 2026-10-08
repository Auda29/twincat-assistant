# Implementation Roadmap

All implementation milestones remain open. No delivery dates or effort estimates
are committed yet. The sequence first reduces the risks of shell embedding and
engineering access.

## M0: Repository and Target Environment

**Outcome:** A clear baseline without claiming already implemented product features.

- [x] Prepare a private repository and add the MVP and architecture plans.
- [ ] Record the specific TwinCAT, TcXaeShell64, and Windows versions.
- [ ] Check installed Automation Interface, SDK, and WebView2 components.
- [ ] Record existing MCP installations and the available documentation corpus.
- [ ] Define a suitable synthetic ST test project and manual test cases.
- [ ] Record upstream versions and licenses of reused components.

Real customer/machine projects, corpus data, credentials, and user state remain
outside the repository.

## M1: Technical Feasibility in the Shell

**Outcome:** The two largest integration risks have been validated in practice.

- [ ] Install a minimal classic C# tool window in the selected shell host.
- [ ] Load the local OpenChamber web interface using WebView2.
- [ ] Test docking, keyboard focus, resizing, and tool window restart.
- [ ] Identify the correct PLC project and an existing ST block through the
  current IDE instance.
- [ ] Read the declaration and implementation; investigate unsaved editor
  changes and dirty-state detection.
- [ ] Apply an explicitly approved change through the bridge on a synthetic
  test project and evaluate a real build.
- [ ] Check whether the agent's change can be restored in a controlled manner.

The write test is an isolated, manually approved integration test, not approval
for general agent writes. Before/after states are backed up. There is no
connection to a production runtime.

If reliable dirty-state detection or conflict control is unavailable, agent
writes remain disabled. A working chat view alone does not satisfy the agreed
MVP. Any fallback host requires an explicit decision and must not silently be
presented as shell compatibility.

## M2: Project-Bound Chat and Both MCPs

**Outcome:** A usable, initially read-only Assistant inside the IDE.

- [ ] Start the local agent infrastructure or connect to a compatible running
  instance; avoid unintended duplicate starts.
- [ ] Persistently associate the thread with the specific project/IDE binding.
- [ ] Connect both MCPs and test tool catalogs and actual calls.
- [ ] Check the knowledge MCP's protocol profile and `structuredContent`.
- [ ] Visibly attach the current object context; distinguish saved and live context.
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
- Integrate unsaved editor state live and improve conflict handling.
- Improve semantic reference/call analysis.
- Support additional Visual Studio/TwinCAT hosts.
- Add HMI and I/O engineering.
- Evaluate advanced runtime features under a separate safety concept.

## Testing and Evidence Principle

Each feasibility result records the host/upstream version, input, expected
result, and observed result. Synthetic tests do not prove full TwinCAT product
coverage. A practical shell validation cannot be replaced by an ordinary web
browser test.
