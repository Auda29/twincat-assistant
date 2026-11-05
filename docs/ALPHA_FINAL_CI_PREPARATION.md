# Final alpha CI preparation, 2026-11-05

Prepared only. No push, GitHub workflow dispatch, runner registration or host
mutation is authorized by this preparation record. Parent must first confirm
the final local package smoke and the final prompt source/test commit, then
explicitly release the CI push/start.

## Reviewed input

- Application version: 0.2.0-preview.2, Windows x64, 22,085 manifest files.
- Native host AssemblyVersion/FileVersion and VSIX version: 0.2.0.2.
- Parent source basis: d5a227ac072891bfeb6eb67c40e842e9d2c6346e.
- Application manifest SHA-256:
  12f0827fe13cfb1bdd18c54b56f9576b178de6d4c91803f339cf699ec5b03657.
- Read-only input in the main workspace:
  artifacts/alpha-final-20261105/application.
- New runner label: alpha-preview2-20261105-5b8ed103.
- New ephemeral runner name: alpha-preview2-5b8ed103.

The Parent source was merged, not cherry-picked again. Immediately after the
merge, git diff against that parent for src, native, upstream, package.json
and package-lock.json was empty. The package is an actual source/UI and native
rebuild with final integration changes. It is not the first CI candidate with
a different manifest version.

The importer still compares native, upstream and dependency manifests against
the exact reviewed parent commit, validates the entire original package and
refuses vendor interop and opaque VSIX files. The changed manifest, version,
architecture and file count are exact one-candidate constraints. Only tracked
Core files are refreshed from the final CI checkout according to the existing
contract, with package integrity enforcement re-enabled. The required source
integrity marker must occur exactly once; an unfamiliar contract is refused.

A future prompt-only src/test commit may be merged before release and supplied
by this refresh. A future native, upstream, package or dependency change requires
a new reviewed build and producer baseline; do not bypass the existing guard.
Both rollback endpoints must recognize the precise policies they encounter.
This preparation does not establish compatibility with an unknown later prompt.

## Pre-push validation

The previous delivery-20261105 input is rejected as unreviewed before creating
its output directory. The new input's manifest digest exactly matches the
provided reviewed digest. Real import/staging and its final inventory check are
recorded in the follow-up evidence after completion. The prepared local copy
is test output only and will not substitute for the final approved CI SHA.

## Coordinated next steps

1. Parent confirms local real-package smoke and supplies the final prompt
   commit. Merge that source/test commit and verify the resulting native,
   upstream and dependency trees remain identical to the reviewed baseline.
2. Record the final reviewed SHA. Push PR15's existing isolated branch and
   immediately link https://github.com/Auda29/twincat-assistant/pull/15.
3. Inspect the actual queued push workflow for that exact SHA. The runner
   script must match the branch, workflow path, push event and new unique label.
   Keep the sanitized environment, ephemeral/one-job mode, no default labels,
   exact pre-step run/SHA/job guard and post-run deregistration.
4. Start only after those checks and Parent release, using a fresh runner
   directory. Never reuse old credentials or install a service.
5. Record the new real hosted installer run, artifact IDs, hashes and any
   failures honestly. No gate from the first CI run transfers automatically.

Prepared command, deliberately not executed:

```powershell
./scripts/Start-AlphaArtifactRunner.ps1 -ExpectedSha <approved-final-sha> -RunId <verified-queued-run> -Application 'C:/Users/NiklasW/Documents/AllgemeineDokumente/Privat/Code/twincat-assistant/artifacts/alpha-final-20261105/application' -RunnerDirectory local-data/ci-runner-preview2
```

Real installation, elevation, native configuration and extension/cachemarker
changes remain under the Parent's host handoff. This worker does not use the
reported zero-Shell snapshot as authorization to mutate that host.

Preparation validation completed successfully:

- Full real-package import from the reviewed final input completed with
  'Reviewed application input staged and manifest verified'. It checked every
  input and output inventory entry; the staged copy remains ignored test output.
- The old reviewed delivery input failed its hash gate and created no output.
- Read-only native metadata inspection found AssemblyVersion, FileVersion and
  VSIX identity version all equal to 0.2.0.2.
- JavaScript syntax check, PowerShell parser check, exact source-tree parity
  and workflow/runner unique-label agreement passed. git diff --check passed.
- No smoke, host acceptance or CI run is claimed for this preparation. The
  Parent's pending smoke and final prompt commit remain the release condition.
