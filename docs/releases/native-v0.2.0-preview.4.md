# TwinCAT Assistant 0.2.0 Preview 4

Includes the PR #18 fix for failed project setup. Existing project locks are
reported explicitly. Setup failure ends the loading animation and is no longer
shown as a disconnected chat. The bounded setup deadline is now 120 seconds,
allowing complete verification of both application packages during a version
transition. Locks are not automatically deleted or adopted.

## Installation and update

Close TcXaeShell normally. Download the setup EXE and `SHA256.txt`, compare the
checksum and open the EXE. Confirm UAC with the Windows account that uses the
Assistant. Choose **Install / update** and the existing installation directory,
normally `C:\Program Files\TwinCatAssistant`.

Uninstalling Preview 3 is unnecessary. Preview 4 uses the distinct application
version `0.2.0-preview.4` and native assembly/file/VSIX version `0.2.0.4`.
Existing chats, sign-ins and project bindings are retained.

Previously abandoned project locks still require reviewed recovery with
processes quiescent. The two identified locks on the affected workstation were
backed up separately; the installer does not delete such files. The
[findings and limits](../PROJECT_SETUP_FAILURE_20261109.md) describe the original
diagnosis.

The target remains TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows x64.
.NET 8 runtime, .NET Framework and WebView2 are local prerequisites. The
installer is unsigned and neither starts nor stops an IDE or PLC.

## Scope and evidence

This release contains only the merged main source including PR #18. Product
cleanup from PR #16 and the full language change from PR #17 are not included.
The first-menu-invocation fix and graphical installer from Preview 3 are retained.

Chat is rebuilt from this revision's pinned sources; the unchanged Core and
verified MCP inputs are reused. The Host, complete application package and setup
EXE are built locally on Windows. This is not a new hosted-CI installer run.
`BUILD_PROVENANCE.json` and `SHA256.txt` accompany the EXE.

Protected package startup, both real MCPs on synthetic files, restart, data
preservation, unchanged-package integrity and embedded installer files are
verified before publication. Visible acceptance in the installed IDE remains a
subsequent operator test. Knowledge connection latency and cold startup with
restored editors remain under investigation.
