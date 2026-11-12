# Engineering Safety and Change Workflow

## Implemented boundary

Analysis remains read-only with default-deny tool and provider policies. The
private [Editor profile](EDITOR_PROFILE.md) connects the Engineering-owned native
provider for explicit build and supported source actions, with independent
native approval. Normal-ST writes remain blocked as documented in
[the transaction analysis](NORMAL_ST_TRANSACTIONS.md).
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

The MVP does not modify an unsaved target object. The user must explicitly save
their state, after which the agent must read it again. If reliable dirty-state
detection is unavailable, the write path remains disabled. The plugin does not
indiscriminately save the entire solution.

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

The agent's before/after states are stored locally. Rollback is allowed only if
the current state matches the agent's post-change state. Newer user changes
block automatic restoration.

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
