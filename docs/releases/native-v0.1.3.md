# Native prototype 0.1.3 preview

This source-only development prerelease adds active-editor context and fixes
native chat startup. The separate Node operator package remains `0.1.0-dev.1`.

## Changes

- The native header follows the active ST PROGRAM, FUNCTION_BLOCK or FUNCTION
  without manual Refresh/Read. Identity includes the IDE process, solution, PLC
  project and document, so same-name POUs remain distinct.
- Event subscriptions, debouncing and generation checks prevent stale context
  publication. Local source preview and manual diagnostics have separate status.
- Connect chat reuses a verified protected local service or starts the pinned
  runtime. First-run password setup uses Windows DPAPI for the current user.
  Credentials travel over UTF-8 stdin, with Unicode round-trip coverage.
- The embedded browser waits for protected readiness and reports navigation or
  service failure. Owned runtime processes stop with their native owner.
- The root lockfile updates Zod from `4.6.5` to `4.6.7` for
  [GHSA-mrhx-c7xc-m3w5](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5).

## Local verification

- 34 Node tests and 8 production-panel browser contracts pass.
- 49 native policy/context/chat checks and 20 synthetic WPF view checks pass.
- Real pinned-runtime transport checks and 7 C# owner/credential checks pass,
  including protected login with a non-ASCII password and process cleanup.
- Both x86 and amd64 VSIX variants build locally without warnings or errors.
- The installed x86 prototype follows PLC_A/PLC_B for same-name `FB_Motor`
  editors. The user confirmed the unsaved-comment indication and the actual
  OpenChamber login page. Synthetic reader lifecycle/isolation checks also pass.
- The refreshed root npm audit reports zero advisories. GitHub workflow results
  are tracked separately for the pushed commit.

## Acceptance and distribution limits

The pinned upstream audit reports five affected packages, two critical and three
high. See [the audit report](../SECURITY_AUDIT.md). This preview has no production
approval. Dependency remediation remains open.

Only source is published. Locally built VSIX files contain Beckhoff interop
components whose redistribution rights still need review; no VSIX is attached.

amd64 installation, extended native lifecycle/focus/responsiveness acceptance,
general exact unsaved-buffer semantics and an authenticated provider/model
conversation remain open. The observed login page does not establish those
results. Native context is local and is not automatically attached to chat or
MCP. Source writes, live apply and project-bound builds are not exposed.

No TwinCAT compiler, simulation or machine validation is claimed.
