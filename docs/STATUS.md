# Implementation status

Version: `0.1.0-dev.1`. This is a development foundation, not a TwinCAT release.

## Implemented

- Node operator commands `init`, `doctor`, and foreground `start`.
- Exclusive private state initialization, policy integrity checks, isolated
  HOME/XDG paths, restricted inherited environment, explicit source-code
  disclosure consent before provider access, and exact read-tool permissions.
- Loopback-only, password-protected OpenChamber/OpenCode launch with pinned
  version checks, duplicate-start locking and POSIX process-group cleanup.
- A bounded production MCP probe, exercised against both real pinned servers:
  modern documentation protocol and legacy saved-project engineering protocol.
- An installable OpenChamber panel displaying host directory/session context.
  Explicit context attachment, session rebinding and error states; no native
  bridge, file, service or model capability is requested by this panel.
- Portable .NET 8 domain contracts for exact object/baseline-bound proposals,
  operator approval, expiry, one-use serialized apply, pre-write backups,
  readback, partial/unknown outcomes and separately approved rollback.
- Authenticated, bounded, current-user named-pipe diagnostics. Only
  `get-context`, `get-status`, and `read-target` are exposed. The executable
  reports native engineering **unavailable**; domain writes are not on the wire.

## Evidence and limits

`npm run check` exercises Node contracts and the actual built panel in Chromium
through the real SDK postMessage boundary and a labelled test host. This is not
installation acceptance in the real OpenChamber UI or WebView2.

The .NET regression runner exercises production domain code, private files and
real local named pipes. Its in-memory engineering adapter is synthetic and
exists only in tests. No COM, Automation Interface or compiler result is faked.

The upstream integration suite uses real installed OpenChamber, OpenCode and
MCP processes. Project and corpus fixtures are synthetic; a successful fixture
call does not establish Beckhoff documentation coverage, a live IDE binding,
model-context acceptance or machine safety. OpenCode `2.0.23` registers MCP
plugins lazily: listing agents warms the location before polling `/api/mcp`.
The integration test uses that read-only operation, not a model request, and
asserts discovery of both configured servers. The exact commands are in
[installation](INSTALLATION.md). Native MVP acceptance boxes remain open.

## CI boundary

`Portable checks` runs the root audit, Node tests, production-panel browser
contracts and the portable .NET runner on Linux. It does not clone private MCP
repositories, validate the complete upstream runtime, or execute native Windows
acceptance. Those checks are intentionally separate and must not be inferred
from a green component job. See [security audit](SECURITY_AUDIT.md) for release
blockers.

## Not implemented / external acceptance required

- Classic Visual Studio/TcXaeShell64 package, WPF tool window and WebView2 host.
- Native project/object discovery, editor dirty-state and revision semantics.
- Live monorepo engineering adapter, operator review integration, live apply or
  rollback, and project-bound compiler diagnostics.
- A supplied Beckhoff corpus or a real provider conversation using both MCPs.
- Windows process-tree supervision: `start` explicitly refuses Windows until
  safe native cleanup is implemented and validated. Building the portable C#
  components is not evidence of a working Windows product.
- Full extension install/reload and chat persistence acceptance in OpenChamber.
- Production storage retention, installer/update delivery, signing and release.

Continue with [Windows acceptance](WINDOWS_ACCEPTANCE.md); do not enable writes
because the portable tests pass. No deployment, online change, forcing, PLC
start/stop or runtime write is exposed by this foundation.
