# TwinCAT Assistant

[Website (DE)](https://auda29.github.io/twincat-assistant/) · [Website (EN)](https://auda29.github.io/twincat-assistant/en.html)

AI assistance for TwinCAT projects, inside your IDE.

Explain Structured Text, explore PLC project code and find relevant Beckhoff
documentation in a docked chat window. TwinCAT Assistant connects the active
project and POU to a conversation that you can return to later.

**0.2.0 Beta 10 · Analysis, Editor and Debug**

[Get started](#getting-started) · [Documentation](docs/README.md) · [Releases](https://github.com/Auda29/twincat-assistant/releases)

## What you can do

- Understand function blocks, declarations and ST implementations with an
  attached POU snapshot.
- Explore saved PLC projects, search code, inspect actions and methods, and
  look up library references through the Engineering MCP.
- Search locally installed Beckhoff InfoSys documentation and open the
  matching articles with sources through the Knowledge MCP. Look up documented
  symbols, libraries and error codes with explicit ambiguity and missing fields.
- Review and approve Structured Text edits and new PLC objects in **Editor**.
- Use **Debug** to sample live PLC symbols read-only from an existing TwinCAT login (connects automatically to the configured PLC target by default; configurable in the `...` menu).
- Approve a selective PLC build from Editor or Debug and inspect its diagnostics.
- Keep conversations associated with their PLC project, switch between
  project chats and use a general chat for questions without a project.

Host and chat support German and English with a shared, saved language choice.

The chat includes model selection, streamed responses, tool activity, code
blocks and attachments. Model requests go to the provider you configure.

## Getting started

Download the [latest Windows installer](https://github.com/Auda29/twincat-assistant/releases/latest)
for TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows x64. You also need
.NET 8 runtime, WebView2 and access to a supported model provider. Documentation search uses the
Knowledge MCP with a prepared corpus, which can be created from locally
installed Beckhoff InfoSys help.

1. Close TcXaeShell, verify the downloaded setup checksum and double-click the
   EXE. Confirm UAC with your normal Windows account. The setup window handles
   installation and managed updates without Git, npm or compilation. See the
   [release notes](docs/releases/native-v0.2.0-beta.11.md), and the separate
   [migration notes for old unpackaged prototypes](docs/PROTOTYPE_PROFILE_MIGRATION_20261109.md).
2. Open the TwinCAT Assistant tool window, connect your model provider and
   select a model in the chat.
3. Open a PLC project and select a Structured Text POU. Attach its snapshot
   when you want the assistant to examine that code, then ask your question.
4. For documentation questions, use **Knowledge search** to discover and prepare
   your installed InfoSys help. See [documentation setup](docs/KNOWLEDGE_SETUP.md).
5. In a bound project chat, choose **Editor** or **Debug** in the composer.
   Follow the native consent dialog and confirm each requested build, edit or creation.
   See the [profile guide](docs/EDITOR_PROFILE.md).

The native host has separate x86 and amd64 builds. Live IDE testing currently
focuses on x86; the [verification notes](docs/WINDOWS_ACCEPTANCE.md) track the
compatibility and acceptance boundaries.

For development builds, use the [development setup](docs/INSTALLATION.md) and
[pinned source runtime guide](upstream/README.md).

## Beta scope

Analysis reads project code and documentation. Editor discovers Structured Text
across the bound PLC project. Each edit or new object requires separate native
approval. Automation Interface edits may persist directly to disk. Edits to a
supported open XML buffer remain unsaved. Creating an object writes its file
or updates its owning POU file immediately, with project changes pending save.
Debug reads scalar symbols from the already logged-in PLC. Editor and Debug can
each request a separately approved selective PLC build.

PLC activation, download, Online Change and runtime control are outside the
profile permissions. Installed-host workflow acceptance and extraction
quality and performance on a real documentation corpus are still open.

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
| Engineering MCP 0.5.10 | Saved project analysis, bound builds, Editor and Debug | [Released runtime and sources](https://github.com/Auda29/twincat-mcp-mono/releases/tag/engineering-v0.5.10) |
| Knowledge MCP 0.1.2 | Nine documentation tools, including symbol/library/error lookup | [Released package](https://github.com/Auda29/twincat-knowledge-mcp/releases/tag/v0.1.2) |

Model execution and MCP processes run outside the IDE. Documentation, generated
indexes and user project files are not bundled with the application.

## Documentation

- [0.2.0 Beta 11 — Structured Text edits and object creation](docs/releases/native-v0.2.0-beta.11.md)
- [0.2.0 Beta 10 — automatic read-only Debug connection and Engineering 0.5.8](docs/releases/native-v0.2.0-beta.10.md)
- [0.2.0 Beta 9 — Engineering 0.5.7 PLC association and COM invocation fixes](docs/releases/native-v0.2.0-beta.9.md)
- [0.2.0 Beta 8 — unified composer profile menu and Debug diagnostics](docs/releases/native-v0.2.0-beta.8.md)
- [0.2.0 Beta 7 — Analysis, Editor and Debug](docs/releases/native-v0.2.0-beta.7.md)
- [0.2.0 Beta 6: chat restart recovery and background build readiness](docs/releases/native-v0.2.0-beta.6.md)

- [0.2.0 Beta 5: build tools, typed Knowledge and pinned-history fix](docs/releases/native-v0.2.0-beta.5.md)
- [Profiles and native approvals](docs/EDITOR_PROFILE.md)
- [0.2.0 Beta 4: startup, context and Knowledge improvements](docs/releases/native-v0.2.0-beta.4.md)
- [0.2.0 Beta 3: startup performance and shared product icon](docs/releases/native-v0.2.0-beta.3.md)
- [0.2.0 Beta 2: integrated fixes and version-aware setup](docs/releases/native-v0.2.0-beta.2.md)
- [Beta 1 Setup 1: extraction fix and version overview](docs/releases/native-v0.2.0-beta.1-setup.1.md)
- [0.2.0 Beta 1 release notes](docs/releases/native-v0.2.0-beta.1.md)
- [German/English UI and saved language selection](docs/LANGUAGE_SUPPORT.md)
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
