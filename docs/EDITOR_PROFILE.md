# Editor profile and native engineering

This is a private development feature in Assistant PR #30. It is not installed
or enabled by a released installer. Analysis remains the default profile.

## Using the feature

1. Select a project-bound chat in the local runtime started by this Assistant
   window. A general chat or another window's runtime cannot authorize Editor.
2. Choose **Enable Editor for this chat** and review the scope notice.
3. The chat switches to the managed `twincat-editor` profile. Its live tools are
   bound to this exact chat, workspace, IDE process, solution and PLC project.
4. Each build or source change requires a separate native confirmation. A chat
   message, model instruction or profile selection cannot approve an operation.
5. Choose **Return to Analysis** to revoke the connection. Changing chat,
   project, solution or owner also revokes it. Temporary editor invalidation
   suspends new approvals while preserving observation of submitted work.

Editor permits the reviewed engineering tools and existing read-only tools.
It does not permit arbitrary files, shell commands, ADS/runtime writes,
activation, download, forcing, automatic saving or configuration switching.
Analysis cannot use Editor tools even while the live server is connected.

## What works

- Describe the bound session and project association.
- Request a selective PLC build using its verified configuration and platform.
- Read build-owned status, output and diagnostics. Cancellation is a request,
  not proof of an isolated IDE cancellation.
- Discover supported already-open POU, Method and Action sections.
- Read live normal TwinCAT ST text and prepare a before/proposed preview.
- Apply approved changes to supported ordinary XML text buffers with an exact
  whole-buffer transaction. Rollback requires the unchanged recorded post-image.
- Click a diagnostic with a source-baseline navigation candidate. The helper
  holds and rechecks source/project evidence; the host requires one matching
  current native ErrorItem and invokes its own Navigate method. There is no
  path/line fallback when proof is missing.

Normal ST previews explicitly report `customStReadOnly` and
`atomicWriteSupported: false`. They issue no mutation-ready preview ID.
Properties, unknown XML metadata and unsupported custom editor shapes remain
refused. Source journals are session-memory records, not durable crash recovery.
An unknown submitted outcome blocks conflicting actions until correlated
terminal evidence reconciles it. It is never automatically retried.

## Runtime ownership

The native owner joins its Node launcher to a private kill-on-close Windows job
before sending the initial plan. Editor bootstrap enters the owned child through
stdin. A separate frontend bearer token exists only in Core's ephemeral MCP
configuration; it is absent from configuration GET results and durable files.
The host HMAC capability never enters that configuration, arguments, environment
or model tools. The broker additionally checks Core's actual chat metadata.

Revocation closes the native transport first and removes the temporary MCP
registration. A broken control channel stops only this window's owned runtime,
whose job reaps the broker. Pending bind generations, fresh chat epochs and
single-flight toggling prevent late activation after a context switch.

## Acceptance still required

The supported host is exactly TcXaeShell x86 / TwinCAT 3.1.4024.78. Installed
host build events, dirty-state interfaces, editor synchronization and ErrorItem
navigation still require a separately authorized live check. No operator IDE,
PLC project, provider login or corpus was used during implementation.

The remaining normal-ST write blocker is documented in
[NORMAL_ST_TRANSACTIONS.md](NORMAL_ST_TRANSACTIONS.md). The three-project
integration record is [MCP_P1_INTEGRATION_20261112.md](MCP_P1_INTEGRATION_20261112.md).
