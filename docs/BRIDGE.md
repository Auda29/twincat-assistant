# Local bridge contract (protocol version 1)

This document defines the portable, read-only local bridge shipped in
`engineering/src/Engineering.Bridge`. It is a diagnostic transport boundary,
not a TwinCAT implementation. No native engineering adapter is included, and
no mutation, build, deploy, runtime, COM, shell, or arbitrary method is
reachable through this bridge.

## Start the diagnostic server

The executable reads its authentication token from an environment variable so
the token is not placed in command-line history or process arguments:

```sh
export TWINCAT_ASSISTANT_BRIDGE_TOKEN='generated-out-of-band'
dotnet run --project engineering/src/Engineering.Bridge/Engineering.Bridge.csproj -- \
  --pipe twincat-assistant-bridge \
  --token-env TWINCAT_ASSISTANT_BRIDGE_TOKEN
```

Use a high-entropy token generated and supplied by the local launcher. The
server does not print it. On Windows, `PipeOptions.CurrentUserOnly` restricts
the named pipe to the current Windows user. The same option is requested on
portable .NET platforms; the handshake token remains mandatory on every
platform. The server accepts one connection at a time and has no TCP/listening
socket.

The production launcher should use the built executable instead of `dotnet
run`. The command above is the diagnostic command and returns `2` for missing
or malformed arguments/token.

## Framing and handshake

Messages are newline-delimited UTF-8 JSON (NDJSON). Each frame is at most
16,384 bytes by default, including JSON bytes and excluding the newline. A
server may configure a smaller bound; clients must respect the negotiated local
bound supplied by their launcher. Frames larger than the bound are rejected.
There is no chunking, compression, or batch request format. The server closes a
connection that does not complete its current frame within five seconds (the
portable server constructor accepts a shorter timeout for tests); this timeout
is connection-local and does not terminate the server or prevent the next peer.

The client must send the first frame:

```json
{"type":"hello","protocol":1,"token":"<secret>"}
```

A successful server response is:

```json
{"type":"hello-ack","protocol":1}
```

The server compares the token in constant time and closes an unauthenticated
connection. Authentication errors use the stable error code `unauthorized`;
the token is never included in an error, log, or response.

## Requests and responses

A request has exactly the supported shape below (additional fields are ignored
only where they are not security-sensitive; `id`, `method`, and object-valued
`params` are required):

```json
{"id":"req-1","method":"get-status","params":{}}
```

Success always has the request ID and a `result`:

```json
{"id":"req-1","result":{"available":false,"status":"unavailable","readOnly":true}}
```

Failure always has the request ID (or JSON `null` when no valid ID could be
read) and an object error:

```json
{"id":"req-1","error":{"code":"method_not_allowed","message":"method is not allowed"}}
```

Stable error codes are `invalid_json`, `invalid_request`,
`method_not_allowed`, `unauthorized`, `unavailable`, and `internal_error`.
Error messages are diagnostic text and must not be parsed for control flow.
Clients must match response IDs and must not retry a request after a transport
failure.

## Read-only method allowlist

Only these methods exist in protocol version 1:

| Method | Parameters | Result |
| --- | --- | --- |
| `get-context` | `{}` | Current binding/context, or `available:false` |
| `get-status` | `{}` | Bridge availability and read-only status |
| `read-target` | `{}` or a validated object selector | Target declaration/implementation when available |

The diagnostic executable returns `available:false` and `status:"unavailable"`
for all three methods because no native adapter is installed. It never reports a
synthetic build, write, success, deployment, or runtime state. Names such as
`apply`, `approve`, `rollback`, `build`, `save`, `deploy`, `download`, `login`,
`start`, `stop`, `force`, `com`, `shell`, or an arbitrary method are not
allowlisted and return `method_not_allowed`.

## Engineering safety boundary

The portable core stores an exact binding (IDE instance, solution, project),
object identity, full declaration and ST implementation baseline, revision, and
known dirty state. Writable domain scope is limited to an existing non-safety
Program, FunctionBlock, or Function written in Structured Text. Required
capabilities include read, declaration write, implementation write, known dirty
state, and revision. The bridge transport does not expose those writes.

A proposal hash binds target, baseline, proposed declaration and implementation,
kind, and expiry. Only an operator approval receipt matching every field can be
submitted. Apply serializes a fresh binding/dirty/revision/content check, writes
the backup through the supplied durable store before any adapter write, writes
sections separately, and reads both sections back. Partial and unknown outcomes
are terminal for that request; unknown targets are blocked until a fresh
reconciliation read that verifies exact target, current binding, clean dirty
state and revision and persists its audit first; partial reconciliation stays
blocked. Approval is one-use, expires, and is invalidated on binding
switch or shutdown. Rollback requires a separately approved proposal and a
verified matching post-state.

The core never performs a blanket save, deploy, online change, runtime action,
or automatic retry. `IEngineeringAdapter` is an injected boundary only; a real
TwinCAT adapter remains future work and must be accepted separately.

## Native adapter and crash-recovery obligations

Proposal, approval, consumed-operation and unknown-target indexes are held in
memory. The file store persists bounded backups/audit records, not a recovered
transaction journal. After a process crash, do not resume native writes using a
new coordinator until persisted records and actual IDE state are reconciled.
This diagnostic-only delivery never connects a writing adapter, so no production
write path bypasses that missing recovery gate. Native release requires durable
unknown/replay recovery and explicit handling of post-write audit failures.

Binding notifications are serialized with operations. The future IDE adapter
must not synchronously call those notifications from within an adapter call or
block the COM/IDE thread waiting for the coordinator; design and test an async
lifecycle dispatch boundary. It must validate the exact target and binding at
each actual write and define cancellation/timeout semantics. Portable rechecks
cannot remove races inside a separate native editor.

Current-user pipe access plus a token is not protection against malicious code
already running with the operator's privileges. The client is intended for
sequential trusted local diagnostics; parallel client requests, authenticity of
installed executables, response/service deadlines and adversarial native host
behavior require separate acceptance before exposing broader methods.

## Verification

From the repository root, with the pinned SDK (`/workspace/.toolchains/dotnet/dotnet`
or a compatible .NET 8 SDK):

```sh
/workspace/.toolchains/dotnet/dotnet restore engineering/EngineeringSafety.sln --locked-mode
/workspace/.toolchains/dotnet/dotnet build engineering/EngineeringSafety.sln --no-restore --nologo
/workspace/.toolchains/dotnet/dotnet run --project engineering/tests/Engineering.Safety.Tests/Engineering.Safety.Tests.csproj --no-build --no-restore
```

The regression runner reports explicit `PASS`/`FAIL` lines and exits nonzero on
failure. It uses an explicitly named synthetic in-memory adapter only in the
test project; that adapter is never the bridge default. The 34-test suite covers
clean success, object/capability/dirty rejection, stale state, approval
hash/expiry/replay, shutdown and binding invalidation, canonical hash and typed
identity boundaries, serialization, backup ordering, partial and unknown
outcomes, read-back mismatch/unavailability, rollback matching and newer user
changes, local file permissions, authenticated named pipes, malformed
object/array/null frames, response-ID validation, exact frame bounds, idle read
deadlines, stable responses, and mutation rejection.

These tests do not prove TwinCAT Automation Interface behavior, Visual Studio
threading, native dirty-state semantics, real builds, or machine safety. Those
remain blocked until a separately reviewed native adapter and host integration
exist.
