# MVP: Scope and Acceptance

## Goal

An agent in a docked TwinCAT tool window answers project questions, uses
source-backed documentation, and modifies an existing ST block on request.
Changes are reviewed, explicitly approved, and then checked with a real build.

The core workflow does not require a connected PLC. Initially, exactly one
specific TcXaeShell64 version will be supported. The TwinCAT build, shell version,
and installed components will be documented before implementation; VS2022 remains
a possible additional host, but is not a guaranteed MVP platform.

## Example Workflow

1. The user opens a solution and the Assistant tool window.
2. The thread visibly displays its project binding.
3. The user asks: "Explain the timeout handling in FB_Station."
4. The agent searches and reads project code through the monorepo MCP and
   retrieves relevant documentation through the knowledge MCP when needed.
5. The user requests: "Add a configurable timeout."
6. The agent creates an object-level diff for the declaration and ST
   implementation. The IDE bridge checks the target object and baseline state.
7. The user approves that exact change. A stale proposal is not applied; the
   current object must be read again and the proposal reviewed anew.
8. The bridge applies the change and executes the approved build workflow.
9. The thread shows the actual build result, errors, warnings, and the agent's
   change. Failed builds remain explicitly marked as failed.

The agent may independently select suitable read tools. This workflow is an
acceptance example, not a rigid user interface sequence.

## Feature Scope

| Area | Minimum Scope |
| --- | --- |
| Interface | Docked tool window, existing OpenChamber threads, and activity views |
| Conversation | Streaming, cancellation, follow-up questions, approvals, and persistent sessions |
| Project binding | Visible binding to the specific solution and IDE instance; no silent target switching |
| Context | Project structure, code search, and POU contents through the monorepo MCP |
| Documentation | Search and topic retrieval with sources through the knowledge MCP |
| Writes | Declaration and ST implementation of one existing block per change request |
| Validation | Real build execution with an unambiguous completion state and associated diagnostics |
| Review | Object-level diff, explicit approval, and change log |
| Rollback | Controlled restoration of the agent's change if the post-change state is unchanged |
| Runtime | Optional use of existing ADS diagnostics, strictly read-only |

A continued thread must revalidate whether its IDE and project binding is still
available. Continuing a conversation does not automatically resume a cancelled
write request.

## Out of Scope

- Creating new POUs, methods, actions, properties, or interfaces.
- Editing method/property contents or changing multiple objects in one request.
- Automatically merging unsaved user changes.
- Downloads, online changes, login/logout, PLC start/stop, forcing, or runtime writes.
- Safety projects and automatic I/O configuration.
- HMI generation, deployment, or preview control.
- A custom model router, general-purpose agent harness, or chat implementation.
- Multi-run, worktree automation, scheduling, and remote access as guaranteed
  product features.
- Support for every TwinCAT/Visual Studio version.

Additional existing upstream features may be hidden. Hiding a feature does not
automatically disable its underlying functionality.

## Acceptance Criteria

- [ ] The tool window runs in the selected shell host; docking, focus, and
  reopening work without hanging the IDE.
- [ ] The thread remains available after closing and reopening.
- [ ] The open solution and target project are correctly and visibly bound,
  including when multiple IDE instances are running concurrently.
- [ ] Switching projects invalidates old write approvals and does not silently
  change the target project.
- [ ] Both MCPs connect using the selected versions.
- [ ] Actual documentation content from `structuredContent` reaches the model;
  sources are traceable in the response.
- [ ] The agent finds and explains relevant parts of the test project without
  pretending to have a complete semantic call graph.
- [ ] An allowed ST target object is correctly modified after approval.
- [ ] Unsaved target objects and changes made since reading reliably block the write.
- [ ] A real build returns the correct status and fresh diagnostics; success and
  failure are not inferred from old IDE messages.
- [ ] A deliberately introduced compiler error is reported as a failed build
  with a traceable diagnostic.
- [ ] The agent's change can be rolled back if the post-change state is unchanged;
  newer user changes are not overwritten.
- [ ] Cancellation, timeouts, or disconnection do not automatically retry a
  change that may already have been applied.
- [ ] Direct file and shell tools cannot bypass engineering approvals.
- [ ] No MVP workflow activates, downloads to, or modifies a runtime.

## Completion

All core criteria are verified against a synthetic test project and then against
a suitable real project. Results record exact versions, the test case, and
observed behavior. A successful build proves compilability, not functional
correctness or machine safety.
