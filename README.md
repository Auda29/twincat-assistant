# TwinCAT Assistant

An AI agent directly inside the TwinCAT shell or Visual Studio: understand
projects, find relevant InfoSys documentation, make controlled ST code changes,
and evaluate real build results. The user experience follows OpenChamber's
threads and agent activity views.

**Status:** `0.1.0-dev.1` — local integration foundation plus a read-only native
prototype, not the accepted TwinCAT MVP. Both IDE payloads build. The earlier x86
`0.1.1` passed user-confirmed menu/window/docking smoke checks and local ST display.
Native `0.1.2` added automatic local active-POU following and
a compact context display with optional diagnostics. Full host lifecycle,
broader unsaved-buffer semantics, extended chat lifecycle and amd64 acceptance remain open. Source is labelled
as an Automation Interface snapshot, not verified unsaved editor text.
The x86 core flow is now checked: same-name POUs in two PLCs follow correctly,
and a real unsaved editor comment produces the visible unsaved-changes status.
Native `0.1.3` adds protected local chat startup, first-run password
setup and actual navigation/service error reporting. See [native chat](docs/NATIVE_CHAT.md).
Candidate `0.1.4` adds a compact project-bound chat, explicit POU composer
attachments, a general chat without POU context, provider consent and bounded
reconnection. It reuses the existing OpenChamber chat through a hash-verified
version-specific adapter. See [chat integration](docs/CHAT_INTEGRATION.md) for
the candidate's tests and installed acceptance boundary.
Released `0.1.6` replaces the upper UI
with the selected compact context bar, follows OpenChamber's palette and opens
project conversations automatically. Connected providers are enabled without
a separate consent click. Automatic POU attachment is an optional setting in
the overflow menu; submitting the message still controls provider transfer.
The x86 candidate is installed with all 13 payload hashes verified and 0.1.5
preserved outside extension discovery. The IDE was closed for installation;
broader UI and lifecycle acceptance remain open.
Local x86 fix `0.1.7` binds editor tracking when the IDE restores a docked
Assistant window without invoking its menu command. It was installed with all
13 payload hashes verified and 0.1.6 preserved outside extension discovery.
Automatic restoration after restarting the installed IDE still needs acceptance.
Preview release `0.1.8` connects both existing MCPs to project chats, using
separate private workspaces for each saved PLC project. Project identity remains
available with a DUT or unsupported source editor; general chats have no
engineering server. No documentation corpus is configured yet. See
[native MCP setup and verification](docs/NATIVE_MCP.md) and
[release notes](docs/releases/native-v0.1.8.md). The x86 0.1.8 payload is installed
with all 13 hashes verified and 0.1.7 backed up outside extension discovery.
Both MCP paths are configured; installed startup acceptance remains open.
Earlier x86 `0.1.5` corrected provider setup: explicitly allow an authenticated
provider under Details first, then choose a model in the existing chat picker.
This avoids a default-deny catalog hiding a successfully signed-in provider.
The scoped x86 chat goal is accepted in `ContextChecks`: project refusal/return,
explicit POU reference, provider/model setup and the correct actual model reply
are user-confirmed. Wider native MVP and MCP gates remain open.
The isolated operator CLI,
OpenChamber context panel, portable safety core and diagnostic pipe remain;
live writes and compiler builds are unavailable.
See [implementation status](docs/STATUS.md) for the exact verification boundary.
The [roadmap](docs/ROADMAP.md#where-we-are-now) records current position, next
tasks and separate read-only, agent-context, write and compiler milestones.

## Approach

- Reuse OpenChamber and OpenCode rather than rebuilding the chat interface and
  agent infrastructure. Start with as few changes to the existing system as possible.
- A C# plugin provides a docked tool window using WebView2.
- Model calls and MCP processes run outside the IDE.
- A small IDE bridge connects the agent system to the open solution, PLC objects,
  and build execution.
- The two existing TwinCAT MCP projects remain the domain-specific foundation.

## Existing Projects

| Project | Role |
| --- | --- |
| [twincat-knowledge-mcp](https://github.com/Auda29/twincat-knowledge-mcp) | Local, source-backed search and retrieval of TwinCAT/Beckhoff documentation |
| [twincat-mcp-mono](https://github.com/Auda29/twincat-mcp-mono) | Project analysis and ADS diagnostics; planned extension with a live engineering backend |
| [OpenChamber](https://github.com/openchamber/openchamber) | Chat interface, threads, activities, and change review |
| [OpenCode](https://github.com/anomalyco/opencode) | Agent execution, model integration, and MCP client |

## Initial MVP

A persistent chat in explicitly tested 32-bit and 64-bit TwinCAT shell versions that examines
the correct project, uses both MCPs, and can modify the declaration and ST
implementation of an existing block after approval, then compile it.

Normal use follows the active ST editor locally without manual refresh/read/block
selection. Source disclosure requires consent. This is the target product;
the current milestone remains strictly read-only and exposes no compiler action.

No downloads, online changes, forcing, or runtime writes. A connected PLC is not
required for the core workflow.

## Documentation

- [Implementation status and verification](docs/STATUS.md)
- [Installation and local commands](docs/INSTALLATION.md)
- [OpenChamber extension](docs/EXTENSION.md)
- [Diagnostic bridge](docs/BRIDGE.md)
- [Windows acceptance checklist](docs/WINDOWS_ACCEPTANCE.md)
- [32-bit and 64-bit native prototype](docs/NATIVE_PROTOTYPE.md)
- [Native Knowledge and TwinCAT MCP setup](docs/NATIVE_MCP.md)
- [Documentation overview](docs/README.md)
- [MVP: scope and acceptance](docs/MVP.md)
- [Architecture and integration boundaries](docs/ARCHITECTURE.md)
- [Implementation roadmap and technical validation](docs/ROADMAP.md)
- [Engineering safety and change workflow](docs/ENGINEERING_SAFETY.md)

## Development and Licensing

Local development: `npm ci`, `npm run check`; see [installation](docs/INSTALLATION.md)
for pinned upstream setup and the separate .NET and integration checks.
OpenChamber `2.1.1`, OpenCode `2.0.23`, and the extension SDK `2.1.1` are
lockfile-pinned. MCP revisions are recorded in the installation guide.
Upstream source checkouts and all operator data remain outside tracked files.
TcXaeShell 15/x86 and TcXaeShell64/amd64 are separate acceptance targets; the
installed x86 XAE build is 3.1.4024.78. No live amd64 host acceptance is claimed.

At the time of research, OpenChamber and OpenCode are licensed under MIT. When
reusing their code, retain their copyright and license notices and review the
licenses of third-party components. No software license has been selected for
this project yet; repository access does not grant general permission to use or
redistribute it.

This is an independent project, not an official Beckhoff product. Beckhoff
documentation and real machine projects are not bundled.
