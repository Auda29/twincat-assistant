# Alpha CI and coordinated host handoff, 2026-11-05

This record covers roadmap item 3. It does not close real-host acceptance.
No IDE, PLC, ADS, compiler or runtime operation on an engineering project is
performed by this workflow. The installer fixtures are synthetic only.

## CI input and runner boundary

GitHub read-only inspection found public repository Auda29/twincat-assistant,
default branch main, Actions enabled and no registered self-hosted runners.
The existing workflow consumed an artifact with no producer. The replacement
push workflow on ci/alpha-artifact-20261105 produces that artifact before the
hosted windows-2022 build. It needs no default-branch workflow change or release.

The one-job local producer imports the manifest-verified real application from
ALPHA_DELIVERY_BUILD.md. Its exact manifest SHA-256 is
3c261754d608232077d087df9aca0fa909382789e1a0f8de7d69e96b3068a294.
The source baseline is cebd35a46daa0cf457f443af7008a4a1f8142ced.
Native, upstream and dependency inputs must be unchanged from that baseline;
Core modules are refreshed from the CI checkout and re-inventoried. Embedded
package integrity enforcement is retained. No user profile, corpus or credentials
are imported. The host's local Beckhoff interop is absent from the package.
The source runtime receipt, both MCP manifests/provenances and notices remain
included. Native host compilation originally required the installed TwinCAT
4024.78 reference; this run does not pretend the hosted runner rebuilt that DLL.

Start-AlphaArtifactRunner.ps1 checks the queued run, branch, workflow path, SHA,
push event and unique upload-job label before registering. The runner has no
default labels, runs once with ephemeral registration, and has a pre-step hook
rejecting a different SHA/run/repository/job/event. The process environment has
an explicit platform-only allowlist and isolated HOME/TEMP. No service is
installed. Registration is removed on completion or failure. Remove only the
resolved local-data/ci-runner subtree after the listener exits and registration
is absent; it contains transient runner credentials. No backup is a CI input.

The hosted job checks synthetic install/update/rollback/failure recovery and
prerequisites, starts the real package with both MCPs and synthetic state,
builds the real EXE and checks every embedded payload hash. Artifacts are short
lived candidates, unsigned and not host-accepted releases.

## Host handoff, requires Parent coordination

The Parent holds a verified local backup at
local-data/alpha-host-baseline-20261105-140304/original-extension, with all 13
sizes and hashes in baseline.json. The prototype DLL SHA-256 is
e3d1226cf26d6e3f43c1595016928a7e0f675c5232f424394ef87df5a5361f00;
the old extension manifest SHA-256 is
c8a4cd897d7ec8db23a0ae547141b6a6eafbeb2091b02314c9d469ad855b83c2.
That backup includes proprietary local interop and must never be uploaded.
The baseline records PID7768. Process absence must be rechecked immediately
before mutation; an earlier empty process listing is insufficient.

The source is Program Files (x86)/Beckhoff/TcXaeShell/Common7/IDE/Extensions/
TwinCatAssistant.Native.X86. Users have ReadAndExecute and the Parent token is
not elevated. No ACL changes are needed or authorized. The operator must use
an elevated PowerShell for moves and installer execution. Keep the same Windows
user identity, do not launch the IDE elevated or under a different user.

1. Parent coordinates closing only the test/user Shell normally. Unsaved user
   work is handled by its owner, never saved or discarded by automation.
2. Before any new runtime starts, prepare a separate synthetic native state and
   host configuration pointing to it, using the existing documented native
   configuration mechanism. Confirm exact selected state/configuration in
   Diagnose. Keep the production host config bytes and restore them afterwards.
   NativeChatConfiguration.LoadFrom retains StateRoot from that JSON even when
   an installed selector replaces executable paths. Set McpSettingsPath to the
   candidate's runtime/mcp/default-settings.json for the isolated test, rather
   than reusing an operator corpus/MCP configuration. Native credentials stay
   untouched; binding keys include StateRoot. Use new synthetic sessions only,
   so legacy bindings cannot be adopted by a reused session id.
   This isolation is mandatory for returning to unmanaged 0.1.9 with this plan.
   Do not point the new runtime at the existing operator profile. If that cannot
   be established, stop before launch and design a selective configuration
   backup/restore with the Parent. A whole-profile restore would lose new chats.
3. Reverify every source and backup file against baseline.json, exact inventories
   including no links, and live inventory for tcxaeshell-x86-tc4024,
   TwinCAT3.1.4024.78, x86, .NET8, WebView2 and local interop identity. Save the
   old cachemarker bytes and timestamp. Hash the candidate and compare the CI
   SHA256.txt before running it. Record synthetic config/chat/login/binding
   sentinels outside product directories before every stage.
4. With Parent coordination and elevation, move the existing unowned extension
   to a fresh path outside Extensions on the same volume, for example
   <ShellRoot>/alpha-prototype-0.1.9-backup. Retain the Parent's independent
   verified backup too. Never adopt it into the installer's version store.
   The installer deliberately refuses overwriting it in place.
5. Run the first candidate below with an initially absent, dedicated install root.
   Verify one discoverable payload, application manifest and selector, then start
   the IDE normally with the explicitly isolated synthetic state. Perform the
   live acceptance steps in HOST_ACCEPTANCE_MATRIX.md and record observations.
6. A real update needs a second honestly versioned built candidate containing
   the same recovery support and compatible prompt policies. No such second
   real candidate is asserted here. Set product, VSIX and assembly/file versions
   consistently and rebuild the actual final changes. Merely changing the
   manifest version is not an update acceptance candidate. If the final prompt
   changes from C0 to C1, a new runtime accepting C0 does not make the old runtime
   accept C1. Freeze a baseline knowing both exact policies, or provide a tested
   bounded downgrade before any live rollback claim. Install the second build to the same root, record
   state.json current/previous, repeat the startup and sentinel checks. Invoke
   Rollback below with the IDE closed; inspect the previous selector and files,
   then start that previous runtime and recheck every sentinel. Fixtures do not
   satisfy this gate.
7. To return to unmanaged 0.1.9, close the isolated test IDE normally. Verify the
   managed extension against its retained snapshot, move it outside Extensions,
   and restore the original same-volume prototype folder. Restore the exact
   original host configuration and cachemarker bytes/timestamp. Verify all 13
   baseline hashes, restart the prototype normally and confirm the original
   operator chats/logins/bindings/configuration. Keep all managed version and
   synthetic-state directories for investigation. Do not delete operator data.
   Restoring only the old DLLs after migrating an operator profile is NOT a
   supported rollback because 0.1.9 may reject the new prompts/package paths.

Exact installer invocation after the above coordinated preparation:

```powershell
& $verifiedSetup -Action Install -InstallRoot 'C:\Program Files\TwinCatAssistant-Alpha' -HostId 'tcxaeshell:TcXaeShell\15.0'
# After a second real candidate was installed and accepted, IDE closed:
& $verifiedSetup -Action Rollback -InstallRoot 'C:\Program Files\TwinCatAssistant-Alpha' -HostId 'tcxaeshell:TcXaeShell\15.0'
```

$verifiedSetup is the absolute CI-downloaded EXE whose SHA-256 matched the
candidate's SHA256.txt. Do not substitute the earlier local EXE for the new
journal-capable candidate. Any transaction.pending blocks further operations:
retain its referenced old/stage/version paths, compare against state.json and
manifests with the host closed, and coordinate manual recovery. Do not guess
which folder to delete. Managed profile journals recover on inspection; a stale
model-consent.lock requires all profile writers to be stopped before removing
that single lock. Tampered or mixed unknown file contents remain refused.

External gates remain the coordinated elevation/migration, isolated state
selection, a genuinely distinct real update candidate, and live install/update/
rollback observations. No host acceptance is derived from the tests here.

## Exact isolated state preparation for the Parent

Run as the original user with all Shells closed, immediately before the
coordinated first launch. This changes the shared native setup, so it is a
Parent-operated step, not an action performed by this worker. Save the original
bytes with FileMode.CreateNew and record their digest privately. Do not output
the configuration body. Replace $selectedApplication with the manifest-verified
installed versions/<version>/application directory.

```powershell
$nativeConfig = Join-Path $env:LOCALAPPDATA 'TwinCatAssistant/native-chat.json'
$backup = Join-Path $privateEvidence 'native-chat.original.json'
$bytes = [IO.File]::ReadAllBytes($nativeConfig)
$file = [IO.File]::Open($backup, [IO.FileMode]::CreateNew)
try { $file.Write($bytes, 0, $bytes.Length); $file.Flush() } finally { $file.Dispose() }
$config = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
$config.StateRoot = Join-Path $env:LOCALAPPDATA 'TwinCatAssistant/alpha-synthetic-20261105'
if (Test-Path -LiteralPath $config.StateRoot) { throw 'Use a new empty synthetic state name' }
$config | Add-Member -NotePropertyName McpSettingsPath -NotePropertyValue (Join-Path $selectedApplication 'runtime/mcp/default-settings.json') -Force
[IO.File]::WriteAllText($nativeConfig, ($config | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
# After all test Shells are closed and the prototype payload was restored:
[IO.File]::WriteAllBytes($nativeConfig, [IO.File]::ReadAllBytes($backup))
if ((Get-FileHash $nativeConfig).Hash -cne (Get-FileHash $backup).Hash) { throw 'Native configuration restore failed' }
```

For the managed update and rollback, point McpSettingsPath at the currently
selected application before starting the IDE, with the same isolated StateRoot.
Retain the synthetic profile across these two transitions to test relocation,
chat, login and binding persistence. Do not restore a whole operator directory.
Record exact file inventories/digests of the untouched operator profile and
native credentials/bindings before/after, privately, without logging contents.

## Verification evidence

- Source commit fca292b7da473125edac3d17af7974eb32a75f1a implements the
  producer/workflow/profile journal. Commit 46f96a17697128721c6c1651a8a0f09dc2297d43
  fixes checkout fetch depth so the recorded native/dependency baseline exists.
- The first actual run 42033278140 failed closed before upload because the
  shallow checkout lacked that baseline. No fixture artifact was substituted.
- [Actual corrected CI run](https://github.com/Auda29/twincat-assistant/actions/runs/42034006930),
  head 46f96a17697128721c6c1651a8a0f09dc2297d43, push event. Producer job
  127267311112 passed exact import, source comparison, manifest verification,
  archive creation and upload. Artifact 13417617143 reviewed-application-input
  is 509727101 bytes; GitHub artifact ZIP digest is
  sha256:ab6ff4cfec364ffc15a0887dfb77e0a6ef514b2ca885a7e831488c73de8b2fb5.
  This is the archive digest, not the application manifest digest.
- The hosted windows-2022 job 127269994875 passed installer/prerequisite fixtures
  and the real application's protected startup, both actual MCP probes,
  preserved restart state and unchanged manifest checks. The full run completed
  successfully, including EXE build, embedded inventory/hash inspection and
  upload. Candidate artifact 13418347605 contains three files and is
  518586701 bytes, GitHub ZIP digest
  sha256:1d62ed49619ac6d9debb61e6b02f420a39f5f26ff0af9b7751c1235763c73ef1.
  That ZIP digest is not the executable digest. The executable digest is
  retained in its accompanying SHA256.txt.
- node --test --test-concurrency=1 tests/*.test.mjs: 134/134 passed, zero skipped,
  166.12 seconds. The added fifth recovery test actually kills its synthetic
  writer, confirms lock refusal, establishes writer exit, removes only its
  fixture lock and confirms journal recovery with the chat sentinel unchanged.
- TC_TEST_DOTNET_ROOT set to C:/Program Files/dotnet, then
  node --test tests/integration/application-package.test.mjs: 3/3 passed, zero
  skipped, 322.85 seconds. Synthetic relocated launch and V1/V2/V1 profile/project
  rebasing are evidence for those code paths, not real host acceptance.
- Test-InstallerFixture.ps1 and Test-InstallerPrerequisites.ps1 passed locally.
  They verify synthetic transactions/failure restoration and .NET8 acceptance
  with newer-major-only/missing-runtime refusal, without system installation.
- Under concurrent local workload the initial full suite had 132/133 pass and
  one existing Windows process-fixture readiness timeout. Its targeted rerun
  passed 8/8; the subsequent full serial suite above passed. A separate local
  full-package startup probe exceeded its 90 second readiness bound. This local
  result remains a failure; the independently successful hosted real-package
  smoke is not represented as a local pass.
- Read-only live inventory at 2026-11-05T13:24:28Z remained alpha-target/eligible
  for the intended Shell and unaccepted/refuse for VS2019. It recorded running
  Shell PIDs 7768 and 30380, so no deployment was attempted. The private JSON is
  local-data/ci-handoff-host-inventory.json with SHA-256
  6e9c69478f0f86c478ebfa1e4670f82ba220013f4905f806e51d5a56ee8ba965.
- Runner 2.338.2 ZIP was checked against its official asset digest
  a1f17aa987fc8158bc7521d284912267a0d01b7c2c4684b33c86783b5e8221cc.
  Both one-job listeners exited; GitHub subsequently reported zero runners.
  Ephemeral registration files .runner/.credentials/.credentials_rsaparams are
  absent in both directories. Recursive deletion of the two ignored runner
  work directories was rejected by the execution approval layer, including an
  absolute literal in-workspace target, without a detailed reason. No deletion
  through another shell or API was attempted. The remaining inert directories
  local-data/ci-runner and local-data/ci-runner-retry need approved local cleanup.

## Remaining portable CI diagnostics and narrow correction

The installer run succeeded independently. Portable checks on the same PR head
were not all green. In run 42034006816, Linux tests 2 and 7 in
agent-prompts.test.mjs used Windows C:/ fixture paths and failed absolute-path
validation on Linux. This belongs to the prompt worker's fixture update; no
prompt text or permissions were changed here. Windows lifecycle had one
30 second compiler-fixture timeout and a 15 second Knowledge-installer subprocess
timeout under concurrent load. managed-relocation.test.mjs additionally used
the hosted TEMP 8.3 alias RUNNER~1, correctly refused by the strict realpath guard.

The local follow-up commit serializes only the hosted Windows unit-test step
and supplies canonical runner.temp as TEMP/TMP for that step. It neither
relaxes path validation nor increases deadlines. Full local serial execution
passed 134/134; the hosted correction itself needs the next coordinated run.
No new local producer is registered for a documentation/test-only follow-up.
The old restricted producer must be reviewed again for any final candidate
with changed native/upstream inputs; its baseline guard must not be bypassed.


Retrieve the actual first CI candidate without a release or another build:

```powershell
gh run download 42034006930 -n windows-package-candidate -D <fresh-private-directory>
Get-FileHash <fresh-private-directory>/TwinCAT-Assistant-0.1.0-dev.1-x64-setup.exe -Algorithm SHA256
Get-Content <fresh-private-directory>/SHA256.txt
```

Compare the two executable digests exactly before installation. Retain
ci-input-provenance.json alongside the EXE. Artifact expiry is deliberately
short, input3days and candidate7days; coordinate retrieval before expiry.
The CI logs themselves record successful embedded-payload verification. A
separate local download/EXE reinspection is not claimed until it completes.
