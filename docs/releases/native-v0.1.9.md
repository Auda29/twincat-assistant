# Native prototype 0.1.9 preview

This development prerelease sets up installed Beckhoff InfoSys help from the
Assistant and shares one Knowledge connection across native chat contexts.
Search results now include the IDs and source data needed to open and cite a
documentation article. The Node operator package stays at `0.1.0-dev.1`.

## Changes

- Discover installed German and English InfoSys archives, select their version
  and prepare the documentation through **Wissenssuche einrichten**. Import,
  validation and activation run in the background with cancellation/retry and
  update detection. Existing external TOML configurations are preserved.
- General and project chats share one loaded Knowledge backend per protected
  native runtime. An authenticated local relay forwards only the four reviewed
  Knowledge read tools. Protocol negotiation uses that backend's existing pipe
  instead of starting a disposable second copy of the entire corpus.
- Managed snapshot activation replaces the backend once. Closing a chat retains
  the loaded corpus; closing the native runtime revokes the shared process and
  its descendants through the existing process supervision.
- Complete structured Knowledge results are also supplied in the first model
  text block. The previous count-only summary could hide search IDs, article
  content and citations from the model.
- Windows help discovery and setup ownership handle short/long path aliases
  consistently. Setup tests require actual script refusal rather than treating
  a process timeout as a successful refusal.
- Engineering access remains scoped to the selected project's saved files.
  Native editor attachments remain separate snapshots. Provider/model consent
  and the reviewed read-only tool policy are preserved.

## Verification

Local implementation checks on 2026-10-28 pass:

- 59 Node tests and 8 browser contracts, including concurrent Knowledge clients,
  cancellation isolation, one reload per activation, complete result text,
  authenticated IPC, unreviewed-tool refusal and owned process-tree shutdown.
- 43 compiled WPF/WebView2/runtime checks with synthetic help archives. The
  native 0.1.9 payload also passed 33 view checks; both VSIX targets and the
  Windows helper build without warnings or errors.
- Both real pinned MCPs connect in two isolated native project contexts with
  exactly one Knowledge backend. Returned search IDs open their source topics;
  loss of the launcher revokes the shared connection.
- The installed German help imports and activates with 49,970 articles. A full
  load takes approximately 108 seconds, three subsequent searches across
  separate clients take 617 milliseconds in total, and opening a returned topic
  takes 452 milliseconds on the local test machine. These are measurements,
  not latency guarantees. Fresh runtime startup still performs full validation.
- The operator reports a successful installed end-to-end model test using both
  MCPs: saved project code and library references are read, a matching Beckhoff
  topic is opened using its returned ID, and its documentation is cited. The
  answer distinguishes saved files, editor snapshots and documentation.

The automated checks use isolated profiles and make no model request. The
operator-reported model test is separate evidence. No live PLC connection,
exact live library resolution, PLC compilation or machine behavior is verified.
Linux and Windows CI are checked on the final release commit before publication;
the GitHub release records that commit and its workflow result.

## Distribution and setup

This is a source-only prerelease, consistent with 0.1.8. Locally built VSIXs
contain Beckhoff interop components whose redistribution rights remain under
review, so no VSIX assets or documentation corpora are attached. Recorded
upstream dependency findings and wider native acceptance gates remain open.
This preview does not claim production approval.

The x86 0.1.9 prototype is installed locally with all 13 payload hashes verified
and 0.1.8 retained for rollback. The shared-connection and result-text fixes
update the repository's Node runtime; a running IDE must restart once to load
them. The existing documentation index is reused.

See [documentation setup](../KNOWLEDGE_SETUP.md),
[native build/setup](../NATIVE_PROTOTYPE.md),
[native MCP configuration](../NATIVE_MCP.md) and
[recorded release blockers](../SECURITY_AUDIT.md).
