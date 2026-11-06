# Maintenance guard build, 2026-11-06

This extends the earlier b38 integration record. It does not approve or perform
a global extension swap. The working installation and operator configuration
remain unchanged. The maintenance coordinator is being implemented and tested
separately with synthetic hosts before any request for a real maintenance window.

## Implemented guard

Native source `c5790d6` adds the opt-in marker contract documented in the
[local maintenance plan](LOCAL_MAINTENANCE_ACCEPTANCE_PLAN.md). The marker is
resolved beside the original Assembly.CodeBase, including Shadow Copy. Missing
or mismatched process override, SID, config hash or selector hash refuses before
default config/credentials/preferences/bindings/WebView paths are resolved.
Unmarked behavior stays unchanged. Removing the marker or replacing the DLL
with old code bypasses the guard by design; this is not a same-user security
boundary. The coordinator must enforce quiescence and publish an intact marked
host. The production installer override refusal remains unchanged.

65 synthetic guard checks and 29 existing private-contract checks passed on the
worker-built DLL. These cover actual public getters, public Load, default store
constructors, malformed/duplicate schemas, path/link refusals and actual
AppDomain Shadow Copy. No installed IDE or global host was involved.

## Initial combined package trial

The worker-built DLL has SHA-256
`a3c3aa621a3795c2eb3695bca054673e1f7bf9e54af16912cfbd9e8da55d7e3d`.
It was compiled before the source commit and its ProductVersion still names
parent `c1f080e`. Its actual guard implementation was exercised, but this is
not the final clean-commit native build identity.

The initial package in `artifacts/alpha-maintenance-20261106/application` has
22,088 entries and manifest SHA-256
`cb92a12b61e4aa8841d5c9fb87fb4280f296616271bb92da4e00cd36b0d12e0f`.
The marker is absent from the package. `Measure-PrivatePackagedNative.mjs
--maintenance` creates it only beside a separate copied host.

The actual x86 public loader and unchanged compiled 80-second StartAsync reached
protected readiness in 33,062 ms. The marked host rejected a missing override.
Owner stop, exact C1 policy, rebased Knowledge path, history sentinel and full
inventory verification of both original and both copied packages all passed.
The input C0 manifest remains the original `e6fe2935...` package. This is a
warm-filesystem global Knowledge-profile migration outside the IDE, not UI,
installed update/rollback or first-after-logon acceptance.

Raw report: `local-data/guarded-private-native-20261106/report.json`.
The [compact evidence](evidence/native-maintenance-initial-20261106.json)
records source, harness, fixture, package and native hashes without profile paths.

## Final coordinated candidate

A clean-commit native rebuild, fresh final package, repeated guard/private
checks and exact-package Native80 test are pending. The source receipt retains
the already verified pinned Core executable and separately rebuilt Chat UI;
there is no claim of an additional Core compilation. The final package identity
must supersede, not overwrite or silently adopt, the initial cb92 trial.

The older producer preparation `02194d7` is bound to b38/6dbb only. A producer
for this new native guard requires a separately reviewed final source/manifest
baseline. No runner or CI producer has been started for the guard candidate.
