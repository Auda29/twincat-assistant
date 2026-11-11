# TwinCAT Assistant 0.2.0 Beta 4

Beta 4 incorporates PR #26's startup, editor-context and Knowledge improvements
and PR #27's initial Shell64 preparation.

## Changes

- Unchanged project setup avoids the global provider writer lock after checking
  the complete known configuration. Actual updates retain the existing locks.
- Saved member correlation requires captured Method, Action, Property or accessor
  text to agree with live XML. Torn snapshots lose correlation. Saved XML reads
  use bounded buffers instead of allocating 2 MiB on each poll.
- Knowledge validation uses equivalent ASCII-control checks and a bounded cache
  of successful exact locators. The exact pinned module is patched with recorded
  original/patched hashes and a corrected package receipt. A local corpus run
  decreased from 123.5 to 78.0 seconds; overlapping build work means these are
  observations, not a controlled benchmark. The 15-second target remains open.
- Host inventory distinguishes a classic 4024 setup from the mere presence of
  Package Manager. The experimental amd64 host builds explicitly as x64.
- A trusted-runner source-to-installer workflow and separately privileged
  publication are available. They require a provisioned Windows runner; this
  release uses the local Windows build path, not that complete workflow.

## Installation

Close TcXaeShell normally and run the setup EXE in the existing managed
installation directory, normally `C:\Program Files\TwinCatAssistant`. Compare
its SHA-256 with `SHA256.txt`. Confirm UAC with the account that uses the Assistant.
No uninstall is needed for an existing managed version. Chats, sign-ins and
bindings keep their existing paths. Application version is `0.2.0-beta.4`;
native assembly/file/VSIX version is `0.2.0.10`.

The supported installation target remains TwinCAT 3.1.4024.78 / TcXaeShell 15
x86 on Windows x64 with .NET 8, .NET Framework and WebView2. Setup is unsigned
and does not close the IDE or perform PLC/runtime operations.

## Verification and remaining work

`BUILD_PROVENANCE.json` records this candidate's source, artifact hashes and
actual checks. Package tests use private synthetic state and both real MCPs.
Synthetic update/rollback checks preserve chat/binding bytes and DPAPI-protected
login bytes with same-user decryption. This does not certify real provider
sign-ins after a workstation rollback.

TwinCAT 4026/Shell64 support is preparation only. No 4026 installation was
available for acceptance, and its installer target remains blocked. Explicit
PLC builds and navigable compiler diagnostics are planned, not implemented.
Installed member/unsaved-buffer, first-after-logon, repeated initialization and
normal-use acceptance remain open. No new access to unsaved editor buffers is
claimed. The release build does not change the workstation installation.

This beta is published as GitHub Latest at the operator's request; GitHub's
Pre-release flag is disabled.
