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
and source observation type as automatic metadata. The existing composer
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

The Knowledge and engineering MCPs retain their existing implementation. The
shared Knowledge broker remains in use. Its cold corpus validation is a separate
cost, unaffected by building OpenCode from source.

Metadata is captured before asynchronous attachment resolution. Queued inputs
retain the object they referred to when queued; delivery does not replace it
with the editor's later active object.

## Validation scope

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
