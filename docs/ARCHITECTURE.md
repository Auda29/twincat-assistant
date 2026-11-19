# Architecture and integration boundaries

TwinCAT Assistant 0.2.0 Beta 7 embeds the source-built OpenChamber 2.1.1 interface in a native WPF/WebView2 host and uses OpenCode 2.0.23 for sessions, model access and MCP clients. The IDE host resolves the IDE, solution and exact PLC project; external processes provide Knowledge and Engineering services. See [Runtime integration](RUNTIME_INTEGRATION.md) and [installation](INSTALLATION.md) for build inputs and setup.

```text
TcXaeShell x86 / native host
  WPF + WebView2 -> OpenChamber + OpenCode
  Project binding, context, native build approval and diagnostics
       | authenticated local transport
       +-- Knowledge MCP: local documentation corpus
       +-- Engineering MCP: saved project reads and native build provider
```

Analysis is the read-only default. Editor or Debug is bound to the selected project chat and native consent. Each selective build requires its own approval for the exact project, configuration and platform. Diagnostic navigation checks the build baseline and matching native ErrorItem. See [Profiles and native engineering](EDITOR_PROFILE.md).

Editor can read and preview supported open ST sections. A confirmed diff can be applied to an exact supported XML buffer. Debug reads operator-approved scalar symbols through the local ADS router after checking the existing TwinCAT login. See [Normal ST transactions](NORMAL_ST_TRANSACTIONS.md) for the remaining standard-editor write gate.

The published dependencies are Engineering 0.5.5 (`engineering-v0.5.5`, `b1a9b13a494b36da61534d8612d54bb05ff27492`) and Knowledge 0.1.2 (`v0.1.2`, `84c437aea8e27da930a246e7bb3ae310e8e5d31d`). Knowledge provides nine read-only tools, including five typed symbol, library and error lookup tools. Its corpus is supplied and activated locally, not bundled; documented metadata and versions do not establish installed libraries. See [Knowledge setup](KNOWLEDGE_SETUP.md) and [native MCP integration](NATIVE_MCP.md).

The native integration uses authenticated local transport, project-bound sessions and explicit disclosure for source attachments. General chat has Knowledge but no project engineering tools. General shell and filesystem mutation are denied by policy. The profiles do not grant PLC activation, download, online change, variable forcing, or runtime start/stop.

## Host and process responsibilities

The native host owns the tool window, WebView2 origin/message checks and exact IDE,
solution and PLC project binding. DTE and Automation Interface work stays on the
required IDE thread. External processes own sessions, model requests and MCP
transport; web content receives no generic COM or shell interface. Session
invalidation handles IDE shutdown, project changes and owned-child failure.

Active-editor tracking coalesces lifecycle events and rejects obsolete results.
Object names and tab titles alone do not establish identity. Following an editor
locally does not automatically disclose source or retarget a chat or approval.
Explicit snapshot attachments and saved-file tool results remain distinct.

Knowledge's bounded structured results are also delivered as text through the
reviewed compatibility adapter so citations and article content reach the model.
Tool discovery alone is not evidence that content was received. See
[native MCP integration](NATIVE_MCP.md) for protocol and broker details.

## Eligibility and remaining acceptance

The installation target is TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows x64. TwinCAT 4026 and Shell64 are not enabled. Package and synthetic fixture evidence does not establish installed-host behavior: real build events, dirty-state checks and diagnostic navigation, real-corpus typed extraction quality and performance remain open. See [Beta 7 release scope](releases/native-v0.2.0-beta.7.md) and [status](STATUS.md).
