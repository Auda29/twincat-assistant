# Integrated producer preparation, 2026-11-06

Preparation only: no push, workflow dispatch, runner registration, executable
execution or registered-host deployment. This supersedes the November 5
producer baseline. Parent release is still required before any CI action.

## Exact reviewed input

- Implementation baseline: `6dbb2ade20777f1e666c36bd8ccf7e0dced023ab`.
- Parent checkpoint: `c1f080eb5d1860d751b7d4d9bba467870ff9461b`, merged here.
  Its changes after the implementation baseline are documentation/evidence;
  `src`, `native`, `upstream`, `package.json` and `package-lock.json` are identical.
- Input: integration checkout `t3-83602adb`,
  `artifacts/alpha-integrated-20261106/application`, used read-only.
- Manifest SHA-256:
  `b38c2e5d20b1b1084934eeba80dca09cfdf26aa35e2742756e47baf503ef4354`.
- Version `0.2.0-preview.2`, Windows x64, 22,088 manifest entries.
- Rebuilt native DLL SHA-256:
  `0e40d2e8d7a38cf9f3128a8be8efa22b8f92d88b519adecef6b5de4fb6ca83c4`.
- Unique producer label: `alpha-integrated-20261106-82f6d9c1`.
- Ephemeral runner name: `alpha-integrated-82f6d9c1`.

The manifest, identity and native/upstream/dependency source guards remain exact.
No version relabeling, automatic input adoption or guard exception is involved.
The native x86 DLL depends on a local TwinCAT 4024.78 build with a licensed local
interop reference. Hosted Windows does not reproduce that native build without
this dependency. The manifest-checked distribution excludes vendor interop and
opaque VSIX archives; runtime/MCP provenance and notices remain in the input.
Parent's exact build, public Native load, both-MCP smoke and embedded-EXE inventory
are recorded in [integrated acceptance](INTEGRATED_ACCEPTANCE_20261106.md).
They are local evidence, not a completed hosted CI run or installed UI acceptance.

## Local import proof

The importer verifies the full input inventory before creating a new output,
refreshes only tracked `src` from the checked-out CI source, requires exactly
one integrity-enforcement marker, adds provenance, creates a new manifest and
verifies the complete output inventory. The probe uses an ignored local output;
it is not uploaded or substituted for a future reviewed CI SHA.

The [sanitized probe record](evidence/final-producer-preparation-20261106.json)
records the input and output hashes, file counts, source parity, parser checks,
and old-candidate rejection before output creation. The successful probe imported 22,088 entries and verified 22,089 output entries;
the only added file is `ci-input-provenance.json`. Three refreshed Core files
(`profile-update.mjs`, `rollback-policy.mjs`, `state.mjs`) differ only in local
CRLF versus input LF. Every tracked input and output Core source matches the
canonical reviewed Git baseline after LF normalization and the required integrity
marker. Native, upstream and dependency payloads are unchanged. The local probe
manifest is `5dc12b324919fab448e29f3e66bc63bb84606c01cf186642ddbd6cb26424b10e`;
a future CI SHA changes provenance and can produce a different output manifest.
Every later invocation repeats the full verification. Any native, upstream or dependency change requires
a newly reviewed package and baseline, rather than weakening this guard.

[Fresh rollback evidence](evidence/policy-rollback-p2-20261106.json) separately
contains 12 successful real-package Node-policy groups, including all nine P2
reference refusals with unchanged configuration/settings/journal bytes. It uses
actual frozen runtime/MCP/native components and current byte-equivalent Core;
it does not claim the new Native85 DLL was exercised by that harness. Parent's
combined Native85 evidence remains separate. The current C1 EXE is required for
the C1-to-C0 rollback bridge; the immutable first C0 EXE cannot secure that downgrade.

## Future coordinated CI action

After Parent releases the exact final SHA, update PR15's existing branch and
immediately link the PR. Inspect the actual queued push run before registering
anything. `Start-AlphaArtifactRunner.ps1` must confirm repository, SHA, push
event, branch, workflow path, queued state and the single `reviewed-input` job's
unique label. Its job-start hook checks the exact SHA, repository, event, run
and job before steps. Keep `--no-default-labels`, ephemeral one-job mode,
sanitized process environment and home, no service, and post-run deregistration.
Use a fresh runner directory; never reuse old registration credentials.

Prepared command, not executed:

```powershell
./scripts/Start-AlphaArtifactRunner.ps1 -ExpectedSha <approved-final-sha> -RunId <verified-queued-push-run> -Application 'C:/Users/NiklasW/.t3/worktrees/twincat-assistant/t3-83602adb/artifacts/alpha-integrated-20261106/application' -RunnerDirectory local-data/ci-runner-integrated-82f6d9c1
```

The hosted package job performs installer/prerequisite/private-profile fixture
checks, actual protected package startup with both MCPs and preserved restart
state, then builds and inspects the executable before its short-lived upload.
Its installer scripts come from that same final CI checkout, including the P2
recovery/reference preflight and private test-profile refusal. No new hosted
result is claimed until this exact flow actually completes.

## Host boundary

Keep `GlobalDeploymentTestOverrideBlock`: the process-only private configuration
contract at Native `85e45fd` isolates data, but the discovered suffix Shell still
loads the global prototype. No private extension precedence/exclusion has been
proved. The installer validates the override before any directory creation,
inventory, recovery or file swap, then refuses registered-host deployment.
Neither global `native-chat.json` nor operator state is changed by this preparation.
No further Shell or VM is started. Real install/update/rollback and UI gates remain
under Parent coordination and require a safe host strategy on the available
workstation; local synthetic tests do not close them.

Any later opt-in native maintenance/test guard is outside this baseline. It
requires a rebuilt, separately reviewed package and a new producer contract;
this importer must reject that package until its exact source and manifest
have been reviewed. No temporary localhost/host test is enabled by this commit.
