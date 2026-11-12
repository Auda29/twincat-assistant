# MCP-first integration, 2026-11-12

This development window ends at 14:30 Europe/Berlin. Changes remain pull
requests. No merge, public release, installation, IDE operation or PLC action is
authorized by this implementation record.

## Reviewed dependencies

| Component | Candidate |
| --- | --- |
| Engineering MCP | `98f34dd76dc5903c2020ba037cec632ac3cd2004`, [PR #2](https://github.com/Auda29/twincat-mcp-mono/pull/2) |
| Knowledge MCP | `0.1.2.dev20261112`, `d5b774941600f746b092b0672216471f35dca73b`, [PR #27](https://github.com/Auda29/twincat-knowledge-mcp/pull/27) |

The Knowledge functional baseline is `486be3c`; the candidate changes its
version and adds reviewed artifact-building support. Its private wheel is
`ae4b7a20c006439560bacf73ac2f7d2f68915c56932684ec0453c1976be367cb`.
Its nine-tool schema digest is
`6a2231f57a81c3ca2820bbae78c2f590fd5f6d259a37b19ae996fd8085f3c3ab`.
The development lock deliberately blocks public release and standalone
installation. A reviewed upstream release and redistribution decision must
replace it before shipping.

Engineering owns compiler, association and source policy. Assistant owns the
native UI, host lifecycle, exact binding, approval and authenticated child.
Assistant PR #29 is not adopted as a second compiler implementation.
All repository leads use GPT-6.1 Sol high; bounded implementation uses Terra,
and independent architecture/review uses Astra.

## Knowledge adoption

The five new tools are `tc_find_symbols`, `tc_get_symbol`, `tc_find_libraries`,
`tc_get_library` and `tc_lookup_error`. The existing four topic/status tools
remain. Analysis grants exactly these additional read-only tools. Engineering
write, build, cancellation, runtime and ADS permissions are not added to Analysis.

Typed results carry explicit topic/source citations, nullable documented
namespace/library/version fields, missing fields and ambiguity. A documented
version does not establish an installed library version. Unsupported source
formats return absent coverage. The typed index is lazy; no operator corpus was
read during development. Exact previous C1 policies migrate once; modified or
mixed policies remain refused. Chat, login, preference and binding files remain
outside this migration.

The Python validation overlay accepts two exact reviewed original/patched hash
pairs. The new LF original is byte-identical to the committed upstream guard;
the earlier CRLF variant differs only in line endings. Hash verification does
not normalize input. The distribution-specific RECORD is updated and verified.
A private relocated runtime passed actual stdio catalog checks and all five
populated typed queries after a synthetic archive import and activation.
Real-corpus extraction acceptance remains open.

## Native engineering boundary

The provider loads only from the installer-selected application's
`host/x86/TwinCatMcp.Engineering.Host.dll`. Selector and connected package root
must agree. The exact manifest entry, length, hash, assembly name and version
`1.0.0.0` are checked before loading those bytes. The input builder compiles a
clean pinned upstream clone; the application builder verifies its receipt and
assembly identity. No vendor interop or Visual Studio SDK assembly is bundled.
The actual private input DLL hash is
`135c8e94db1ff632846c94e375d2958e5a5aa6a1686f34348a7cfe0ba9807781`.
Rebuilders must hash their own output.

Eligibility remains TcXaeShell x86, executable 15.0.0.0, shell base 15 and
TwinCAT 3.1.4024.78. This change does not enable 4026, Shell64 or full Visual
Studio. A session binds a fresh UUID, current IDE PID/start time, exact solution
and PLC paths and lifecycle epoch, separately from chat IDs.

Transient text/save/frame invalidation suspends new approvals without discarding
the provider or observation state. Only a verified same-project result resumes
approval. Unchanged timer polls do not invalidate source previews. Actual
solution/project/view revocation invalidates the session and owned child.
Unresolved build/source guards survive view/session loss for the IDE process.
Timeout, cancellation request or lost observation never prove an action stopped.

## Manual build

The provider preflights the exact PLC association, current configuration and
platform, known-clean buffers, project policy and provably empty build
dependencies. It invokes the documented selective
[`BuildProject(configuration, full .plcproj path, false)`](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842451979.html).
It does not substitute a `.tsproj`, switch configurations, save, activate or
download. The earlier assumption that `.plcproj` could never be passed to
`BuildProject` was incorrect.

The native confirmation binds the exact configuration/platform and request
bytes. The provider rechecks them immediately around one-shot approval. Build
results are bounded and owned by captured build events. Output and diagnostics
are unavailable if their provenance cannot be established. Compiler navigation
is not enabled without exact source-item/baseline evidence.

Cancellation records a request and keeps observing. It does not invoke the
IDE-wide Build.Cancel command. Correlated terminal evidence reconciles the
guard; pre-submission refusals/conflicts allow a new attempt. A later observation
conflict retains the guard.

## Manual source workflow

The supported candidate is an already-open ordinary `.TcPOU` XML text editor
with one exact physical RDT moniker. A fresh read-only RDT/MEF accessor verifies
cookie, docdata, buffer identity, active document and exact containing project.
It returns only a normal document/data `ITextBuffer`, never a projection buffer.
No document is opened or saved. Dirty state uses RDT persistence or exact
solution/project hierarchy persistence; unknown is refused, never treated clean.

Engineering verifies clean locked `.plcproj` membership and exact XML GUID/name
and section. `source.targets` discovers bounded existing POU, Method and Action
IDs. The UI reads, previews and confirms the exact before/after text. Approval
binds target, section, preview/journal, whole-buffer digests, project revision,
request bytes and expiry. Apply uses one `ITextEdit` transaction against the
exact whole-buffer baseline. Rollback requires the exact retained post-image.

Properties, specialized TwinCAT ST editors, FBD, LineIds and other unsupported
XML metadata remain refused. The narrow raw XML path is implemented; general
Editor-profile support is not complete. The journal exists only inside the
original provider session. It is not durable crash recovery. Lost/unknown
outcomes remain blocked and are not automatically retried.

## Owned MCP child and transport

Assistant starts its dedicated Engineering-only Node child suspended and assigns
it to a kill-on-close job before execution. It verifies the full selected package
inventory first. The child receives one bounded bootstrap line through stdin,
then authenticates a read-only ping over the private pipe, acknowledges readiness
and starts normal MCP. Native capability preflight calls the actual child's
`tc_session_describe`. This child has no ADS configuration or runtime tools.
Native manual approvals dispatch to the same Engineering provider implementation.
The saved-file MCP and model-visible Engineering permissions remain separate.

The pipe uses a random name, protected current-user SID DACL, remote-client
refusal, fresh 256-bit secret, mutual HMAC-SHA256 proofs and independent nonces.
One request is allowed per connection, without automatic retry. Limits are
4 KiB handshake, 256 KiB request, 512 KiB response, 30 seconds and four
connections/handlers. The secret never enters arguments, environment,
configuration, model tools or logs. Local capability possession authorizes
transport only; it grants no mutation approval. The installation ownership
boundary must prevent foreign package replacement after verification.

## Validation and open acceptance

The full portable/Windows Node suite passed 171 tests. Independent adoption
review verified actual input manifests, overlay/RECORD, source snapshot migration
and provider receipt. Independent native review found timer-preview invalidation
and two build observation/rejection defects; all were corrected with actual
standalone native callback/renderer regressions. The x86 host and VSIX build
with the installed SDK; this is not a claim that the pinned release build ran.
Synthetic child tests cover stdin bootstrap, framing, job cleanup, cancellation
and tampered packages.

The final x86 host/VSIX build succeeded with 14 existing threading warnings.
There were 1,062 native policy/context/localization checks and 28 actual
standalone WPF/WebView callback checks. The real packaged Node MCP passed
cross-language HMAC bootstrap, protocol initialization, correlated describe
and owned-process disposal against a synthetic C# provider. The full private
application passed protected startup, both real saved-file/Knowledge MCP probes,
restart, state preservation and unchanged-package checks.

No IDE was started or attached, no operator source/state was inspected, no PLC
was compiled and no installation was performed. Actual host build events,
diagnostic navigation, TwinCAT dirty-state reporting, live editor transactions,
real-corpus extraction and installed acceptance remain open. The private
candidate is not a public release and the broader P1 Editor scope is incomplete.
