# Integrated acceptance, 2026-11-06

The operator resumed Roadmap 1–4. All integration builds and tests run in a
separate checkout, `t3-83602adb`. The normal source checkout, original installed
prototype and operator configuration remain on the recovered working baseline.
This record does not declare an accepted release or close the installed UI gates.

## Integrated source and builds

- Native private process configuration: `85e45fd`.
- Completed operation-scoped package verification: `f9d7fde`, integrated as
  `3c5ea55`.
- P2 profile recovery/reference preflight: `f5fb18a`, integrated as `d0b8e1f`.
- Installer private-profile preflight and global-deployment refusal: `4116ecb`,
  integrated as `6dbb2ad`.
- Pinned Core executable reused read-only after hash validation. Chat UI and
  EmbeddedChat were rebuilt in this checkout with a fresh source-runtime receipt.
  This is not a claim that Core was recompiled today.
- Windows helper, read-only probe and x86 native host rebuilt with SDK 8.0.425
  and the existing local TwinCAT interop reference. No interop redistribution.
  The native build has the same six existing VSTHRD010 warnings.
- Rebuilt host DLL SHA-256:
  `0e40d2e8d7a38cf9f3128a8be8efa22b8f92d88b519adecef6b5de4fb6ca83c4`.

## Completed integration checks

| Check | Result |
| --- | --- |
| Serial Node suite | 159/159 passed, zero skips, 216.28 s |
| Native policy/context/chat regressions | 151 passed |
| Private native configuration | 29 passed on the rebuilt DLL |
| Installer profile preflight, Windows PowerShell 5 | passed |
| Synthetic installer registration/update/rollback/recovery/interop | passed |
| Installer runtime prerequisites | passed |
| Full real package build | passed |
| Protected package startup and delivery route exclusions | passed |
| Both real MCPs against synthetic inputs | passed |
| Restart, synthetic state preservation, package unchanged | passed |
| New DLL/private process configuration/C0 migration within Native80 | passed, 35,456 ms |
| Final executable build and embedded inventory | passed, all 22,088 files |
| Hosted portable/source CI at `c1f080e` | passed, all three jobs |
| Installed IDE/UI/member/paste/first-after-logon | open |

Local raw logs under this checkout's ignored `local-data`:

- `integrated-native-build-20261106.log`
- `integrated-node-tests-20261106.log`
- `integrated-installer-fixture-20261106.log`
- `integrated-package-build-20261106.log`
- `integrated-package-smoke-20261106.log`
- `integrated-installer-build-20261106.log`
- `private-host-config-c6ee7ff6217d42f1b2d321048b3348dd/result.json`

The complete package is under
`artifacts/alpha-integrated-20261106/application`. The independently verified
Knowledge and Engineering inputs were copied read-only from the existing input
store, then their complete manifests verified in the integration checkout.
Knowledge remains 0.1.1; Engineering remains pinned revision `3c303837e6d6f72bec1b9d38e778a7c32c61ae5c`.

The application manifest has 22,088 entries, version `0.2.0-preview.2`, Windows
x64. Its SHA-256 is
`b38c2e5d20b1b1084934eeba80dca09cfdf26aa35e2742756e47baf503ef4354`.

The local EXE is
`artifacts/alpha-integrated-20261106/installer/TwinCAT-Assistant-0.2.0-preview.2-x64-setup.exe`,
236,379,648 bytes, SHA-256
`6ccdd89d3917ad98c9e1c9034ee234613df1298450edde17a680e3d49cee9d10`.
Its embedded inventory inspection passed without invoking Main. It has not been
installed or executed. A compact, path-redacted
[machine-readable record](evidence/integrated-acceptance-20261106.json) records
the exact package, DLL, fixture and harness hashes.

## Combined private native startup

`Measure-PrivatePackagedNative.mjs` copies the exact first C0 package and this
complete new package into a synthetic versioned installation. It does not patch
or re-version either package. A fresh private profile uses the process override;
the x86 fixture calls the rebuilt DLL's public `NativeChatConfiguration.Load()`
with its selector beside the copied assembly, then its actual `StartAsync()`.
No IDE or installed extension is invoked.

The compiled 80000-ms deadline remains present. Protected readiness succeeded
in 35,456 ms, followed by successful owner stop. The C0 global Knowledge profile
migrated to exact C1 configuration and current packaged paths; the synthetic
history sentinel survived. Both original packages and both copied packages
passed full inventory verification afterward. This is one warm-filesystem,
outside-IDE migration result, not first-after-logon or a project/UI acceptance.

Raw result:
`local-data/combined-private-native-20261106/retry-dd3a6970-3f6a-4223-8702-6e0b5d656d03/report.json`.
The first attempt failed in the test fixture before backend startup because its
32-bit PowerShell could not resolve `Get-FileHash`. The fixture now hashes via
.NET. The retry revalidated the exact staging manifests, used a new private
profile and retained the original failure log. `--reuse-staging` always creates
a separate result directory and fresh copied host/profile; it never accepts a
different package manifest.

Reproduction, with reviewed input paths and a fresh output name:

```powershell
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'
node scripts/Measure-PrivatePackagedNative.mjs <first-C0-package> <current-package> local-data/<fresh-run>
```

## Why a fresh Shell suffix is insufficient

The discovery launch used exactly one owned suffix Shell and exited normally.
Its ActivityLog confirms a private per-profile extension search directory, but
also imports and enables the global prototype. The private registry's effective
Package CodeBase still points to the global original DLL. The candidate shares
the prototype's VSIX identity, Package GUID and ToolWindow GUID. Neither exclusion
nor safe precedence has been demonstrated. Installing another copy there would
not establish isolation.

Consequently the installer validates the private data contract, then refuses
registered-host deployment with that override before inventory or install-state
creation. It does not rewrite global `native-chat.json`. A different local user
alone would not exclude the global extension.

The operator has only the current workstation. Read-only inspection found no
Windows Sandbox executable or Hyper-V management module. VMware is installed,
with inventory entries labelled TC2 and 4026; neither proves an available clean
4024.78/x86 test host. No VM was started, changed or provisioned. Official native
Computer Use also remains unavailable, reporting `native pipe unavailable`,
OS error 2.

## Remaining completion gates

Hosted [run 42190617865](https://github.com/Auda29/twincat-assistant/actions/runs/42190617865)
has since passed all three jobs for `c1f080e`: Linux components, Windows lifecycle
and source runtime. This is the b38 integration source, not the subsequent native
maintenance guard or a new Windows installer artifact run.

The producer preparation in `02194d7` is bound to this exact b38 package and
`6dbb2ad`, with a passed import and old-input refusal. It is unpushed and has not
run in CI. It must continue to refuse the subsequently changed native guard.
The fresh real-package policy regression evidence in `8ee4012`, integrated as
`d5715d4`, passes 12 groups including nine P2 negative cases. Its frozen old DLL
is explicitly not evidence for the rebuilt native DLL tested here.

The separate [local maintenance proposal](LOCAL_MAINTENANCE_ACCEPTANCE_PLAN.md)
now prepares a guarded local UI test on the workstation. The native guard is
implemented in `c5790d6`, with 65 synthetic guard checks and 29 private profile
checks. That change needs its own final package identity, startup test and
reviewed maintenance coordinator before any global swap. This b38 record remains
the evidence for the earlier integrated package; do not conflate the candidates.

For real host acceptance, use a demonstrated isolated extension installation,
or a clean licensed test VM on this workstation. Do not replace the working
prototype to bypass the isolation failure. Visible docking/focus/WebView,
member editors, actual TwinCAT paste, installed update/rollback and an actual
first-after-logon remain separate gates. No provider, PLC, ADS or customer
project action was performed in this integration round.
