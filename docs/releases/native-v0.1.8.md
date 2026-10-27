# Native prototype 0.1.8 preview

This development prerelease connects the existing Knowledge and TwinCAT MCPs
to native project chats in OpenChamber/OpenCode. It includes the 0.1.7 fix for
editor tracking when the IDE restores a docked Assistant pane. The Node
operator package stays at `0.1.0-dev.1`.

## Changes

- Project identity resolves independently of a supported ST source editor.
  A DUT or method can retain its PLC project without becoming a supported native
  source attachment.
- Each exact solution/PLC project gets a private immutable OpenCode workspace
  and a read-only saved-file engineering backend. Project/session/directory
  checks prevent prompting in a foreign project conversation. General chats
  have Knowledge tools only.
- `Configure-NativeMcp.ps1` connects the two existing MCP installations and
  accepts an optional Knowledge TOML path. No documentation corpus is bundled
  or ingested. The UI reports a missing corpus; saved-project access remains
  available. Connection states appear under Diagnose.
- Native POU attachments remain separate Automation Interface snapshots.
  Engineering tools read saved project files and their references. PLC writes,
  implicit saves, builds and ADS/runtime control remain unavailable.

## Verification

Local checks on 2026-10-27 pass:

- 51 Node tests, 8 browser contracts, 80 shared native checks and 28 WPF view checks.
- 37 compiled WPF/WebView2/runtime checks with both MCPs and no corpus, and
  34 checks without MCP setup, including project switching and reconnection.
- 3 protected Windows runtime/ownership checks.
- Both x86 and amd64 VSIX targets build without warnings or errors.
- Real pinned MCP processes return distinct sources for identically named
  synthetic POUs, refuse an unconfigured project path, return structured
  documentation sources from a synthetic activated corpus and report an
  unconfigured corpus as unavailable.

Linux and Windows CI pass for the implementation in PR #6. The fixtures use
isolated profiles and synthetic project/corpus data and make no model request.
These checks do not establish installed IDE lifecycle behavior, exact unsaved
editor-buffer semantics, actual corpus quality, live amd64 compatibility, PLC
compilation or machine behavior. The installed x86 payload is still 0.1.7.

## Distribution and setup

This is a source-only prerelease. Locally built VSIX files contain Beckhoff
interop components whose redistribution rights still need review, so no VSIX
assets are attached. Build instructions are in the
[native prototype guide](../NATIVE_PROTOTYPE.md); configure the two servers using
[native MCP setup](../NATIVE_MCP.md). No operator documentation corpus is
configured yet. The recorded [upstream dependency blockers](../SECURITY_AUDIT.md)
and wider native MVP acceptance gates remain open.
