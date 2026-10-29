# Architecture and Integration Boundaries

## Core Decision

The planned next step on `feat/twincat-runtime-ui` specializes both upstreams
through source builds, retaining the relevant existing implementations and
adapting their IDE integration. The agreed direction, proposed reductions and
stepwise decisions are in the [TwinCAT runtime/UI plan](TWINCAT_RUNTIME_UI_PLAN.md).
This is a planning document; the distribution-based implementation below remains
the current 0.1.9 baseline.

OpenChamber and OpenCode are reused together as the foundation. Initially, neither
a new chat interface nor a new agent harness will be built. TwinCAT-specific
development focuses on the IDE host, the engineering bridge, and extensions to
the existing monorepo MCP.

The local foundation reuses lockfile-pinned npm distributions: OpenChamber
`2.1.1` and OpenCode `2.0.23`. MCP source commits and setup are recorded in
[installation](INSTALLATION.md). No upstream fork, source vendor tree or
submodule is committed. The architecture diagram below is the target native
product, not the current executable topology. Current code includes the external
operator CLI, read-only context extension, portable diagnostic bridge and a
separate native WPF/WebView2 prototype. Its reader displays ST locally but is not
connected to the diagnostic pipe, MCPs or sessions. See [Status](STATUS.md).

## Components

```text
TwinCAT shell / Visual Studio
|
+-- C# plugin
|   +-- WPF tool window with WebView2
|       +-- OpenChamber web interface
|
+-- IDE bridge
    +-- Solution/project binding and object context
    +-- Controlled reads and writes of PLC objects
    +-- Build execution and diagnostic collection
                 ^
                 | Local, authenticated connection
                 v
OpenChamber + OpenCode outside the IDE
+-- Sessions, model access, streaming, and agent execution
+-- twincat-knowledge-mcp
+-- twincat-mcp-mono
    +-- Saved project files and ADS diagnostics
    +-- New live engineering backend -> IDE bridge
```

## Responsibilities

### IDE Plugin and Bridge

- Provide the tool window and manage WebView2 and the IDE lifecycle.
- Unambiguously identify the specific IDE instance, solution, and target project.
- Call DTE and the TwinCAT Automation Interface only in an appropriate IDE/COM
  threading context; model calls must not block the UI thread.
- Validate the target object, dirty state, baseline revision, and approval.
- Read and make controlled changes to declarations and ST implementations.
- Capture build completion and associated diagnostics.
- Report state changes and unknown outcomes to the backend.

### OpenChamber and OpenCode

- OpenChamber provides the familiar thread, activity, and review interface.
- OpenCode provides agent execution, model access, and MCP client capabilities.
- The agent decides which read tools are needed to answer a question.
- Write and build actions must respect the engineering approval workflow.
- General-purpose tools must not create uncontrolled bypass paths.

### Existing MCPs

`twincat-knowledge-mcp` remains a read-only documentation interface. The operator
provides an authorized, validated local corpus; without it, documentation
knowledge remains unavailable.

`twincat-mcp-mono` retains its core/adapter separation. A new backend connects to
the running IDE. Existing read and build contracts should be preserved wherever
they fit the domain. Engineering writes require additional, explicit contracts;
they are not a reinterpretation of ADS variable writes.

## Current Capabilities and Limitations

Implementation baseline and exact pins are recorded in [status](STATUS.md) and
[installation](INSTALLATION.md). Portable tests do not close native IDE gates.

| Component | Verified Capabilities | Limitation |
| --- | --- | --- |
| Knowledge MCP | Four tools, local packages/snapshots, search, and topic/hierarchy retrieval | No bundled corpus; no engineering or runtime control |
| Monorepo MCP | Saved project analysis and ADS diagnostics | No implemented live engineering write backend |
| Monorepo build tools | Registered tool contracts | The file backend reports build/compiler diagnostics as unavailable |
| OpenChamber | Turn-based threads and extension SDK | No ready-made TwinCAT host; tightly coupled to OpenCode |
| Automation Interface | Documented PLC read/write interfaces | Unsaved editor behavior, undo, and specific versions require practical validation |

## Interface Strategy

1. First, display the local OpenChamber web interface in WebView2.
2. Add project context, navigation, and tool presentations.
3. Use the OpenChamber extension SDK wherever it is sufficient.
4. Build a more customized frontend host only when a demonstrated need arises.

### Active-editor-first interaction

OpenChamber chat remains the primary surface. The native layer should follow the
selected IDE's active ST POU locally and display compact persistent binding,
freshness and dirty/unknown indicators. Refresh/Read buttons and a manually loaded
POU selector are prototype diagnostics, not prerequisites for normal interaction.

Resolve the active object independently of a complete catalog. Subscribe to
editor/solution lifecycle events, coalesce redundant updates, preserve supported
COM threading and reject results from an obsolete binding. Unsupported/ambiguous
editors and closed solutions clear old context; names or tab titles alone cannot
establish identity. Unsaved-editor semantics require acceptance before claiming
that an Automation Interface snapshot is the exact editor buffer.

Automatic local following must not disclose/persist source or silently retarget
a thread or future approval. Native-to-session/MCP transport remains a separate,
authenticated integration under explicit disclosure consent. Optional project
search/browsing must state completeness, but the 200-target prototype inventory
must not gate access to the current editor. No new chat harness or duplicate
TwinCAT MCP/tool catalog is introduced by this UX change.

The 0.1.4 candidate now adds a native project/session binding and an explicitly
staged POU composer reference. Its fixed-version adapter is required because the
SDK cannot reshape the whole embedded surface. It reuses the existing app stores,
verifies distributed asset hashes, and does not patch installed upstream files.
See [the integration contract](CHAT_INTEGRATION.md) for transport, consent,
storage and acceptance boundaries. This does not connect the native reader to
the MCP engineering backend.

The VS Code extension is an architectural reference, not an extension that can
be installed in Visual Studio. According to the research, the OpenChamber
extension SDK supports web and desktop; its use in the embedded web interface
requires practical validation.

MCP-based object changes are not automatically ordinary OpenCode file patches.
POU diffs, change logs, and rollback require explicit integration. The initial
presentation may use a dedicated review panel; deep integration into every
upstream diff view is not an MVP requirement.

## Communication Boundaries

- Agent/MCP processes remain outside the IDE process.
- The implemented diagnostic bridge uses authenticated, current-user named
  pipes with bounded NDJSON frames; see [protocol version 1](BRIDGE.md). It
  exposes only read-only unavailable diagnostics, not native writes/builds.
- Endpoints are restricted to the intended user. The connection also requires
  verified identity or session authentication.
- WebView2 accepts only the intended local interface. Origins, navigation, and
  messages are validated; web content receives no generic COM or shell interface.
- The integration must explicitly handle IDE shutdown, solution changes,
  multiple IDE instances, and crashed helper processes.
- The solution, PLC project, and optional ADS target have separate identities.
  Matching names do not prove that source code and runtime correspond.

## MCP Compatibility

According to its current adapter, the knowledge MCP uses the `2026-07-28` profile
and returns documentation content in `structuredContent`, while the text block
contains a short status summary.

The current OpenCode V2 documentation describes `legacy` as the default and
supports `auto` or `2026-07-28` as server-specific selections. The knowledge
server therefore needs a deliberately compatible protocol setting. That
selection must not be applied to the monorepo server without validation.

Connection and tool discovery alone are insufficient: a real search/topic call
must deliver documentation content into the model context. OpenChamber's MCP
configuration interface must preserve the required setting rather than losing
it when configurations are changed.

## Open Decisions

- Exact TwinCAT/TcXaeShell64 version and installed components.
- Suitable classic Visual Studio SDK, .NET, and WebView2 versions.
- An upstream upgrade strategy beyond the currently tested distribution pins.
- Native host integration of the implemented diagnostic bridge and portable
  approval contracts; no operator approval/write endpoint is currently exposed.
- Unambiguous object identity and reliable dirty-state/revision checks.
- Build, diagnostic, and persistence behavior in the selected shell.
- A demonstrable rollback workflow without assuming universal IDE undo support.
- This project's software license and future distribution.

## Sources

- [OpenChamber](https://github.com/openchamber/openchamber)
- [OpenChamber extension SDK](https://github.com/openchamber/openchamber/blob/main/packages/sdk/README.md)
- [OpenChamber turn projection, reviewed revision](https://github.com/openchamber/openchamber/blob/866dffd863c98a25aa6577e90cca46b01a66237e/packages/ui/src/components/chat/lib/turns/projectTurnRecords.ts)
- [Monorepo EngineeringService, reviewed revision](https://github.com/Auda29/twincat-mcp-mono/blob/3c303837e6d6f72bec1b9d38e778a7c32c61ae5c/packages/core/src/engineering.ts)
- [Monorepo engineering backend documentation](https://github.com/Auda29/twincat-mcp-mono/blob/main/docs/engineering-backends.md)
- [Knowledge MCP adapter contract](https://github.com/Auda29/twincat-knowledge-mcp/blob/main/docs/implementation/topic-mcp-adapter.md)
- [OpenCode V2 MCP and protocol selection](https://opencode.ai/v2/docs/mcp-servers)
- [Beckhoff: Automation Interface API](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21905744267.html)
- [Beckhoff: connecting to a running IDE through DTE](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842572555.html)
- [Microsoft: tool window extension](https://learn.microsoft.com/en-us/visualstudio/extensibility/creating-an-extension-with-a-tool-window?view=vs-2022)
- [Microsoft: WebView2 communication](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/communicate-btwn-web-native)
- [Microsoft: WebView2 security](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
