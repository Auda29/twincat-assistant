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
   real candidate is asserted here. Install it to the same root, record
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
