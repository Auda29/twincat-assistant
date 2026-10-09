# TwinCAT Assistant

An AI agent directly inside the TwinCAT shell or Visual Studio: understand
projects, find relevant InfoSys documentation, make controlled ST code changes,
and evaluate real build results. The user experience follows OpenChamber's
threads and agent activity views.

**Status:** `0.1.0-dev.1` — runnable local integration foundation, not the native
TwinCAT MVP. This checkout contains an isolated operator CLI, a read-only
OpenChamber context panel, a portable C# engineering safety core, and an
authenticated diagnostic named-pipe bridge. No TwinCAT IDE plugin or native
engineering adapter is shipped; live writes and compiler builds are unavailable.
See [implementation status](docs/STATUS.md) for the exact verification boundary.

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

A persistent chat in one explicitly selected TwinCAT shell version that examines
the correct project, uses both MCPs, and can modify the declaration and ST
implementation of an existing block after approval, then compile it.

No downloads, online changes, forcing, or runtime writes. A connected PLC is not
required for the core workflow.

## Documentation

- [Implementation status and verification](docs/STATUS.md)
- [Installation and local commands](docs/INSTALLATION.md)
- [OpenChamber extension](docs/EXTENSION.md)
- [Diagnostic bridge](docs/BRIDGE.md)
- [Windows acceptance checklist](docs/WINDOWS_ACCEPTANCE.md)
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
The exact native TwinCAT/TcXaeShell64 target is still to be selected.

At the time of research, OpenChamber and OpenCode are licensed under MIT. When
reusing their code, retain their copyright and license notices and review the
licenses of third-party components. No software license has been selected for
this project yet; repository access does not grant general permission to use or
redistribute it.

This is an independent project, not an official Beckhoff product. Beckhoff
documentation and real machine projects are not bundled.
