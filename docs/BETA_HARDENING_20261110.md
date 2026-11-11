# Unreleased beta hardening

The operator confirms that Beta 3 is substantially more responsive. This work
does not claim that every intermittent initialization failure is resolved.

## Startup and context

Repeated project setup no longer acquires the global provider writer lock when
its inspected configuration is already current. Actual changes retain the
transaction and lock. A regression holds another writer's lock while repeating
setup and verifies unchanged global configuration bytes.

Member source correlation now requires independently captured text to match its
live XML, as well as matching object/owner IDs. Torn Action/Method/Property/Get
snapshots stay unknown, rather than being labelled saved. Previous correlation
is cleared before each attempt. Dirty document evidence still takes precedence.
Saved XML reading uses a small growing buffer instead of allocating roughly
2 MiB on every poll. Its existing size bound remains. Actual unsaved editor-buffer
access is **not newly implemented or claimed**; AI snapshot provenance remains.

## Knowledge validation overlay

The input builder applies an exact-SHA-bound overlay to upstream 0.1.1's
provenance module. ASCII-control detection uses the same range through regex
matching; successful exact locator strings use a bounded 4,096-entry process-local
cache. Exceptions are not cached. Percent decoding, traversal, device/ADS,
canonical URL and length checks remain. Corpus consistency, manifests, file
hashes and index checks still run. There is no persistent validation cache.

This is **Assistant's modified build of 0.1.1**, not an upstream release. Original
source, input/output hashes, an overlay receipt and the corrected wheel RECORD
are retained. The application builder requires the reviewed patched hash; old
inputs must be rebuilt. Its optional second absolute argument selects a separate
input directory, so existing issued inputs need not be changed.

Actual Python/pinned dependencies pass 222 validator comparisons, failure
non-caching and cache bounds. The relocated real MCP catalog/status probe and
the complete application/MCP/restart smoke pass without package mutation.

The existing documentation corpus was read-only, with separate private caches.
Both variants validate the same manifest, 49,970 topics and 525,278 sections.
Wall-clock samples were 123,464 ms original and 77,999 ms patched. Build work
overlapped portions of these samples; they are not controlled cold-disk
benchmarks or latency guarantees. The **15-second cold-start target is not met**.
Synthetic repeated-locator timings must not be presented as corpus startup times.

## Recovery and release automation

The actual installer transaction fixture now verifies byte-identical chat,
binding and DPAPI-protected login sentinels after install/update/rollback, plus
successful same-user decryption. These are synthetic credentials and a synthetic
host, not real provider-login or workstation rollback acceptance.

`scripts/Build-Release.ps1` automates a fresh source/native/MCP/license/package/
installer build, regressions, native WebView startup, package smoke and actual EXE
extraction. It emits checksums and source provenance. Existing inputs and output
directories are not overwritten. Vendor interop stays local and is not shipped.

`.github/workflows/release.yml` permits owner dispatch from trusted main only.
Candidate-only is the default. Optional publication is a separate hosted job
with write permission. It checks source identity, artifact names, local checksums
and uploaded digests before Latest publication. Reviewed English notes and a new
unused version/tag are required; existing releases/tags are never replaced.

The build job requires a dedicated trusted Windows x64 runner labelled
`twincat-build`, installed TwinCAT reference, VS Build Tools, WebView2, Git, gh
and uv. Prefer an ephemeral runner with a separate account/workspace. No PR code
runs on it. GitHub currently reports **zero** self-hosted runners. This work
created no runner registration or service. The complete new workflow has thus
**not been executed**; automation is implemented, not operationally accepted.

## Verification and remaining gates

- Fresh x86 build and 944 native regressions pass.
- Eight native MCP/profile tests pass, including no-op setup under a busy writer.
- All 17 actual standalone WPF/WebView startup checks pass.
- Fresh Knowledge input build, Python equivalence and full corpus validation pass.
- Full application smoke with both real MCPs, restart and state preservation passes.
- Synthetic installer update/rollback and protected login/binding preservation pass.

Installed normal-use reliability, cold solution/editor restoration, member
editors and true unsaved-buffer behavior remain practical gates. Real workstation
rollback with retained sign-ins needs a coordinated closed-IDE window. No UAC,
IDE attach/restart, installed payload changes, operator profile changes, customer
source access or new public release occurred during this work.
