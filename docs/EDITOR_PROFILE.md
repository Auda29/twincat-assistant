# Profiles and native engineering

Choose the profile in the chat composer. Analysis is the default.
These profiles apply to TwinCAT Assistant 0.2.0 Beta 12 with Engineering MCP 0.5.12.
To use Editor or Debug, open a chat for the active PLC project in this
Assistant window. With the PLC project selected in TwinCAT, click **+**
in the Assistant to create a chat for that project. Selecting Editor or
Debug requests native consent. Switching back to Analysis or changing
chat, project or runtime owner revokes the grant.

| Profile | Access |
| --- | --- |
| Analysis | Read-only project and documentation tools. Saved-project tools use the static `engineering` server. |
| Editor | Project-wide ST discovery, read and preview, approved source edits and object creation, and selective builds via `engineering_editor`. |
| Debug | Read-only ADS samples, condition waits and symbol watches from the connected PLC target, and selective builds via `engineering_debug`. |

## Editor

Select **Editor** in the composer and approve the project chat. Ask the
Assistant to find a Structured Text object in the bound project or limit
discovery to a file. Review the exact before and proposed text in the native
side-by-side approval dialog, with additions and removals highlighted in color.

Engineering MCP 0.5.12 sends tool approvals with a 90-second expiry, within
the native host's 120-second ceiling. The Engineering MCP client and native
host allow 125 seconds for a mutation. The pipe exchange allows 130 seconds.
The broker allows 150 seconds for a child exchange and 155 seconds for MCP
execution over HTTP, leaving time for grant checks, ADS connection and teardown
around a 120-second Debug wait.

- TwinCAT Automation Interface edits support closed and open ST POUs,
  members, DUTs, interfaces and GVLs. Changes may persist directly to disk.
  The provider refuses conflicting or unverified unsaved editor changes.
- Edits to a supported open XML editor buffer remain unsaved.
- The creation dialog shows the project tree path, kind, options and initial
  text. Supported creation kinds are `fb`, `prg`, `fun`, `method`, `action`,
  `property`, `dut_struct` and `gvl`. TwinCAT writes the object file or updates
  the owning POU file immediately. Changes to the `.plcproj` remain pending save.
- Applied edits have a session journal and can be rolled back after another
  native approval if the current content still matches the applied change.
  When rollback reports `unsaved_editor_changes`, save the open document in
  TwinCAT before requesting another attempt. Creation has no source journal.
  If any mutation reports an unknown outcome, inspect the project before
  continuing and do not retry that operation.

Editor has no rename, delete, save or PLC runtime operations.

## Debug

Log in to the intended PLC from TwinCAT and select **Debug** in the bound
project chat.

By default, **Connect Debug automatically** is enabled in the Assistant's
`...` menu (persisted in `preferences.json` as `autoDebug: true`). When
enabled and the PLC project provides a valid configured AMS Net ID and ADS port
(`851–899`), selecting **Debug** connects directly in read-only mode for all
symbols (`symbolScope: "all"`) without a modal dialog.

If you turn **Connect Debug automatically** off (or if the project does not
report a configured ADS port), selecting **Debug** falls back to the manual
confirmation dialog (with any detected ADS port pre-filled), where you confirm
the endpoint and list exact symbol names to disclose.

The host rechecks the project's login, association and configured target
address during live reads. Configured AMS Net ID and ADS port are project
configuration evidence, not independent proof of a different runtime port
after a logout/relogin between observations; re-select Debug after changing
targets. The Debug process uses a strictly read-only ADS connection
(`readOnly: true`, empty write allowlist, up to 8 scalar symbols per read)
through the local ADS router and returns timestamped scalar samples. Use
`engineering_debug_plc_debug_wait_until` to wait up to 120 seconds for a
condition on approved symbols. Set `stableForMs` when the condition must stay
true for a minimum duration. Use `engineering_debug_plc_debug_watch` to
register or reuse a bounded watch, `engineering_debug_plc_debug_list_watches`
to see active watches and their latest observations, and
`engineering_debug_plc_debug_unwatch` to release a watch. Debug does not
offer source edits, PLC writes, breakpoints, stepping, forcing, login or
runtime control.

## Builds

Both Editor and Debug can request a local build of the exact PLC project
using the verified configuration and platform. Each build asks for separate
native approval. Build output and diagnostics are bound to that operation.
Diagnostic navigation requires a current native error item and the recorded
build evidence. A build never widens to the whole TwinCAT solution.
If build preflight reports `unsaved_project_changes`, save the pending project
changes in TwinCAT XAE before asking for another build.

The host supplies the host's compiled interop types to the provider's
authoritative factory. An unavailable reference can still block build or
Debug preflight. The engineering profiles do not grant automatic saving,
activation, download or configuration switching.


## Runtime ownership

The native owner joins its Node launcher to a private kill-on-close Windows job
before sending the initial plan. Editor bootstrap enters the owned child through
stdin. A separate frontend bearer token exists only in Core's ephemeral MCP
configuration; it is absent from configuration GET results and durable files.
The host HMAC capability never enters that configuration, arguments, environment
or model tools. The broker additionally checks Core's actual chat metadata.

Revocation closes the native transport first and removes the temporary MCP
registration before the runtime registers the next profile. A broken control channel stops only this window's owned runtime,
whose job reaps the broker. Pending bind generations, fresh chat epochs and
single-flight toggling prevent late activation after a context switch.

## Acceptance still required

The supported host is exactly TcXaeShell x86 / TwinCAT 3.1.4024.78.
The [Beta 12 release notes](releases/native-v0.2.0-beta.12.md) record the
isolated dirty-state and preflight checks on a synthetic PLC project.
End-to-end acceptance in the installed host remains open.

The attached native ST buffer and Automation Interface boundaries are documented in
[NORMAL_ST_TRANSACTIONS.md](NORMAL_ST_TRANSACTIONS.md). The three-project
integration record is [MCP_P1_INTEGRATION_20261112.md](MCP_P1_INTEGRATION_20261112.md).
