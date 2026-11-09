# Guarded producer preparation, 2026-11-06

Preparation only. No CI-branch push, dispatch, runner registration or host change.
This supersedes the b38 producer constants while retaining their historical
evidence. The active import accepts only application manifest
`6ee4eb9bb5c83de02f60cdea06ecf918e33c50d4baf5a3da2c39a01b2e7da271`,
Windows x64, version `0.2.0-preview.2`, 22,088 files. Native/upstream/package inputs
must match clean source `b0bf7c2ab18e6a6ff0daaeacb3fdbbbe80ff5b99` exactly.

The exact DLL is
`77e03ab221db44cd2a4eb442fa778f22de3da565940272312e540d85bac10ffd`.
The [native/package/installer record](MAINTENANCE_GUARD_BUILD_20261106.md)
contains its compiled Native80, marker, policy and inventory evidence. An
independent review of source `b46a2df` found no blockers in the native guard and
committed plan scope. It explicitly did not approve the pending coordinator,
installed UI or a real global swap.

The positive local import passed complete input and output inventory checks.
It refreshes tracked Core files from the source checkout, adds only
`ci-input-provenance.json`, and enables the required packaged integrity marker.
Its local provenance names the preparation checkout's source HEAD; an actual
job must independently supply its checked `GITHUB_SHA`. The old b38 input was
refused before output-directory creation. JavaScript syntax, Windows PowerShell
parsing, runner/workflow label agreement and diff checks passed.

Unique upload-job label: `alpha-guard-20261106-73a94c62`.
Ephemeral runner name: `alpha-guard-73a94c62`.
The runner launcher still checks the exact queued push run, branch, workflow,
SHA and uniquely labelled job, then enforces its pre-step run/SHA guard in a
clean environment. It does not install a service or touch an IDE.

Raw preparation logs are `local-data/ci-guard-import-20261106.log` and
`local-data/ci-guard-old-refusal-20261106.log`. Output is under
`artifacts/ci-guard-preparation-20261106/application`. The compact
[evidence file](evidence/guarded-producer-preparation-20261106.json) records its
manifest hash and script hashes. This is not proof of an actual hosted producer
run. Remote runner count was zero after these preparation checks.

The normal source branch's [portable/source CI at b46a2df](https://github.com/Auda29/twincat-assistant/actions/runs/42199003316)
passed. PR #15's CI branch still needs a deliberate source/producer update and
its own actual installer run. Real host/UI acceptance remains open. Maintenance
entry stays blocked while the conservative process preflight finds the Codex
controller runtimes; no allowlist or process termination is introduced here.
