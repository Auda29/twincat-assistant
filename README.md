# TwinCAT Assistant

AI assistance for TwinCAT projects, inside your IDE.

Explain Structured Text, explore PLC project code and find relevant Beckhoff
documentation in a docked chat window. TwinCAT Assistant connects the active
project and POU to a conversation that you can return to later.

**0.2.0 Preview · Read-only**

[Get started](#getting-started) · [Documentation](docs/README.md) · [Releases](https://github.com/Auda29/twincat-assistant/releases)

## What you can do

- Understand function blocks, declarations and ST implementations with an
  attached POU snapshot.
- Explore saved PLC projects, search code, inspect actions and methods, and
  look up library references through the Engineering MCP.
- Search locally installed Beckhoff InfoSys documentation and open the
  matching articles with sources through the Knowledge MCP.
- Keep conversations associated with their PLC project, switch between
  project chats and use a general chat for questions without a project.

The chat includes model selection, streamed responses, tool activity, code
blocks and attachments. Model requests go to the provider you configure.

## Getting started

The alpha is currently installed from source. You need Windows with TwinCAT
XAE and access to a supported model provider. Documentation search uses the
Knowledge MCP with a prepared corpus, which can be created from locally
installed Beckhoff InfoSys help.

1. Follow the [installation guide](docs/INSTALLATION.md) to build the native
   host and configure the local runtime and MCPs. The
   [source runtime guide](upstream/README.md) covers the pinned OpenCode and
   OpenChamber builds.
2. Open the TwinCAT Assistant tool window, connect your model provider and
   select a model in the chat.
3. Open a PLC project and select a Structured Text POU. Attach its snapshot
   when you want the assistant to examine that code, then ask your question.
4. For documentation questions, use **Wissenssuche** to discover and prepare
   your installed InfoSys help. See [documentation setup](docs/KNOWLEDGE_SETUP.md).

The native host has separate x86 and amd64 builds. Live IDE testing currently
focuses on x86; the [verification notes](docs/WINDOWS_ACCEPTANCE.md) track the
compatibility and acceptance boundaries.

## Alpha scope

The current alpha reads and explains code and documentation. PLC code editing,
compilation, deployment and runtime control are outside the available scope.
A connected PLC is not required for project and documentation analysis.

Engineering tools read saved project files. An attached POU is a separate
snapshot and does not guarantee complete access to unsaved editor changes or
all child actions and methods. The
[context and attachment guide](docs/RUNTIME_INTEGRATION.md) explains these
source boundaries.

## Built on

TwinCAT Assistant reuses existing chat and agent implementations and connects
two dedicated local MCP services.

| Component | Role | Foundation |
| --- | --- | --- |
| Assistant Host | Native tool window, IDE context and WebView2 integration | C# / WPF |
| Assistant Chat | Messages, input, attachments and conversation display | [OpenChamber](https://github.com/openchamber/openchamber) |
| Assistant Core | Model providers, agent execution, MCP client and conversation storage | [OpenCode](https://github.com/anomalyco/opencode) |
| Engineering MCP | Saved PLC project and source analysis | [twincat-mcp-mono](https://github.com/Auda29/twincat-mcp-mono) |
| Knowledge MCP | Beckhoff documentation search and article retrieval | [twincat-knowledge-mcp](https://github.com/Auda29/twincat-knowledge-mcp) |

Model execution and MCP processes run outside the IDE. Documentation, generated
indexes and user project files are not bundled with the application.

## Documentation

- [0.2.0 Preview release notes](docs/releases/native-v0.2.0-preview.1.md)
- [Installation and configuration](docs/INSTALLATION.md)
- [Using project chats and attachments](docs/CHAT_INTEGRATION.md)
- [Setting up Beckhoff documentation search](docs/KNOWLEDGE_SETUP.md)
- [Architecture and integration boundaries](docs/ARCHITECTURE.md)
- [Detailed implementation status](docs/STATUS.md) and [roadmap](docs/ROADMAP.md)

See the [documentation index](docs/README.md) for development and verification
references.

## Development and licensing

Build prerequisites and test commands are documented in the
[installation guide](docs/INSTALLATION.md). Reproducible upstream builds use
[source-lock.json](upstream/source-lock.json); local runtime data and generated
source checkouts stay outside tracked files.

Reused components retain their upstream licenses and copyright notices.
This repository does not yet specify a project license.

TwinCAT Assistant is an independent project and is not an official Beckhoff
product.
