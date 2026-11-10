# Specializing OpenCode and OpenChamber for TwinCAT Assistant

Recorded 2026-10-29 on `feat/twincat-runtime-ui`, starting from release
`native-v0.1.9`, commit `d9d62d0`.

This document records the agreed redesign. The first two feature groups use
pinned sources and targeted TwinCAT integration on this branch; see
[Runtime integration](RUNTIME_INTEGRATION.md). Selecting modules to remove remains
a later joint step.

The next phase on `backend` starts with the [server inventory](BACKEND_SERVER_INVENTORY.md).
It records merged main revision `de4eef8`, distinguishes existing server tasks
from intended ownership by Core, Chat, Host and both MCPs, and records open
reduction decisions without changing runtime behavior.

After integrating the completed frontend design, the [shared startup contract](START_CONTRACT.md)
records entry points, lifecycles and requirements for consolidation. Themes and
commands must be prepared before the first Chat bootstrap.

The [priorities agreed on 2026-11-02](ROADMAP.md#agreed-priorities-2026-11-02)
continue this work: establish target hosts and differences in TwinCAT 4026,
the 64-bit shell and Visual Studio integration; prepare a built backend package;
then create the CI installer. Profile prompts, editor profile, German/English UI
and inline completion follow. Prompt review can start independently earlier.
Complete backend cleanup and acceptance of every Host variant are not prerequisites
for an initial installer for the verified Host. The roadmap defines boundaries;
existing conversations and retained core functionality must survive.

## Agreed direction

Reuse relevant OpenCode/OpenChamber functionality with its existing implementation
and behavior, then adapt the infrastructure and Chat UI for TwinCAT embedding.
Changes should follow project-context, IDE-lifecycle and interaction requirements.

Existing agent logic, model communication and Chat rendering form the foundation.
Our code connects these to TwinCAT and defines reliable project, conversation and
attachment states. The result should behave as one TwinCAT Assistant. Actual
source dependencies determine which generic features can be removed.

## Starting point

The pinned npm distributions are OpenCode `2.0.23` and OpenChamber `2.1.1`.
The native C# Host supplies the tool window, WebView2 and local IDE context.
Engineering MCP reads saved project files; Knowledge MCP reads local documentation.

A version-bound adapter integrates the embedded UI with internal OpenChamber
stores and verifies distributed JavaScript hashes. Internal export mappings are
documented in the [integration contract](CHAT_INTEGRATION.md).

The redesign moves both projects to traceable source builds. The current
installation and existing data remain the comparison baseline for staged migration.

## Functionality to retain

### OpenCode foundation

- Existing providers, sign-in methods and model management.
- Message processing, response streaming and cancellation.
- Agent execution, tool selection/calls and results used in subsequent model steps.
- Error handling, retries and long-context management present in the selected revision.
- MCP support with appropriate protocol profiles.
- Conversation persistence/history and required data/database mechanisms.
- Existing permission mechanisms underlying reviewed TwinCAT tool approvals.

Migration must not silently reduce sign-in or model capabilities. Retained
behavior must be checked practically for the providers currently used.

### OpenChamber UI

- Existing Chat rendering/composer behavior accepted by the user.
- Streamed responses, cancellation and understandable errors.
- Conversation history and switching.
- Provider/model selection and sign-in.
- Adding, removing and submitting attachments/references.
- Markdown, code blocks, copy actions, tool displays and source citations.
- Appearance/settings required by retained features; review other features individually.

Existing Chat components remain the functional foundation. Embedding, navigation
and native-context integration are adapted around them.

## TwinCAT-specific changes

### Durable project/conversation binding

A conversation belongs to one unambiguously identified PLC project. Durable
project identity is separate from transient IDE instances and runtime configuration.
An IDE restart or model change alone must not force a new project conversation.

Opening a project loads its complete history and restores the appropriate chat.
Pending loading is not an empty history. New chats retain an explicit action.
Verify same-named projects, multiple PLCs in one solution and multiple IDE instances
separately. Establish moved/renamed-project rules before migrating data.

Navigation agreed on 2026-10-30 permits multiple chats per PLC project. The clock
opens a compact project-only list of titles and activity; Plus starts another
project chat. Reopening restores the last selection. Changing POU/member retains
the current conversation. General chats remain separate through the menu. Project
tools and Knowledge share connections; existing chats remain.

On 2026-11-02 the next presentation was agreed: the clock toggles a left chat bar,
docked beside wide conversations and overlaid on narrow ones, closing after
selection. It shows project name, new chat, search, selection and active responses,
remembering open state per project. “Mark done”, “Done chats” and “Reopen” replace
visible archive terminology while preserving storage/history.

This navigation was implemented on `feat/project-chat-navigation` on 2026-11-02.
The compact selector replaces the general sidebar and adds title search, rename,
archive and restore. Last selection survives runtime restart. See
[rules and acceptance boundaries](RUNTIME_INTEGRATION.md#agreed-project-chat-navigation-2026-10-30).
Local revision `dfb50b1` was installed that day. The user manually accepted project
navigation, separate drafts/attachments, restoration after IDE restart and the
Properties/Toolbox overlap fix. A combined theme-test timeout remains separately
open. Older chats retain their former fixed project titles; new chats allow
original automatic title generation.

### Explicit context and attachment states

Distinguish the locally active POU, project metadata, staged snapshot and content
actually transmitted with a message. The UI must reflect the actual state.

Agree which metadata makes “this POU” unambiguous. Following the editor locally
does not authorize source transmission. Saved files, Automation Interface snapshots
and any future verified editor buffer remain identifiable source types.

### Stable UI/runtime/IDE interface

Add a small, explicitly supported source interface for project identity, context,
attachment state and history. This gradually replaces coupling to minified store
exports. Consider OpenChamber and OpenCode together: route, event and model changes
must match their consumers. Reuse native IDE integration and both existing MCPs.

### Deliberate tool/process composition

Knowledge remains shared within the local runtime. Engineering stays scoped to
the exact PLC project. Search hits, IDs, articles and sources must reach the model
intact through tool results.

Preserve supervision and cleanup of owned children after closure/crash. Measure
initial Knowledge startup separately: full corpus verification belongs to Knowledge
MCP, and forking alone does not remove its cost. Long connection time despite a
prepared index remains a [separate investigation with video evidence and acceptance criteria](KNOWLEDGE_SETUP.md#open-investigation-slow-knowledge-connection),
independent of UI cleanup.

## Reduction candidates

Candidates for joint inventory are general terminal/shell functionality;
Git/worktree/repository management; generic file editing outside TwinCAT contracts;
unneeded UI, background services and connection types; and extensions or other
features whose product value is unconfirmed.

Inspect dependencies before removing a feature. Hiding a menu item is insufficient
if an unwanted backend route or tool remains reachable. Retain needed shared
libraries and data structures.

## Sources, data and maintenance

Record package versions, source revisions and builds for both projects. First
establish a working unchanged source reference, then keep changes in small,
traceable commits. Decide source management after this check: fork repositories,
submodules and vendoring are not yet chosen. Account for licenses, notices and
existing redistribution boundaries.

Conversations currently reside at
`%LOCALAPPDATA%\TwinCatAssistant\operator-state\data\opencode\opencode.db`;
native project bindings at `%LOCALAPPDATA%\TwinCatAssistant\native-chat\threads\`.
Before changing storage/binding formats, verify adoption with a separate data copy.
Conversations and sign-ins must survive. Credentials, project source, local
databases and documentation corpora must not enter source repositories.

## Work sequence

1. Inventory OpenCode functionality and actual dependencies at the selected
   revision; agree what to retain, adapt or remove.
2. Inventory OpenChamber components, settings, interfaces and services likewise.
3. Pin source/tool revisions, build reproducibly and verify a functional reference.
4. Integrate TwinCAT project/context/attachment interfaces in source, reuse
   existing behavior and fix known restoration/attachment defects.
5. Remove agreed feature groups individually, clean dependencies and verify
   affected behavior after each step.
6. Verify data adoption and installation on an isolated candidate before changing
   the local installation.

Record outcomes/open decisions and review them together after each section. The
next concrete step from this document is the OpenCode inventory.

## Acceptance

- Existing provider sign-in, model selection and ordinary Chat work.
- Streaming, cancellation, tool errors and subsequent model steps remain usable.
- Both MCPs return actual results; Knowledge hits open with usable sources.
- The same project chat returns after a complete IDE restart.
- Project changes, same-named POUs and multiple IDEs do not misassign messages/attachments.
- Staged, removed and submitted attachment displays match actual content across context changes.
- Existing data can be adopted; a verified return path precedes local migration.
- Relevant regressions and Linux/Windows CI pass; startup and memory measurements
  are recorded separately.

The first redesign retains read-only scope. Future PLC writes, compiler and runtime
features follow [MVP](MVP.md), [Roadmap](ROADMAP.md) and
[Engineering Safety](ENGINEERING_SAFETY.md). A passing build or Chat test does
not establish PLC or machine behavior.
