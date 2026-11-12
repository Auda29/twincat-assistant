# MCP-first integration, 2026-11-12

Release update: Beta 5 adopts the published MCP versions and exposes only build,
session and diagnostic operations. All new source-editor paths are retained
but inactive, as described in [the release scope](releases/native-v0.2.0-beta.5.md)
and [profile guide](EDITOR_PROFILE.md). The following record describes the
earlier private development checkpoint and its evidence, not the active release
source-tool catalog. It does not claim real-host or corpus acceptance.

Changes remain private development pull requests, without merges, releases,
installation or live IDE/PLC operations. The coordinated window ends at
14:30 Europe/Berlin. All three leads use Sol; bounded implementation uses Terra
and independent architecture/review uses Astra.

## Dependencies

| Component | Reviewed candidate |
| --- | --- |
| Engineering MCP | `83a7d226fbe2492e917a7fe6ea6fa26f0498ac6a`, [PR #2](https://github.com/Auda29/twincat-mcp-mono/pull/2) |
| Knowledge MCP | private `0.1.2.dev20261112`, `d5b774941600f746b092b0672216471f35dca73b`, [PR #27](https://github.com/Auda29/twincat-knowledge-mcp/pull/27) |

Engineering owns compiler, project association, source evidence and change
policy. Assistant owns native binding, UI, approvals, scoped accessors and child
lifetime. Assistant PR #29 is not adopted as a second compiler implementation.
Knowledge owns typed documentation extraction and queries.

The Knowledge functional baseline is `486be3c`. Canonical wheel SHA-256 is
`ae4b7a20c006439560bacf73ac2f7d2f68915c56932684ec0453c1976be367cb`;
nine-tool schema SHA-256 is
`6a2231f57a81c3ca2820bbae78c2f590fd5f6d259a37b19ae996fd8085f3c3ab`.
The development-only lock blocks public release and standalone installation.
A reviewed released artifact pin and redistribution decision are required
before shipping. This record is not that approval.

The clean-clone Engineering input uses SDK 8.0.425 and net472 references.
Its actual rebuilt provider is assembly `TwinCatMcp.Engineering.Host` version
`1.0.0.0`, SHA-256
`3270219a38b46d7b290d59572ac5fb19b5b3f2449d24dd46025e2d4a92424023`.
The builder verifies the revision/version/hash receipt and PE identity before
copying it to `host/x86`. The loader checks installer-selected root, exact
manifest entry, size/hash and identity before loading those bytes. A future
rebuild must hash its own output. No vendor interop or VS SDK assembly is bundled.

## Delivered development slices

| Feature | Implementation | Acceptance still open |
| --- | --- | --- |
| Five typed Knowledge tools | Search/get symbols, search/get libraries and error lookup, with citations, ambiguity and missing fields | Real operator corpus extraction |
| Selective PLC build | Exact association, configuration/platform, clean-state and empty-dependency preflight; native approval | Actual host events and compiler results |
| Native diagnostic navigation | Build-owned diagnostic and clean source/project baseline; exact current native ErrorItem match and retained COM identity | Actual TwinCAT navigation behavior |
| Normal ST read/preview | Exact attached slot, GUID/name/project handle, RDT identity and attached document; no saved text mislabeled live | Installed custom editor acceptance |
| XML-buffer apply/rollback | Exact whole-buffer ITextEdit baseline, trusted diff approval and session journal | Actual host edit/rollback acceptance |
| Editor profile | Explicit per-chat activation, narrow managed tools, ephemeral MCP registration and native confirmation | Operator workflow acceptance |
| Normal ST apply/rollback | Blocked; public adapter does not establish an exact-slot atomic transaction | Vendor-supported transaction/sync contract first |

The broader P1 write requirement is not complete. The precise reason is in
[NORMAL_ST_TRANSACTIONS.md](NORMAL_ST_TRANSACTIONS.md); it is not merely a missing
live test. See [EDITOR_PROFILE.md](EDITOR_PROFILE.md) for the actual workflow.
Host eligibility remains exactly TcXaeShell x86 / TwinCAT 3.1.4024.78,
executable 15.0.0.0 and shell base 15. This does not enable 4026 or Shell64.

## Knowledge and configuration

The existing four topic/status tools remain alongside the five typed tools.
Typed results carry source citations and nullable documented namespace, library
and version fields. Documentation does not establish an installed library version.
Unsupported formats return absent coverage. Synthetic archive import, activation
and all five populated typed queries ran through the actual relocated runtime.
No operator corpus was read.

The overlay accepts only the two reviewed exact original/patched hash pairs;
input is never normalized before hashing. The canonical LF guard matches its
committed upstream bytes, while the legacy variant differs only by CRLF. The
version-specific RECORD is updated and verified.

C3 adds the managed `twincat-editor` agent, exact tool permissions and narrow
permission-policy entries. Analysis remains default and read-only. Exact C2,
C1 and C0 configuration migration candidates are retained; unknown/modified
policies remain refused. Provider, chat, login and binding data are preserved.

## Native source and build boundaries

Every engineering session binds a fresh UUID, IDE PID/start time, canonical
solution and PLC paths and an epoch separate from chat IDs. Transient editor
invalidation suspends approvals while retaining submitted-operation observation.
Actual rebinding or view disposal revokes the provider and owned child.

Pinned chat history also survives transient build/save/frame invalidation.
Its existing rows stay visible but disabled until a fresh project identity is
published; stale callbacks remain refused. Temporary anchor/host invisibility
does not dismiss a pinned list. Confirmed project loss still closes the list,
and unpinned lists retain their normal dismissal behavior.

BuildProject receives the exact `.plcproj` path and verified configuration plus
platform, without substituting `.tsproj`, changing platform, saving, activating
or downloading. Bounded output/diagnostics belong to matching build events.
Cancellation is a recorded request; it does not invoke the IDE-wide cancel.
The diagnostic baseline is capped at 256 files and 16 MiB total and captured
only during a requested build, never automatic capability preflight.

Dirty state is observed through exact RDT entries, including member monikers,
or explicit bound project/item persistence. A complete bounded RDT scan and
identity rechecks precede unopened-source proof. Absence and unknown never mean
clean. Actual TwinCAT support for unopened-item persistence remains unverified.

Normal ST uses the actual attached declaration/implementation slot and document,
never primary/focused-view order. RDT close/replacement permanently invalidates
that mapping for the session. Returned `customStReadOnly` previews have no
mutation-ready ID. The synthetic MEF searchable-block adapter is never used as
a normal-ST write transaction.

Native approvals bind exact raw request bytes, session/target, section,
preview/journal, digests, project revision and expiry. The trusted provider supplies
before/proposed text; models cannot substitute the displayed diff. Pending
requests have a checked deadline before and after the modal. Replay records are
provider-session scoped. Unknown submitted work retains process-wide guards;
no timeout or profile toggle clears it. Source journals remain session-memory
records, not crash recovery.

## Owned Editor transport

The native launcher joins a kill-on-close job before its first startup plan.
The dedicated child receives its HMAC capability only through stdin and mutually
authenticates the current-user local pipe. No raw host capability enters model
tools, arguments, environment, durable configuration or logs.

A distinct frontend bearer token exists only in Core's ephemeral MCP override.
Core supplies the chat metadata; the broker requires the exact selected chat
and workspace and exposes only the frozen 15 tools. Analysis permissions still
deny those tools. Runtime configuration GET responses and durable files do not
contain either credential. Revocation closes transport first, then removes the
registration; a broken control channel stops only the owned runtime job.
Pending generations and fresh chat epochs prevent late activation after a switch.

## Validation

- Pinned SDK 8.0.425 x86 host/helper/probe/VSIX build passed; 1,076 native
  policy/context/localization checks passed. Fourteen existing threading warnings.
- Full Node suite: 191 passed, no skipped tests. The source Core/Chat runtime was
  rebuilt for the Editor overlay.
- Thirty-six actual standalone WPF/WebView callback/renderer checks passed;
  112 chat-list component checks include pinned/unpinned visibility regression.
  Actual packaged Node HMAC bootstrap, MCP initialization, correlated describe,
  owned-process disposal and tamper refusals passed against a synthetic provider.
- Real pinned Core integration established Editor-allow, Analysis-deny and shell-
  deny rules, location isolation, ephemeral registration/removal and no credential
  persistence. Stdin bind/revoke and x86/x64 owner-death job checks passed.
- Full private application passed protected startup, both real MCP probes,
  restart, state preservation and unchanged-package checks.
- Independent reviews fixed transient preview invalidation, build observation and
  rejected-start guards, transient Editor mode, pending-bind cleanup/reentrancy
  and process-wide approval-limit defects. Final bounded reviews found no
  remaining concrete P1/P2; live-host acceptance was not claimed.

No IDE was started or attached, no customer source/state was inspected, no PLC
was compiled and no installation or public release was performed. The current
private package does not close real-host, corpus or normal-ST write gates.
