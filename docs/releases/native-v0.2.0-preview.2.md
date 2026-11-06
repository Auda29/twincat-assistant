# TwinCAT Assistant 0.2.0 Preview 2

Installable alpha candidate for local acceptance. Installed UI, first-after-logon
and real installation/update/rollback acceptance remain open. This is not a
stable release or a claim that Roadmap 1–4 are fully accepted.

## Included

- Versioned Windows application and executable installer, with complete file
  inventories, pinned Core/Chat, both MCPs and retained third-party notices.
- First supported alpha installation target: TwinCAT 3.1.4024.78 in TcXaeShell
  15 x86 on Windows x64. Other hosts are refused rather than inferred compatible.
- Analysis prompt, exact known-policy migration, selective package rollback and
  operation-scoped full package verification. No PLC/runtime actions or editor
  write profile are enabled.

## Install

Download the setup EXE, SHA256.txt and provenance from this release. Compare the
downloaded EXE SHA-256 before execution. Close the target Shell normally. Use
64-bit Windows PowerShell with the required same-user administrative rights:

```powershell
.\TwinCAT-Assistant-0.2.0-preview.2-x64-setup.exe -Action Install -InstallRoot 'C:\Program Files\TwinCatAssistant'
```

No Git, npm, SDK or local compilation is required. Compatible installed TwinCAT
interop, .NET 8 runtime, .NET Framework and WebView2 are host prerequisites.
The installer is unsigned. It does not close an IDE, install a Windows service
or operate a PLC. A malformed package or incompatible host is refused.

An existing unpackaged prototype is deliberately not overwritten or adopted.
Its original extension must first receive a verified backup and a coordinated
one-time migration outside the extension search path. Do not delete the old
directory or change the operator configuration just to suppress a refusal.
An unpackaged prototype is not an automatic installer rollback target.

## Update and recovery

Managed versions retain application/extension snapshots and user data. A managed
rollback uses the same installation root, Windows identity and bound state root:

```powershell
.\TwinCAT-Assistant-0.2.0-preview.2-x64-setup.exe -Action Rollback -InstallRoot 'C:\Program Files\TwinCatAssistant'
```

This requires an actually retained compatible previous managed version. Unknown
policy, changed payloads, active hosts and pending transaction journals refuse
automatic continuation. Do not erase journals or replace entire user profiles
to force a rollback.

Source and synthetic checks, real packaged startup/MCP/restart and native
readiness evidence are recorded in `docs/MAINTENANCE_GUARD_BUILD_20261106.md`.
The optional maintenance coordinator is separate preparation tooling, not a
required part of ordinary installer use. The known Knowledge connection delay
and restored-editor startup investigation remain open. Editor profile,
full German/English UI translation and inline completion are not included.
