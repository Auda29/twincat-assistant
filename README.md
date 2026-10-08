# TwinCAT Assistant

An AI agent directly inside the TwinCAT shell or Visual Studio: understand
projects, find relevant InfoSys documentation, make controlled ST code changes,
and evaluate real build results. The user experience follows OpenChamber's
threads and agent activity views.

**Status:** Repository initialization and MVP planning. There is no runnable
application, IDE extension, or implemented engineering bridge yet. The repository
is initially private.

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

- [Documentation overview](docs/README.md)
- [MVP: scope and acceptance](docs/MVP.md)
- [Architecture and integration boundaries](docs/ARCHITECTURE.md)
- [Implementation roadmap and technical validation](docs/ROADMAP.md)
- [Engineering safety and change workflow](docs/ENGINEERING_SAFETY.md)

## Development and Licensing

Build and installation instructions will follow the first technical prototype.
The exact target IDE version and compatible upstream versions are still to be
selected. No upstream code is included in the initial commit.

At the time of research, OpenChamber and OpenCode are licensed under MIT. When
reusing their code, retain their copyright and license notices and review the
licenses of third-party components. No software license has been selected for
this project yet; repository access does not grant general permission to use or
redistribute it.

This is an independent project, not an official Beckhoff product. Beckhoff
documentation and real machine projects are not bundled.
