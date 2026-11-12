# Source runtime for the TwinCAT Assistant

Beta 5 bundles published Engineering 0.5.3 and Knowledge 0.1.2. Their exact
source, release assets, receipts and tool catalogs are pinned separately in
[`engineering-lock.json`](engineering-lock.json) and
[`knowledge-lock.json`](knowledge-lock.json). The native build provider is built
from the released Engineering source; the package verifies published inputs
before staging them. See [application inputs](../docs/APPLICATION_INPUTS.md).

The independent Knowledge MCP release is pinned in
[`knowledge-lock.json`](knowledge-lock.json). On Windows,
[`Install-Knowledge.ps1`](../scripts/Install-Knowledge.ps1) installs its verified
wheel and hashed dependencies in a separate versioned environment. Its release
cycle and operator corpus remain separate from the OpenCode/OpenChamber source
build described below. See [installation](../docs/INSTALLATION.md).

`source-lock.json` fixes OpenCode 2.0.23 and OpenChamber 2.1.1 to their exact
Git revisions. Reviewed overlays retain its
providers, login methods, model catalog, streaming, cancellation, agent loop,
MCP clients, permissions and database while specializing the delivery surface.
Analysis remains read-only. Optional per-chat Build tools use native approval;
new source-editor tools and accessors are inactive in Beta 5. See
[delivery scope](../docs/DELIVERY_SCOPE.md) and
[runtime integration](../docs/RUNTIME_INTEGRATION.md) for the current boundaries.

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
