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
| Debug | Read-only ADS samples of approved PLC symbols and selective builds. |

## Editor

Select **Editor** in the composer and approve the project chat. Ask the
Assistant for a diff against the already-open document. Source changes can
only be applied to the exact supported XML buffer after native confirmation
of the diff and baseline. The resulting buffer remains unsaved. The normal
TwinCAT ST editor can show a read-only preview. An unknown or conflicting
result requires reconciliation before another mutation.

## Debug

Log in to the intended PLC from TwinCAT. Select **Debug** in the same project
chat. The dialog shows the bound project and its configured AMS Net ID.
Compare them with the PLC you are logged into, enter the verified ADS port
(851–899) and list the exact symbols to disclose to this chat and model.
An empty or unknown endpoint receives no grant.

The host rechecks the project's login and association during live reads.
The ADS port is operator-confirmed. The login check alone cannot identify a
different runtime port after a logout and relogin between observations.
Re-select Debug and confirm the endpoint again after changing targets.
The Debug process uses a read-only connection through the local ADS router
and returns timestamped scalar samples. Debug does not offer source edits,
PLC writes, breakpoints, stepping, forcing, login or runtime control.

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
