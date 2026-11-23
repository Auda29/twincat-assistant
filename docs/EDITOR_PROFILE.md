# Profiles and native engineering

Choose the profile in the chat composer. Analysis is the default.
To use Editor or Debug, open a chat for the active PLC project in this
Assistant window. With the PLC project selected in TwinCAT, click **+**
in the Assistant to create a chat for that project. Selecting Editor or
Debug requests native consent. Switching back to Analysis or changing
chat, project or runtime owner revokes the grant.

| Profile | Access |
| --- | --- |
| Analysis | Read-only project and documentation tools. |
| Editor | Source discovery, read and preview, approved XML-buffer changes, and selective builds. |
| Debug | Read-only ADS samples from the connected PLC target and selective builds. |

## Editor

Select **Editor** in the composer and approve the project chat. Ask the
Assistant for a diff against the already-open document. Source changes can
only be applied to the exact supported XML buffer after native confirmation
of the diff and baseline. The resulting buffer remains unsaved. The normal
TwinCAT ST editor can show a read-only preview. An unknown or conflicting
result requires reconciliation before another mutation.

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
through the local ADS router and returns timestamped scalar samples. Debug does
not offer source edits, PLC writes, breakpoints, stepping, forcing, login or
runtime control.

## Builds

Both Editor and Debug can request a local build of the exact PLC project
using the verified configuration and platform. Each build asks for separate
native approval. Build output and diagnostics are bound to that operation.
Diagnostic navigation requires a current native error item and the recorded
build evidence. A build never widens to the whole TwinCAT solution.

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
