# Architecture and integration boundaries

TwinCAT Assistant 0.2.0 Beta 12 embeds the source-built OpenChamber 2.1.1 interface in a native WPF/WebView2 host and uses OpenCode 2.0.23 for sessions, model access and MCP clients. The IDE host resolves the IDE, solution and exact PLC project. External processes provide Knowledge and Engineering services. See [Runtime integration](RUNTIME_INTEGRATION.md) and [installation](INSTALLATION.md) for build inputs and setup.

```text
TcXaeShell x86 / native host
  WPF + WebView2 -> OpenChamber + OpenCode
  Project binding, context, native source/build approvals and diagnostics
       | authenticated local transport
       +-- Knowledge MCP: local documentation corpus
       +-- Engineering MCP: project reads, approved ST changes/builds, read-only Debug
```

Analysis is the read-only default. Editor or Debug is bound to the selected project chat and native consent. Each selective build requires its own approval for the exact project, configuration and platform. Diagnostic navigation checks the build baseline and matching native ErrorItem. See [Profiles and native engineering](EDITOR_PROFILE.md).

Editor discovers ST across the bound project and supports native-approved edits
and creation through the TwinCAT Automation Interface. These edits may persist
directly to disk. Edits to a supported XML buffer remain unsaved. Debug reads
scalar symbols through the local ADS router after checking the existing TwinCAT
login. By default, it connects automatically to the project's configured AMS
Net ID and ADS port in read-only mode (configurable in the `...` menu). It also
supports condition waits of up to 120 seconds and bounded symbol watches. See
[Structured Text transaction paths](NORMAL_ST_TRANSACTIONS.md).

Analysis uses the static `engineering` MCP server. Editor and Debug use separate
temporary `engineering_editor` and `engineering_debug` servers. Profile changes
revoke the previous grant and remove its registration before enabling the next
profile, while retaining the selected chat and queued follow-ups.

The published dependencies are Engineering 0.5.13 (`engineering-v0.5.13`, `aee9b6d5c39dc7b03b87725e8fec61199c29f689`) and Knowledge 0.1.2 (`v0.1.2`, `84c437aea8e27da930a246e7bb3ae310e8e5d31d`). Knowledge provides nine read-only tools, including five typed symbol, library and error lookup tools. Its corpus is supplied and activated locally, not bundled; documented metadata and versions do not establish installed libraries. See [Knowledge setup](KNOWLEDGE_SETUP.md) and [native MCP integration](NATIVE_MCP.md).

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

The installation target is TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows x64.
TwinCAT 4026 and Shell64 are not enabled. An isolated live run on a synthetic
standard PLC project verified clean/unsaved project detection and a selective
build with 0 errors and 0 warnings. Full installed-host workflows, diagnostic
navigation, real-corpus typed extraction quality and performance remain open.
See [Beta 12 release scope](releases/native-v0.2.0-beta.12.md) and [status](STATUS.md).
