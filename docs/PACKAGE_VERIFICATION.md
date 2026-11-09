# Scoped full package verification

Completed 2026-11-06 on checkpoint `10ac145`, following the saved `1cdda4e`
draft. This closes the scoped verification implementation and its synthetic
Native80 acceptance test. It does not close the remaining Roadmap 1–4 host gates.

`withPackageVerificationScope()` uses AsyncLocalStorage with a per-operation map
of pending full-inventory promises. Parallel and nested checks of the exact same
package path share both success and failure. Each package still receives a full
recursive inventory, file-size and SHA-256 comparison against its manifest.
`finally` deactivates the context and clears the map on success or failure.
Async callbacks retaining that store therefore cannot reuse it after preparation.
There is no persistent verification cache.

The scopes cover native chat preparation, native MCP settings/configuration and
project preparation, and launch-plan preparation. Native chat closes its scope
before supervision, readiness callbacks and the long-lived service start.
Standalone later MCP requests get a fresh scope. `managed-relocation.mjs` uses
the same operation verifier for the old package's previously direct full scan.

Selection, ownership, state, policy, regular-file and reparse checks retain their
call sites. Cached inventory use repeats the cheap root and bounded manifest-leaf
guards. The cache never normalizes root keys: an alias gets a fresh verification,
including its original link checks. This intentionally prefers an extra scan for
an alternate spelling over sharing success across unchecked path spellings.
The immutable application-package assumption applies only during preparation;
mutable state is never cached. CLI entry validation and standalone operations
outside these scopes remain fresh. Installer rollback's separate direct scans
are unchanged and belong to its worker.

## Native80 measurement

[Recorded results and every source-file hash](evidence/package-verification-native80-20261106.json)
separate baseline and optimized code. Their full synthetic application manifests
remain in the private measurement directory recorded in that JSON. The trace
snapshots record completed full scans, not only verifier entries:
[baseline](evidence/package-verification-baseline-20261106.json),
[optimized](evidence/package-verification-optimized-20261106.json).

- Baseline restores the five affected source modules from `dda5221`. All other
  source files match the optimized variant, based on checkpoint `10ac145`.
- Optimized uses the finished scoped-verification modules, including repeated
  boundary guards. Both variants use identical trace instrumentation around the
  unchanged complete inventory algorithm.
- Baseline completes four current-package and two previous-package scans, then
  times out during a seventh scan. The real `StartAsync` returns after 85,588 ms,
  including its failure cleanup. It leaves C0 settings/configuration unchanged.
- Optimized completes exactly one full scan per package and returns successfully
  in 36,183 ms. Protected readiness, running process and subsequent owner stop are
  verified. The resulting configuration exactly matches generated C1, the
  Knowledge executable is rebased to the new package, and the history sentinel
  survives. No provider request, PLC/ADS operation or IDE action is performed.
- The C0 input's manifest SHA-256 is
  `e6fe29351f7d5c5100b6e92c9dafb7520efe00c3936d5c1f6e086c6bb77d2bc4`.
  The read-only new input is the old-Native baseline with manifest
  `12f0827fe13cfb1bdd18c54b56f9576b178de6d4c91803f339cf699ec5b03657`.
  Only the private copy gets updated source and a new, explicitly synthetic
  `0.0.0-scope-synthetic` manifest. Original manifests are not changed.
- The frozen host assembly is version `0.2.0.2`, product
  `0.2.0-preview.2+8886fb64f1fe64b19ef1f2a5ea01ec9f9012da1b`, SHA-256
  `fc0c3a0855bab48bf6987b3fffe199fa2dce70e012069f461724a1274bde7b3f`.
  Its compiled StartAsync state machine contains the 80000-ms timeout literal.
  An x86 PowerShell process loads this actual DLL, calls private `LoadFrom` with
  a private selector/config, then invokes public `NativeChatSession.StartAsync`.
  No deadline is changed and no Node300 READY is used as evidence.

Source-inventory hashes, using the exact path/size/hash arrays in the evidence:

```text
baseline  6f1fd91b607a64449d3d181130da3e4b824348565eeefcf1776152fd2ee24cb2
optimized 2f91b1a72c5739668455c91fde86f69e2cc80caf51322e39f60b46ea547ae780
```

## Verification and reproduction

`npm test`: 152 passed, zero failed or skipped. Tests cover parallel/nested
success, concurrent independent operations, shared failures, fresh recovery,
non-entrypoint modification/addition/deletion, relative paths, root aliases and
junction replacement, manifest-leaf replacement, state-selection changes inside
a scope, and callbacks after normal or exceptional scope completion.

The opt-in integration test against the disposable measured package also passes.
It invokes the real exported `readNativeMcpSettings()` API twice in parallel,
then tampers with manifest-listed `package-lock.json`. A later independent call
and an inherited callback both reject the package. Its original bytes are
restored in finally. This tests native MCP configuration requests, not live
Knowledge broker tool execution.

```powershell
node scripts/Measure-PackageVerificationStartup.mjs <C0-application> <input-application> local-data/<new-directory>
$env:TC_TEST_SCOPED_PACKAGE = '<measurement-output>/install/versions/0.0.0-scope-synthetic/application'
$env:DOTNET_ROOT = 'C:/Program Files/dotnet'
node --test tests/integration/package-verification-freshness.test.mjs
```

The measurement script refuses output outside this worktree's `local-data`,
creates a new directory by default and records all hashes. Explicit
`--reuse-staging` accepts only its synthetic version plus the expected old
manifest hash. Every run creates fresh synthetic state and fresh result files.
Reused staging was used for the recorded run to avoid copying the unchanged large
inputs again. Package paths are not trusted from a global operator configuration.

This is a single-machine, warm-filesystem, synthetic migration measurement.
The 80-second result applies to the frozen Native assembly above. The separate
host worker's new process-isolation contract and final rebuilt host payload still
need their own integrated test. The pending installer P2 recovery fix is outside
this change. No installation, push, PR or CI run was performed. Main remained at
recovery `f6e018a`; no main source, operator configuration or installed extension
was written by this worker.
