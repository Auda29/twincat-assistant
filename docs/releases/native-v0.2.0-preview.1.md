# TwinCAT Assistant 0.2.0 Preview 1

Tag: `native-v0.2.0-preview.1`.

This source preview brings the project chat, native IDE integration and the
existing source-built agent runtime together. It remains read-only. The release
number describes the Assistant preview; the pinned upstream component versions
and the development operator package version `0.1.0-dev.1` remain unchanged.

## Included

- A native collapsible project chat sidebar with search, new conversations,
  rename and explicit title generation. Wide tool windows show the sidebar
  beside the conversation; narrow windows use a dismissible overlay.
- **Mark done**, **Done chats** and **Reopen** retain
  conversations through the existing archive/restore actions. Selected chats
  and the wide sidebar preference are remembered per PLC project and profile.
  General conversations remain separate.
- Per-chat drafts and attachments, automatic POU/PRG attachment recovery for
  existing chats, stable attachment button sizing and validation of generated
  titles. Context identifies POUs, actions, methods and properties when the
  native reader can resolve them.
- Whole-conversation copying and Markdown export, TwinCAT light/dark themes,
  and WebView2 composition for IDE Properties/Toolbox overlays.
- The existing providers, logins, models, streaming, cancellation, agent/tool
  loop and conversation database from the pinned Core/Chat source builds.
  Engineering MCP reads saved project files; Knowledge MCP searches and opens
  the locally prepared Beckhoff documentation corpus.

## Verification and scope

The implementation merged through [PR #13](https://github.com/Auda29/twincat-assistant/pull/13)
with successful Linux, Windows lifecycle and source-runtime CI jobs. Local checks
passed 97 Node cases, 8 browser cases, both native architecture builds, 126 native
policy/context/chat cases, 210 WPF host checks and 94 sidebar checks. The combined
source WebView2 fixture and the installed-payload fixture each passed 160 checks.
All 13 installed payload hashes were verified, and the 49 monitored operator,
configuration and chat files were unchanged during isolated verification.

These fixtures use synthetic projects, help, history and model responses. The
operator separately accepted the earlier chat export, navigation, attachment,
title and docking fixes and inspected the installed sidebar. The fixtures are
not evidence of PLC compilation, machine behavior or complete live IDE coverage.

An attached Automation Interface snapshot does not guarantee access to the
current unsaved editor buffer. Successful Amd64 builds do not establish live
64-bit shell compatibility. TwinCAT 4026 and TwinCAT inside Visual Studio need
their own host/version acceptance.

Knowledge remains pinned to released 0.1.1. The long initial corpus connection
is still [under investigation](../KNOWLEDGE_STARTUP_INVESTIGATION.md); its separate
performance candidate is not part of this release.

## Distribution and next steps

This is a **source-only prerelease**. There is no end-user setup executable or
bundled VSIX asset. Use the [development installation guide](../INSTALLATION.md)
and [source runtime guide](../../upstream/README.md). Documentation corpora,
credentials, customer projects and user databases are not distributed.
The existing [dependency and redistribution review](../SECURITY_AUDIT.md) remains
applicable. This preview does not claim production approval.

The [roadmap](../ROADMAP.md#agreed-priorities-2026-11-02) now prioritizes target-host
scope, a runtime package independent of a development checkout and a CI-built
installer. Profile prompts, the Editor profile, German/English translations and
inline completion follow. A checked x86 host can receive the first installer
while other host variants complete their separate implementation and acceptance.
