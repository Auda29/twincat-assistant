# Source runtime for the TwinCAT Assistant

`source-lock.json` fixes OpenCode 2.0.23 and OpenChamber 2.1.1 to their exact
Git revisions. OpenCode's implementation is built unchanged. This includes its
providers, login methods, model catalog, streaming, cancellation, agent loop,
MCP clients, permissions and database. No tool family is removed in this step.

The reviewed OpenChamber overlay adds a versioned source integration to the
existing web entry and composer. `changes.json` lists exact, unique source
replacements. The original composer still owns references, submission, queueing,
failure recovery and rendering. Its hooks publish reference metadata and report
successful admission. Native context contributes metadata to its existing
synthetic context parts, without automatically attaching source code.

## Build

From this repository, with Node 22 or later and Git installed:

```powershell
npm ci
npm ci --prefix tools
npm ci --prefix upstream
npm run build:runtime
```

The build uses Bun 1.4.2 from the separately locked `tools` package. Both source
repositories are checked out under ignored `local-data/upstream`. Their original
lockfiles are installed frozen. Installs use separate caches to avoid Bun's
Windows cache-rename collision. The OpenCode build uses the original `latest`
channel and version 2.0.23, preserving the existing database namespace.

Build logs and the local runtime receipt live under `artifacts/source-runtime`.
The receipt records revisions, platform, overlay/build-input hashes and selected
artifact hashes. Native
startup and the operator CLI use this source build when its valid receipt is
present. `TWINCAT_ASSISTANT_RUNTIME=distribution` explicitly selects the existing
npm runtime. `source` requires a valid source build and never silently falls back.

An initial `--baseline` build records unmodified source builds separately. Run
it before applying the overlay. `--prepare` only prepares the exact sources and
overlay; `--ui-only` reuses an already verified OpenCode build during changes to
the UI integration. Unknown changes to either upstream's tracked sources are
rejected. Previously generated overlays can be refreshed using their recorded
hashes. These directories are generated inputs, not working copies to edit.

This arrangement keeps source inputs and our changes reviewable without creating
additional remote fork repositories. A later decision about fork repositories
can retain these revision and overlay records.

## Integration contract

The web entry exposes `window.__twinCatSource`, version 1. It names the original
UI/session/input/config/locale stores, loads session metadata through the
existing OpenCode SDK wrapper, and provides composer observations. The native
adapter no longer guesses minified asset exports for source builds. The pinned,
hash-checked distribution adapter remains for 0.1.9 compatibility.

Only the TwinCAT host activates context submission. The web surface uses this
contract when embedded. Electron, VS Code, hosted mobile and Capacitor retain
their existing behavior; the shared composer hooks do not inject context until
the TwinCAT contract is activated. No native host object, password or provider
credential is added to the contract.

Sources retain their upstream MIT license files and notices. Do not remove them
when packaging a source build. Existing upstream audit findings and Beckhoff
interop redistribution constraints remain documented in
[`SECURITY_AUDIT.md`](../docs/SECURITY_AUDIT.md).

See [`RUNTIME_INTEGRATION.md`](../docs/RUNTIME_INTEGRATION.md) for project identity,
data compatibility, context states and verification.
