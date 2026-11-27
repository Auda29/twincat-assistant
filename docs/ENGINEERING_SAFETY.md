# Engineering Safety and Change Workflow

## Implemented boundary

Beta 12 keeps Analysis read-only with default-deny tool and provider policies.
The project-bound [Editor and Debug profiles](EDITOR_PROFILE.md) connect the
Engineering-owned provider. Each selective build needs independent native
approval. Editor supports approved ST edits, conditional rollback and object
creation; edit and rollback approvals show the exact side-by-side diff.
Debug samples, waits on and watches approved scalar symbols read-only.

Automation Interface edits may persist directly to disk. Supported open XML
buffer edits remain unsaved. The native ST text-document fallback is read-only;
it is separate from the active Automation Interface edit path. See
[Structured Text transaction paths](NORMAL_ST_TRANSACTIONS.md). New objects are
written immediately, with project changes pending save. Creation has no source
journal or automatic rollback. Unknown mutation outcomes require inspection
before continuing, without retrying the operation.
The older portable C# core and diagnostic [pipe bridge](BRIDGE.md) remain separate
test infrastructure; their injected adapter is not a second live compiler/source
implementation. The requirements below still apply to the native integration.

## MVP Safety Boundary

The Assistant may inspect project code and make controlled changes. Builds are
allowed, but must not implicitly deploy anything. The MVP offers no tools for
downloads, online changes, activation, login/logout, start/stop, or forcing.
Runtime diagnostics remain optional and read-only.

Approval must be validated by the executing bridge or engineering backend.
Tool annotations, agent instructions, or a hidden UI toggle do not replace
this validation.

## Project and Object Binding

A request is bound to the specific IDE instance, solution, PLC project, and
target object. Domain identity must not consist solely of a display name.
Project switching, object deletion, and connection loss invalidate pending
approvals.

An OpenCode session ID may be used for correlation, but by itself it is neither
proof of authentication nor permission to write.

## Reads and Unsaved Changes

Results indicate whether they come from saved files or the live engineering
model. These states must not be mixed without making the distinction visible.

Automation Interface mutations refuse conflicting or unverified unsaved editor
changes. Supported XML-buffer edits use their own verified baseline and leave
the buffer unsaved. If a tool requests a save, the user saves explicitly in
TwinCAT and the agent reads the state again before requesting another operation.
If reliable dirty-state evidence is unavailable, the affected operation is
refused. The plugin does not indiscriminately save the entire solution.

## Change Workflow

1. Validate the allowed target object and ST language.
2. Capture the baseline state and dirty state.
3. Create a proposal and display a declaration/implementation diff.
4. Bind approval to the exact target, baseline revision, and proposed content.
5. Recheck the object state and approval immediately before applying the change.
6. Back up the baseline locally and execute changes serially.
7. Read the actual post-change state and log the outcome.
8. Execute the associated approved build workflow and report its result.

A hash or revision comparison must cover the exact relevant object state that
was previously read. An approval must not be reused for a different proposal.

## Partial Failures and Unknown Outcomes

The declaration and implementation may require separate API calls. No atomic
TwinCAT transaction is assumed. In a partial failure, completed steps, the
observed state, and possible recovery must be visible.

A timeout or disconnection may occur after a change has actually been applied.
In that case, the initial status is "outcome unknown." Only a fresh state check
may confirm success, failure, or a partial change. The Assistant does not blindly
retry such a write request.

Cancellation stops further agent steps, but does not guarantee rollback of an
already started COM call or build. This limitation is made visible in the thread.

## Builds and Diagnostics

- A build is not activation or a download.
- Project, configuration, and target selection must be traceable.
- Success is determined from an actual build completion, not from the absence
  of arbitrary messages in the error list.
- Diagnostics are associated with the current build. Old IDE messages must not
  falsely imply that a run succeeded or failed.
- If custom build hooks can execute arbitrary external actions, this must be
  checked before the first build and disclosed as a trust boundary; a "build"
  is not a universal sandbox.
- A successful compiler run is neither a behavior test nor authorization to
  operate a machine.

## Rollback

Applied edits retain before/after states in a session journal. Rollback requires
separate native approval and is allowed only if
the current state matches the agent's post-change state. Newer user changes
block restoration. Object creation has no source journal or automatic rollback.

Neither a global Git reset nor indiscriminate discarding of IDE changes is used.
Native undo integration is evaluated separately; its availability is not assumed.

## Bypass Paths and Local Communication

- General file-write and shell tools must be restricted in the engineering agent
  profile so they cannot modify protected TwinCAT artifacts or perform runtime
  actions outside the intended workflow.
- An unrestricted shell tool would make a path allowlist insufficient protection.
  Such access remains disabled in the MVP or requires a separately validated
  execution model.
- The IDE bridge exposes no arbitrary COM calls or shell commands.
- Local endpoints require appropriate access control and authentication;
  "localhost" alone is not proof of authorization.
- WebView2 origins, navigation, and message parameters are validated. External
  documentation links receive no access to the host bridge.

These rules are application-level controls, not a claim of a complete OS sandbox
against arbitrary code running with the signed-in user's permissions.

## Data and Logging

Source code may leave the local system through the selected model provider.
The provider, data flow, and operator approval must be visible.

Real projects, documentation corpora, session data, tokens, and change backups
are not checked into this repository. Diagnostic logs must account for sensitive
content and be sanitized before sharing. Retention and local storage locations
will be defined before production use.
