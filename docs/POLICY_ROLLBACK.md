# Policy-aware C1 to C0 rollback

The new C1 installer is the required rollback tool. The first C0 installer is
immutable and cannot safely downgrade a C1 profile. Do not use that old EXE for
the C1 to C0 transition. This bridge supports the actual first CI C0 contract,
not an arbitrary lower version or the unmanaged 0.1.9 prototype.

## Authority and target capability

The installed current C1 Node/runtime performs the conversion. Before it is
executed, the Windows transaction validates its entire package inventory. The
helper then verifies both full application manifests, distinct canonical owned
sibling directories, installer selection, fixed entrypoints and package markers.
The C0 target must additionally match seven explicit normalized UTF-8 source
SHA-256 hashes in rollback-policy.mjs. They identify the actual source files of
first CI commit 46f96a, including config generation, prompts, global/project
inspection and recovery. The current config generator's exact non-prompt source
body must match its reviewed hash too. A changed source with a freshly
recomputed manifest still has no supported capability. Version is not authority.
No old JavaScript module, launcher, MCP or operator-configured command is
executed during conversion. Old source is read only as bounded capability data.

The new C1 installation records the same Windows user's SID and selected native
StateRoot in state.json. Install/update reads only StateRoot from that user's
native-chat.json, or uses the documented default when the file is absent.
Executable paths and credentials from that configuration never reach the helper.
Rollback must use that same SID and StateRoot. Select the isolated acceptance
StateRoot before C1 install/update. Switching accounts for UAC or changing the
native StateRoot afterwards is refused, rather than silently skipping the real
profile. No ACL is changed to work around this boundary.

## Transaction and data preservation

The installer holds its existing host/install transaction locks and records
transaction.pending before preparing policy. It rechecks that the selected host
is closed. The current helper acquires launcher.lock, model-consent.lock and
all existing project locks in sorted order, then rechecks project inventory.
It validates every global/project endpoint before the first downgrade write.
Unknown prompts, modified permissions, foreign executable paths, malformed
receipts, unknown project directories and any existing project journal refuse.
A journal containing JSON null, false or 0 is still an existing journal and
cannot pass this preflight. No truthiness test grants authority.

Only exact known config templates are generated. Global settings retain their
current provider, model, disclosure consent and operator config paths. Packaged
Node/MCP paths select the checked target. The scope-specific C0 prompt comes
from the explicit reviewed C0 table, never Markdown replacement. Each project's
original workspace, identity, engineering.json and every non-policy file remain.
Global changes use the existing before/after two-file journal; project changes
use the existing project configuration/receipt journal. No whole-profile backup
is restored. Chats, authentication data, project bindings and history are not
rewritten or copied, including data created after the C1 update.

The helper reports policy-ready only after every pair is committed and all
per-pair journals are removed. It keeps the locks while waiting on the private
installer control pipe. The installer swaps the extension and commits selection
state before sending swap-complete. Only then does the helper release its locks.
A swap failure restores product files. The still-selected C1 runtime recognizes
the completed C0 policy and old paths and may upgrade them normally again.
Neither this fallback nor process termination authorizes starting C0 on a
C1-containing or unfinished journal.

## Crash recovery

An unconfirmed helper EOF is an error, not a completed rollback. Before a
completed switch, retain C1 as the only recovery runtime. A caught or interrupted
write may leave its existing profile/project journal. C1 profile inspection
validates and completes a known global journal. C1 native project preparation
validates and completes a known project journal; the rollback helper refuses
an outstanding project journal until this recovery has completed. Unknown or
modified journals remain refused. After a killed writer, establish that every
relevant writer has exited before manually removing its exact stale lock. Never
remove the journal to bypass validation.

transaction.pending records the host paths, target and selected StateRoot.
For power loss or a lost installer/helper, retain the referenced snapshots and
staging folders. With the host closed, compare actual files, state.json,
selector and manifests, then coordinate recovery with the Parent. Do not guess
which folder or journal to delete. If product selection was already committed
but helper acknowledgement failed, the installer retains transaction.pending
rather than silently declaring success.

## Supported scopes

All four global tool combinations have explicit C0 templates. The native
project path of the first C0 binary always generated the both-MCP project
prompt. A project lacking Knowledge is therefore an unsupported target scope,
even if C1 can represent a narrower prompt. It refuses before the global
profile is downgraded or product files are swapped. This bridge does not widen
that scope or invent capabilities for the old binary.

## Verification boundary

scripts/test-policy-rollback.mjs accepts the actual first CI package and a real
current C1 package, then creates only an isolated synthetic host, installation
and profile. Its C0 input manifest is pinned to the extracted first CI payload,
whose EXE SHA-256 is 3b8f9c9ccb09943310c080f44c5277b545e1da1f267f54d60be2ee5ce4482519.
It uses the installed local interop solely inside that synthetic transaction;
that DLL and all resulting local snapshots must never be uploaded. It performs
no registered-host installation, IDE attach, provider call or PLC operation.

Real-package results and failures are recorded in the private ignored evidence
file, with a sanitized committed summary after completion. No fixture or direct
configuration read closes the real registered-host install/update/rollback gate.

## Process-only host-test contract v1, 2026-11-06

Installer preflight matches native source 85e45fd. A process-only
TWINCAT_ASSISTANT_TEST_CONFIG must name the canonical single-link file
%LOCALAPPDATA%/TwinCatAssistant/host-tests/<id>/native-chat.json, with an ASCII
id of 1-64 letters, digits, underscore or hyphen. Its only fields are the exact
profile/state StateRoot and system ProgramW6432/dotnet DotNetRoot. Executable,
repository and MCP fields are refused. Missing, redirected, hardlinked, foreign
or malformed overrides never cause a global native-chat.json fallback read.
Existing profile files/directories are checked before any installation state is
created. Inherited WebView2 folder/argument overrides are refused, matching the
native validator. Resolver preflight is read-only; no global config is rewritten.

StateRoot/SID binding uses the validated process selection before Install or
Rollback. However, private data alone does not isolate the registered host's
extension directory. Until an explicitly isolated extension-search target is
implemented, Invoke-Installer refuses the test override before inventory or any
file swap. The resolver and synthetic transaction tests establish data-contract
checks, not an isolated real-host installation. Parent owns the discovery and
subsequent real host acceptance.

Read-only scope validation now precedes all global journal recovery writes.
Both journal endpoint configurations and their referenced MCP files/Engineering
scopes are checked under the existing lock. A valid live endpoint cannot hide
an unsafe non-live endpoint. readOnly:false, an implicit/non-file backend and
missing configuration each preserve config/settings/journal bytes on refusal.
The same shared checks apply to a newly written profile pair before its journal
is created. Historical frozen package evidence remains separate from this fix.

## New P2 evidence, 2026-11-06

[The new regression record](evidence/policy-rollback-p2-20261106.json) is a fresh
12-group run, not reuse of the archived 11 checks. Actual first CI C0 and real
frozen C1 components were used in a new synthetic host/profile. C1 Core came
from 4116ecb, including P2 and f9d7fde, with byte equality to parent 6dbb2ad.
Its manifest is 6126b38887ca04ccd1ca81a04efa3f8910c7b5ade4419e0db92a159268ac85ed.
The Native DLL remained the frozen baseline: this is explicitly not Native85
or registered-host/UI acceptance. Parent owns that separate combined evidence.

The complete C0 to C1 to C0 protected startup/project-workspace sequence passed.
All four global scopes were read by the actual C0 runtime after downgrade.
New chat/login/binding sentinels created after C1 survived. Unsupported project
scope with an existing global journal, unknown policies/rights, null/false/0
project journals, rehashed unknown target source and both caught failure
boundaries refused safely or retained a usable C1 state as appropriate.

Nine additional reference cases cover live, journal-before and journal-after
with readOnly:false, implicit backend and missing configuration. Config,
settings, journal bytes and sentinels stayed unchanged on refusal. C1 alone
recovered a valid mixed journal. No C0 startup on a C1-containing journal was
claimed. Final product inventories and per-pair journal/lock cleanup passed.
The full serial Node suite passed 159/159, and targeted profile/state/prompt
checks passed 25/25, zero skips. Legacy PS5 installer/profile checks passed too.
No Main worktree, OperatorState, registered host, provider or PLC was used as
a mutation target. This is not a system-wide zero-read audit.
