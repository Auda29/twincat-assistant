# TwinCAT Assistant 0.2.0 Preview 3

Fixes the Assistant needing a second click in View > Other Windows. The older
TcXaeShell Shell library creates a new synchronous pane without showing its
frame; the command now explicitly shows the frame on the first invocation.
Other opening failures record exception type and HRESULT in the IDE ActivityLog.

Includes the graphical setup from Preview 2 Setup 1: automatic administrator
request, destination picker, installation/update or managed rollback, progress
activity and saved diagnostic output.

## Install or update

Close TcXaeShell normally. Download the setup EXE and `SHA256.txt`, verify the
checksum, then double-click the EXE. Confirm UAC with the same Windows account
used for the Assistant and select Install/update. For an existing managed install,
use its existing installation folder, normally `C:\Program Files\TwinCatAssistant`.

The alpha target remains TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows x64.
.NET 8 runtime, .NET Framework and WebView2 must already be installed. Setup
does not start/stop TwinCAT or operate a PLC. The executable remains unsigned.

Application and native file/assembly/VSIX versions are advanced together to
`0.2.0-preview.3` / `0.2.0.3`. The retained pinned Core/Chat/MCP build inputs are
reused; the native host, application package and installer are rebuilt. This is
a local Windows build, not a hosted-CI installer artifact. Build provenance and
checksums accompany the EXE.

## Existing prototype profiles

Managed Preview 2-to-3 updates use the existing verified sibling-package policy
migration and preserve chats, logins and project bindings. An unpackaged developer
prototype is a separate migration case: old runtime paths are deliberately not
accepted as arbitrary managed package paths. The affected local workstation's
global and nine project configurations have already received the bounded,
backed-up [one-time profile migration](../PROTOTYPE_PROFILE_MIGRATION_20261109.md).
This release does not silently import unknown prototype configurations or reset
user data.

Installed first-click behavior and the complete real install/update/rollback
cycle still need operator confirmation. The Knowledge connection delay and
restored-editor cold-start investigation remain open. Editor write profile,
complete German/English UI and inline completion are not included.
