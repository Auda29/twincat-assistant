# Architecture and integration boundaries

TwinCAT Assistant 0.2.0 Beta 5 embeds the source-built OpenChamber 2.1.1 interface in a native WPF/WebView2 host and uses OpenCode 2.0.23 for sessions, model access and MCP clients. The IDE host resolves the IDE, solution and exact PLC project; external processes provide Knowledge and Engineering services. See [Runtime integration](RUNTIME_INTEGRATION.md) and [installation](INSTALLATION.md) for build inputs and setup.

```text
TcXaeShell x86 / native host
  WPF + WebView2 -> OpenChamber + OpenCode
  Project binding, context, native build approval and diagnostics
       | authenticated local transport
       +-- Knowledge MCP: local documentation corpus
       +-- Engineering MCP: saved project reads and native build provider
```

Analysis is the default read-only profile. Project chats may explicitly enable **Build tools** for that chat, using the internal `twincat-editor` agent's C4 policy. Nine native build, session and diagnostic tools become available; each selective PLC build requires separate native approval for the exact `.plcproj`, configuration and platform. Output and diagnostics belong to the requested build. Navigation requires an unchanged source/project baseline and one exact current native ErrorItem; there is no path-only fallback. See [Editor profile](EDITOR_PROFILE.md).

The release does not expose the new source discovery, live read, preview, apply, rollback or journal workflows. The native provider receives no editor accessor and `source.*` dispatch is denied. The 15-tool development path remains in source for later work. Existing read-only editor context, explicit attachments and saved-file MCP reads remain available; an attachment is a snapshot and does not prove the current unsaved buffer. Normal ST writes require a verified exact-section transaction; see [Normal ST transactions](NORMAL_ST_TRANSACTIONS.md).

The published dependencies are Engineering 0.5.3 (`engineering-v0.5.3`, `99de69b8cedec780a68a89a631132ea6eb76cf0b`) and Knowledge 0.1.2 (`v0.1.2`, `84c437aea8e27da930a246e7bb3ae310e8e5d31d`). Knowledge provides nine read-only tools, including five typed symbol, library and error lookup tools. Its corpus is supplied and activated locally, not bundled; documented metadata and versions do not establish installed libraries. See [Knowledge setup](KNOWLEDGE_SETUP.md) and [native MCP integration](NATIVE_MCP.md).

The native integration uses authenticated local transport, project-bound sessions and explicit disclosure for source attachments. General chat has Knowledge but no project engineering tools. General shell and filesystem mutation are denied by policy. Neither profile permits PLC activation, download, online change, variable forcing, or runtime start/stop.

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

The installation target is TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows x64. TwinCAT 4026 and Shell64 are not enabled. Package and synthetic fixture evidence does not establish installed-host behavior: real build events, dirty-state checks and diagnostic navigation, real-corpus typed extraction quality and performance remain open. See [Beta 5 release scope](releases/native-v0.2.0-beta.5.md) and [status](STATUS.md).
