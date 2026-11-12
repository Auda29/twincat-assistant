# MCP-first integration window, 2026-11-12

The first coordinated development window ends at 14:30 Europe/Berlin. All three
repository leads use GPT-6.1 Sol high. Bounded implementation tasks use GPT-6.1
Terra; demanding architecture and independent reviews use GPT-6 Astra.
Changes remain pull requests, without automatic merges, releases or installation.

## Ownership and current limits

- Engineering MCP owns reusable native engineering code, project association,
  source evidence, build/diagnostics and controlled source-change policy.
- Knowledge MCP owns documentation ingestion and typed symbol/library/error
  tools. Documentation versions are not installed library versions.
- Assistant owns the native UI, exact IDE/chat binding, one-shot native approval,
  authenticated transport, provider loading and eventual package adoption.

[Engineering PR #2](https://github.com/Auda29/twincat-mcp-mono/pull/2) and
[Knowledge PR #27](https://github.com/Auda29/twincat-knowledge-mcp/pull/27) are
development dependencies, not accepted releases. The existing packaged dependency
pins, Analysis permissions and read-only saved-file MCP configuration remain.
Assistant PR #29 is not the source of a second independent compiler implementation.

This Assistant slice is a foundation. It does not deliver a verified PLC build,
clickable compiler navigation or source writes. The documented DTE build addresses
a TwinCAT `.tsproj`; a bound `.plcproj` must not silently widen that scope. The
button stays disabled unless an exact provider reports `plcBuild: "supported"`.
Generic `build: "conditional"` and authenticated connectivity do not satisfy it.
Current source mutation and navigation support remain unavailable.

## Implemented Assistant boundary

The optional provider is loaded only from the installer-selected application's
`host/x86/TwinCatMcp.Engineering.Host.dll`. The selector must agree with the
connection's package root. The manifest must contain one exact entry; actual bytes
are size/hash checked before loading. Assembly identity is
`TwinCatMcp.Engineering.Host`, version `1.0.0.0`. No developer-configured folder is
an executable-code authority. Existing packages without the provider keep Chat
working and report the optional engineering feature unavailable.

Host eligibility remains exactly TcXaeShell x86, executable 15.0.0.0, shell base
major 15 and TwinCAT 3.1.4024.78. This does not enable 4026, Shell64 or Visual Studio.

Engineering sessions use a host-generated UUID, current PID/start time, exact
solution/PLC paths and a lifecycle epoch, separate from chat IDs. Rebinding,
project invalidation, solution close/reopen and disposal revoke the session and
pending approval. Source operations are not approved by this UI. Build approval
is one-shot, short-lived and binds the complete UTF-8 request digest, operation,
context and target. The generic authorization policy additionally binds section,
preview/journal IDs and before/after digests for future reviewed source actions.

The UI confirms manual actions, observes only correlated bounded results and
never claims a timed-out/cancelled operation has stopped. No compiler logic is
duplicated in Assistant. Navigation and automatic diagnostic disclosure are absent.

## Transport contract

`EngineeringHostTransport` is an independently tested local transport component;
it is not a deployed child-process bootstrap. It uses a random pipe name,
protected current-user SID-only DACL, remote-client refusal, a fresh 256-bit
capability and mutual HMAC-SHA256 authentication. Each connection performs one
request with domain-separated client/server/request/response proofs and fresh
nonces. The capability itself never appears in a wire frame. UTF-8 decoding,
duplicate keys, exact frame schemas and byte limits are checked. Handshake frames
are bounded to 4 KiB, encoded requests to 256 KiB and responses to 512 KiB.
The deadline is 30 seconds, with at most four connections/outstanding handlers.

A handler ignoring cancellation can outlive a connection and retain its slot;
there is no automatic replay. The transport does not verify client PID: knowledge
of the session capability authorizes a local same-user client. That capability
must only enter a trusted owned child through stdin, never model arguments,
durable configuration, environment variables, command-line arguments or logs.
Wiring that owned-child bootstrap and the complete package is a separate open
integration gate. The transport does not by itself authorize engineering actions.

## Validation and remaining gates

- Fresh pinned SDK 8.0.425 x86 build: 1,008 native/localization/authorization
  regressions passed; helper/probe/host/VSIX compile succeeded. Existing threading
  warnings remain. Seventeen standalone WPF/WebView startup checks passed with
  private synthetic state and no installed IDE.
- The standalone transport probe exercises valid and invalid authentication,
  malformed/duplicate frames, exact payloads and MACs, byte limits, idle timeout,
  normal disposal and recovery after a refused client.
- Native approval regressions cover no approval, replay, changed epoch/session,
  expiry, revocation, identical-text object substitution and altered request bytes.

No IDE was started or attached, no operator source/state was inspected, no PLC
was compiled and no installation was performed. Synthetic checks do not establish
actual editor buffer atomicity, build scope, navigation or installed acceptance.
Provider package receipt/adoption, owned-child bootstrap, cross-repository
integration and independent final review remain open. P1 is not complete.
