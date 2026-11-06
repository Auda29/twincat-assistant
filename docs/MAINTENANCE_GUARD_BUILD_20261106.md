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

Parent rebuilt the complete native inputs from clean commit
`b0bf7c2ab18e6a6ff0daaeacb3fdbbbe80ff5b99`, including EmbeddedChat, WindowsHost,
ReadOnlyProbe and x86 IdeHost, with the pinned SDK 8.0.425. All 151 native
regressions, 65 guard checks and 29 private-contract checks passed on this new
build. Its six VSTHRD010 warnings are unchanged. The source receipt retains the
already verified pinned Core executable and separately rebuilt Chat UI; there
is no claim of an additional Core compilation.

The final coordinated DLL has SHA-256
`77e03ab221db44cd2a4eb442fa778f22de3da565940272312e540d85bac10ffd`,
ProductVersion `0.2.0-preview.2+b0bf7c2ab18e6a6ff0daaeacb3fdbbbe80ff5b99`.
The fresh package at `artifacts/alpha-maintenance-final-20261106/application`
has 22,088 entries and manifest SHA-256
`6ee4eb9bb5c83de02f60cdea06ecf918e33c50d4baf5a3da2c39a01b2e7da271`.
It contains no active maintenance marker. Relative to the initial cb92 trial,
only the native host DLL and WindowsHost DLL/EXE changed. The original trial
package and its evidence remain unchanged.

Exact-package marked Native80 passed in 34,251 ms. The missing-override launch
was refused; protected readiness, owner stop, exact C1 policy, rebased Knowledge
path, preserved sentinel and all four full input/copy manifests passed. Raw
report: `local-data/guarded-final-native-20261106/report.json`.

Full real package smoke passed protected startup, both real MCP probes on
synthetic files, restart and state preservation. No provider or PLC action.
The final EXE was built and its complete embedded 22,088-file inventory verified,
without invoking Main or installing it. It is 236,380,160 bytes, SHA-256
`e2453d084198bc33d77b2aa8df9fc82e6b45f238ca672f396941fe9b71679590`, under
`artifacts/alpha-maintenance-final-20261106/installer/`.
Logs: `local-data/maintenance-final-smoke-20261106.log` and
`local-data/maintenance-final-installer-20261106.log`.
The [final machine-readable evidence](evidence/native-maintenance-final-20261106.json)
keeps the source and artifact identities separate from the initial trial.

Raw native build log: `local-data/maintenance-final-native-build-20261106.log`.
Fresh guard evidence:
`local-data/maintenance-guard-a69688bc8c0a45b3bca4c4c3d8978d88/result.json`;
private-contract evidence:
`local-data/private-host-config-0ff932866579458c8a1ea0e7492cbe1f/result.json`.

The older producer preparation `02194d7` is bound to b38/6dbb only. A producer
for this new native guard requires a separately reviewed final source/manifest
baseline. No runner or CI producer has been started for the guard candidate.
