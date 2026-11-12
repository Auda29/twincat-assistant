# Manual PLC project build

The native Assistant panel offers **Build PLC project** and **Attach build output
to chat**, localized in German and English. The first action builds the exact PLC
project bound to the selected project chat. The second stages a visible reference
in that chat's composer. It never submits a prompt. Analysis tool permissions and
the engineering MCP dependency pin remain unchanged.

## MCP discovery and implementation choice

Inspected `Auda29/twincat-mcp-mono` at pinned revision
`3c303837e6d6f72bec1b9d38e778a7c32c61ae5c`, specifically
`packages/mcp/src/index.ts` and `packages/core/src/engineering.ts`.
The pin registers `tc_build_project`, `plc_build_project`,
`tc_build_and_get_errors`, and `tc_error_list`. Both build methods call
`createUnavailableBuildResult`; `tcErrorList` returns `available: false`, and
`collectEngineeringIssues` returns an empty array. The configured project-file
backend explicitly has no live Automation Interface connection.

There is no usable compiler implementation to reuse at this revision. The new
native service uses the already-bound in-process DTE. It calls only
`SolutionBuild.BuildProject(..., false)` and observes build events. It does not
add a compiler MCP tool, a second IDE connection, or a dependency upgrade.

## Preconditions and safety boundary

- Supported host assessment must identify Beta 4's 4024.78 TcXaeShell15 x86 target.
  This change does not enable Shell64 or other hosts.
- Current IDE/solution/project identity must match the selected project chat.
  Bounded discovery must find exactly one project with that full path.
- The solution and discovered projects must report saved. All open documents must
  report saved and authoritative clean RDT state. Missing/failed evidence refuses
  the action. Nothing calls Save, SaveAll or changes editor contents.
- No build may already be running. The current configuration must be Debug or
  Release and the project platform must be TwinCAT RT (x86) or TwinCAT RT (x64).
  The solution's project mapping must match and enable Build. The service does
  not change any configuration, platform or build checkbox.
- The project hook policy refuses custom targets/tasks, project references,
  nonstandard imports, build events, target-dependency overrides, SDK imports,
  project user files, inherited Directory.Build files and redirected directories.
  Only the standard Beckhoff PLC targets import is accepted. Unrecognized layouts
  remain unavailable pending a reviewed expansion.

The installed vendor targets, IDE build configuration and toolchain are trusted.
A native build is not a sandbox for an untrusted project or compromised toolchain.
The local XML policy does not audit installed vendor target implementations.
There are no activation, download, online change, runtime or PLC start calls.

## Completion, output and navigation

The service retains DTE event sources and uses dispatcher events/timers without
waiting synchronously for completion. Matching project begin/done events and
`LastBuildInfo` determine completion. Missing, duplicate or unrelated project
notifications cannot publish success. IDE cancellation without a complete event
sequence yields an unknown/stale outcome. No cancellation command is issued.
After two minutes the outcome is unknown and the run remains locked until IDE
completion or confirmed solution close. A timeout does not claim to stop a build.

Project-context invalidation and solution closing discard results. Configuration
changes are checked again before publication, navigation and disclosure. A
500 ms dispatcher poll covers configuration changes; project tracker callbacks
invalidate results immediately, including transient loss of exact project identity.
Handlers and timers are detached on rebind/disposal. Closing and reopening the same
solution cannot make a discarded result valid again.

The canonical Build output pane is sampled at project begin. Only an append to
that baseline is fresh output. Replaced/cleared/unavailable or oversized capture
is explicitly unavailable, even if IDE completion establishes build success.
Historical Error List rows are never imported as diagnostics. Raw fresh output
remains visible, including lines that the parser does not recognize.

Limits are 512 project discovery entries, 4096 project items, 256 open documents,
512 KiB output pane, 256 KiB fresh output, 200 parsed diagnostics and 2000 Error List
navigation candidates. Chat staging is available for output up to 14,000 characters.
The parser recognizes English/German error and warning lines of the form
`absolute-file-or-moniker(line,column): error C0001: message`, with optional
column and numbered project prefix. Unsupported formats remain raw output.

A clickable row must resolve to an exact bound-project item. Member monikers
also require validation against the saved POU and exact member chain. At click,
identity, project membership and the source file size/write timestamp recorded at
build start are rechecked. The IDE Error List is used only
as a navigation index: one unique exact file/moniker, line, column and description
must match the fresh diagnostic. Native `ErrorItem.Navigate` then owns the custom
TwinCAT editor/member location. Missing or ambiguous matches show a localized
navigation-unavailable message. No relative-path resolution, name-based project
matching, parent-member fallback or guessed line offsets are used.

## Verification and remaining acceptance

Implementation verification uses synthetic fixtures and extension compilation.
It does not read operator PLC projects or start/attach to an IDE.

Real-host acceptance remains required before describing this as a verified
compiler workflow. In a separately authorized disposable PLC fixture, verify:

1. 4024.78 x86 host load and real project/configuration identifiers.
2. Successful, failed, up-to-date and cancelled build event sequences with a
   responsive panel. Confirm no save, deployment or runtime action occurs.
3. Actual Build pane diagnostic formats and capture behavior, including clearing.
4. Native navigation to POU declaration/body, method and property accessor lines.
   Unsupported location formats must refuse safely.
5. Dirty/unknown RDT state, different project chats, project/configuration switch,
   close/reopen, external builds, timeout, disposal and custom hook refusals.
6. Explicit chat staging with changed session/document/binding during the async
   read. Confirm no automatic compiler access or diagnostic disclosure to Analysis.

Shell64 acceptance remains a separate prerequisite for enabling this on 4026.

Verified in the implementation worktree on 2026-11-12:

- `scripts/Build-Native.ps1 -DotNetRoot <pinned-sdk-8.0.425> -Architecture X86`
  passed, including helper/probe compilation, 970 existing/localization native
  checks and 27 new build diagnostics/policy checks. Existing host warnings remain.
- `npm test` passed all 165 tests after preparing the pinned local distribution's
  post-install binary. No copied source-runtime receipt or old build output was
  used as evidence.
- No IDE/PLC acceptance, installation, runtime deployment or release was performed.
  Generated VSIX output is not a redistributable deliverable; the existing build
  includes a local vendor reference, which is excluded from delivered packages.
