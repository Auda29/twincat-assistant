# Local maintenance coordinator: preparation only

This is a separately reviewed temporary replacement of the original extension,
not a parallel installation or an exception to the production installer.
No live plan, real Enter/Restore, IDE, backend, elevation, registry/ACL change
or process termination was performed. GlobalDeploymentTestOverrideBlock and
the existing CI producer remain unchanged. The commands below are a handoff
requiring subsequent review and explicit operator approval.

## Exact candidate

The public module pins the final maintenance application manifest
`6ee4eb9bb5c83de02f60cdea06ecf918e33c50d4baf5a3da2c39a01b2e7da271`
and guarded native DLL
`77e03ab221db44cd2a4eb442fa778f22de3da565940272312e540d85bac10ffd`.
Parent built version 0.2.0-preview.2/x64/22,088 files from source
`b0bf7c2ab18e6a6ff0daaeacb3fdbbbe80ff5b99`; Native marker contract is
`c5790d675c0b4589627688e9de18a35452644b5f`. Preliminary a3/cb92 and older
b38 producer inputs are not adopted.

Parent separately reports exact marked Native READY in 34,251 ms, missing-
override refusal, C1 policy/rebase/history/owner-stop/four-manifest checks and
both-MCP/restart smoke. Its inspected, never-executed EXE is 236,380,160 bytes,
SHA-256 `e2453d084198bc33d77b2aa8df9fc82e6b45f238ca672f396941fe9b71679590`.
These are Parent evidence, not coordinator tests or real IDE/UI acceptance.

## Public surface and gates

`scripts/host-tests/Maintenance.psm1` exports only New-AssistantMaintenancePlan,
Enter-AssistantMaintenance and Restore-AssistantMaintenance. Importing it performs
no live collection or host writes. Public commands expose no supplied inventory,
process, SID or host replacement parameters. Synthetic tests call the internal
engine with temporary fixtures; public entry rejects synthetic plans before
live collection.

Future public execution collects live read-only inventory itself: exactly the
registered TcXaeShell15/x86/4024.78 target, matching DTE registration, and no other
discoverable Assistant payload. Search roots include inventory hosts' Extensions,
CommonExtensions/ShellExtensions, initialization registry search values and
private Beckhoff suffix extension directories. Unresolved macros refuse.
Reviewed search roots must remain unchanged.

Every write phase checks current SID, exact canonical private config path/hash,
StateRoot, system DotNetRoot, global configuration hash/timestamps and process
quiescence. Private config must already exist at
`%LOCALAPPDATA%\TwinCatAssistant\host-tests\<id>\native-chat.json`, with ASCII id
`[a-zA-Z0-9_-]{1,64}`. Only StateRoot (sibling `state`) and DotNetRoot (system
Program Files/dotnet) are permitted. The config must be regular and single-link;
only its profile/state/native-chat/WebView2 directory paths and ancestors are
checked as path metadata. User-data descendants remain opaque: no enumeration,
content read or hashing, including during restore. Package, backup and stage
still receive complete file/link validation. Inherited WebView path/argument
overrides refuse. No private/operator configuration, chats, logins, preferences
or bindings are created, migrated or restored by this coordinator.

All TcXaeShell/TcXaeShell64/devenv/node/opencode/WindowsHost/twincat-knowledge/
dotnet processes block entry and restore, including unreadable/ambiguous owners.
No kill, executable allowlist or PID exemption exists.

Concrete blocker reported by Parent: node PIDs 40096/4856/38716 are Codex
controller runtimes under OpenAI/Codex/runtimes/cua_node/78fb63cda1a9e2b2,
parent PID 19876 codex.EXE. Official REPL reset cleared bindings but left all
three alive. These checkpoint identities must be recollected, not trusted later.
They remain blockers even when no IDE/backend is running.

## Plan, marker and journal

Prepare writes only a fresh same-volume run under
`%LOCALAPPDATA%\TwinCatAssistant\host-maintenance\<run-id>`. Existing runs refuse.
Run/backup/stage/application must be outside every discovered search root, the
host root and any path segment named Extensions. Old pkgdef files are never left
under Extensions under a different name.

Plan schema (exact top-level fields):

```text
schemaVersion: integer 1; kind: assistant-maintenance-plan
mode: live | synthetic; runId; runRoot; operatorSid
configuration: path, sha256, stateRoot
host: root, extension, marker, globalConfiguration, searchRoots
source: root, manifestSha256, guardDllSha256
globalConfiguration: presence/hash/creation and last-write ticks
original: complete relative inventory, sizes/hashes/timestamps
hostMarker: presence/hash/creation and last-write ticks
selector; maintenanceMarker; candidate: complete closed-stage inventory
```

An independent verified backup contains every original payload file, including
valid existing selector/owner metadata and local interop. Unknown, malformed or
incomplete Assistant metadata refuses. The original configuration-change marker
is copied with bytes/timestamps. Global native config is hashed, not copied.
The application is copied and fully manifest-checked; it must contain no vendor
interop, VSIX archives or default maintenance marker. Only the local staged host
gets a copy of the original local interop.

Before publication the complete stage contains the host, existing four-field
selector and this exact flat five-field native marker:

```json
{"schemaVersion":1,"configurationPath":"<exact canonical private config>","configurationSha256":"<lowercase SHA256>","operatorSid":"<exact current SID>","selectorSha256":"<lowercase SHA256>"}
```

The selector points to the fully verified run application. Native public Load
validates its selection; no executable path is taken from operator config.
The coordinator publishes no markerless candidate and uses no C0 native DLL
for UI testing.

Enter/Restore require an independently reviewed hash of exact plan.json bytes.
Journal schema: `{schemaVersion:1, planSha256, phase, markerWriteTicks}`. Each
prospective phase is flushed and atomically replaced before its directory move:

```text
prepared -> parking-original -> original-parked -> publishing-candidate
 -> candidate-published -> refreshing-marker -> entered
 -> withdrawing-candidate -> candidate-withdrawn -> restoring-original
 -> original-restored -> restored
```

The actual original directory is renamed outside all search paths to run/original;
the complete closed stage is renamed to its CodeBase location. Only the existing
host configuration-change marker's journaled last-write time changes, not bytes.
Restore withdraws the candidate to run/withdrawn, renames the verified original
back and restores marker time. Original, backup and withdrawn files are retained.
File creation/last-write times are checked; last-access timestamps and new ACL
copying are not acceptance claims. The original directory rename retains its ACLs.

The existing production deployment kernel lock and persistent per-run lock
serialize cooperating writers. No age/PID-based stale reclamation occurs.
Unexpected lock content, a pending .writing, unknown journal or contradictory
phase/layout refuses. journal.json.previous is retained evidence, never guessed
as authority. Caught failures attempt restore under the same gates. A process
appearing during a transition also blocks recovery writes, preserving the journal
for later explicit recovery. Interrupted runs never auto-enter again.

## Cooperative maintenance limitation

The agreed condition is an explicitly supervised cooperative window with no
IDE/backend owners and an unchanged approved stage. Full hashes before/after
same-volume atomic renames detect changes; they do not exclude adversarial
same-user writers or an independently launched IDE. A new IDE/uncoordinated
writer violates the window: stop and retain evidence for reviewed recovery.
No race-free or kernel-wide exclusion guarantee is claimed. Removing the guard
or marker bypasses it by design, so nobody may modify the published host or run
package during the window.

## Offline operator procedure: not executed

1. Complete source/candidate/private-profile and central acceptance-plan review.
   Save trusted scripts and handoff so they remain usable without Codex. No live
   plan is generated in this task.
2. Close Codex/T3 regularly through its UI after preserving its work, plus all
   IDE/backend sessions. Never automatically save unrelated engineering data or
   kill controllers. REPL reset is insufficient. If controllers remain, stop.
3. In an independent same-user 64-bit Windows PowerShell, after separate approval,
   import the reviewed module and prepare using canonical Windows paths:

   ```powershell
   Import-Module <reviewed-checkout>\scripts\host-tests\Maintenance.psm1
   New-AssistantMaintenancePlan -RunId <fresh-id> -ConfigurationPath <private-config> -PackageRoot <final-package> -Acknowledgement PREPARE_ONLY_NO_HOST_WRITE
   ```

4. Review returned plan SHA, SID/root, complete original/backup inventories,
   search paths, selector, marker and timestamps. Obtain explicit approval for
   that concrete temporary global replacement. Same-user elevation, if protected
   host rights require it, is separately coordinated; no self-elevation or rights
   change is implemented.
5. Only after review/approval and continued quiescence:

   ```powershell
   Enter-AssistantMaintenance -PlanPath <reviewed-plan> -ExpectedPlanSha256 <reviewed-hash> -OperatorOptIn I_ACCEPT_TEMPORARY_GLOBAL_MAINTENANCE_AND_RESTORE
   ```

   The coordinator never starts an IDE/backend. A later separately authorized
   owned IDE test must inherit its exact private override; without it the marked
   DLL refuses. Use synthetic projects, preserving all operator state.
6. After normal owned IDE/backend exit and fresh quiescence, use
   Restore-AssistantMaintenance with the same plan/hash/opt-in. Verify all original
   files, config records, marker timestamps and latest private-state sentinels.
   Retain evidence; no broad cleanup is part of this procedure.

## Independent emergency restoration

Automated restore executes no candidate code and can withdraw an intact candidate
that cannot start. Changed config/stage/package/candidate/backup/SID/global config
makes it refuse. Do not clear overrides, delete locks/journals, force phases or
invoke internal production installer routines to bypass it.

Keep the host closed and obtain independent operator review. Verify the trusted
plan hash and complete original inventory against both backup and parked original.
Inspect journal and actual paths without executing the candidate. With all owners
quiescent, quarantine a changed candidate outside every search path on the same
volume; never leave it elsewhere under Extensions. Rename only the independently
verified parked original to its exact original path. If it is missing/changed,
stop: constructing a replacement from backup needs a new reviewed procedure,
including permission metadata. Independently reconcile marker backup bytes/time
and global-config hash; unexplained changes must not be overwritten blindly.
Private-config repair is a separately reviewed private-file operation. Never
restore whole profiles or discard new chats/logins/bindings. Reverify originals
before a normal IDE start. This manual path is not an implemented bypass.

## Synthetic verification

```powershell
powershell.exe -NoProfile -NonInteractive -File scripts/host-tests/Test-Maintenance.ps1 -EvidencePath <fresh-absolute-JSON-output>
```

Fixtures are fresh temporary trees with fake DLL bytes and synthetic state;
they are retained. The sanitized result under scripts/host-tests/evidence claims
only the transaction engine. No compiler, assembly load, actual backend,
provider/PLC/ADS/GUI, real registered-host action or UI acceptance is exercised.

The complete Windows PowerShell 5 run finished with exit 0: 13/13 groups,
including nine caught entry faults, nine entry interruption/recovery boundaries,
eight restore interruption/recovery boundaries, sixteen binding/process refusal
cases, late-owner refusal, tamper/partial-backup/link/metadata/lock checks and
public-surface rejection. See [sanitized evidence](../scripts/host-tests/evidence/maintenance-synthetic-20261106.json).
Earlier partial logs are not completion evidence. These results require a new
independent coordinator review; Native guard review does not cover this module.

## Review fix: private-state opacity

The original 13-group evidence applies to commit 6ab581a. Review identified an
excessive recursive profile inventory. Profile validation now reads only the
bounded config and necessary directory metadata. One targeted regression holds
a synthetic state-content file exclusively locked throughout prepare, enter and
restore; it passed with unchanged bytes. Use Test-Maintenance.ps1
-ProfileOpaqueOnly -EvidencePath <fresh-absolute-path> to repeat this case.
The separate evidence/maintenance-profile-opaque-20261106.json records this fix;
the original full-matrix evidence has not been rewritten as a new full run.
