# Knowledge 0.1.1 checkpoint

2026-10-29. Published at the user's request before completing the remaining
installed-host checks.

## Completed

- Knowledge MCP release `v0.1.1`, source
  `fff24f410efa687bf39fe6829f5d068b1e80cf8f`, is pinned in
  [knowledge-lock.json](../upstream/knowledge-lock.json).
- [Install-Knowledge.ps1](../scripts/Install-Knowledge.ps1) downloads the pinned
  wheel, requirements and checksums, verifies their SHA-256 values, installs
  hashed dependencies in a separate environment and verifies both CLI and
  package metadata. Fresh installation and reuse passed locally.
- The local native MCP configuration and protected global operator profile now
  select `%LOCALAPPDATA%\TwinCatAssistant\knowledge-runtime\0.1.1\.venv\Scripts\twincat-knowledge.exe`.
- The configured engineering entrypoint, repository root, provider settings and
  managed corpus paths remain in place. Chat database and active corpus-selection
  hashes were unchanged after configuration. Configuration backups are under
  the backend worktree's ignored `local-data/knowledge-upgrade-backup-*`.
- The previous editable Knowledge installation remains available for rollback.
  Its source reported 0.1.1 while package metadata still reported 0.1.0; the
  selected wheel installation consistently reports 0.1.1.

## Checks completed locally

- `npm run check`: 79 Node tests and eight browser checks passed.
- The released wheel reports 0.1.1 through CLI, package metadata and actual MCP
  initialization; the protocol remains `2026-07-28` with four tools.
- The release integration test passed search-to-topic retrieval through the
  production shared broker, including complete model-visible content and one
  shared backend process.
- The native MCP integration passed with the released wheel and the existing
  engineering server, including isolated synthetic PLC projects.
- No paid model request or live PLC/compiler action was performed.

## Resume here

1. Check GitHub CI for the published main commit. Local success does not imply
   that the hosted checks have completed.
2. Start/reconnect the Assistant and confirm the selected 0.1.1 installation
   through the real installed host. No TwinCAT IDE was running at the checkpoint.
3. Test status, a search and opening the returned topic with the existing
   49,970-topic corpus. This full-corpus check has not been repeated for the wheel.
4. The configured fixed runtime checkout remains at the earlier `de4eef8`
   application build. Its path and source runtime receipt were not moved or
   rebuilt; this software update changes the selected external MCP executable.
5. Resume the separate backend/start-contract work and PR #8 independently.

Version 0.1.1 recovers marked abandoned loader caches after process termination.
It does not remove unmarked legacy caches, change the corpus format or reduce
the first full-corpus load. Do not delete existing caches as part of resuming
this checkpoint.
