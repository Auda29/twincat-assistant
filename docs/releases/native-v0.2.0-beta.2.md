# TwinCAT Assistant 0.2.0 Beta 2

Beta 2 combines all merged fixes and product features in a distinct application
version that can update Preview 6 or Beta 1 through the normal installer.

## Included

- Correct extraction of empty Windows ZIP directories, including npm scope
  directories left by package reduction. Both separator styles are supported;
  path-traversal refusal and create-new file behavior remain.
- Setup shows its bundled application version, the managed version already
  installed in the selected directory and the exact fresh-install, update or
  rollback operation. Folder/action changes refresh the overview. Already
  installed or retained versions, unknown state and interrupted transactions
  are not offered as a new installation.
- Shared German/English language selection for Host and Chat, saved across
  restarts. Source code, messages, titles, drafts and attachments remain original.
- Reduced Chat services/dependencies, TwinCAT Assistant branding and product
  version reporting, with required data contracts and upstream attribution retained.
- All Preview 6 fixes: first tool-window invocation, UTF-8 native MCP output,
  explicit project-setup errors and confirmed chat binding before composer mount.

## Installation

Close TcXaeShell normally. Download the setup EXE and `SHA256.txt`, compare the
checksum and choose **Install / update** in the existing installation directory,
normally `C:\Program Files\TwinCatAssistant`. Confirm UAC with the same Windows
account that uses the Assistant. Existing managed installations need no prior
uninstall. Chats, sign-ins, preferences and project bindings retain their paths.

Application version is `0.2.0-beta.2`; native assembly/file/VSIX version is
`0.2.0.8`. This is a new application version, unlike the installer-only Beta 1
Setup 1 revision.

The supported installation target remains TwinCAT 3.1.4024.78 / TcXaeShell 15 x86
on Windows x64 with .NET 8, .NET Framework and WebView2. Other Hosts remain
unaccepted. The installer is unsigned and does not start or stop the IDE/PLC.
An unpackaged prototype is not automatically overwritten or adopted.

## Verification and limits

The local Windows build produces a new complete application and installer from
the release source. Unchanged pinned Core/Chat/MCP inputs are reused only with
their existing receipts/manifests verified. `BUILD_PROVENANCE.json` records exact
source, artifact identities and checks that passed; hosted portable/source CI
is distinguished from the local installer build.

Package smoke uses both real MCPs only on synthetic files and verifies protected
startup, restart, state preservation and package integrity. Installer verification
checks the embedded archive, invokes the actual EXE extraction helper in a private
temporary root and verifies all extracted application files. The wizard's displayed
version must match the embedded application manifest. Installer Main/Run, real
Host transactions and UAC are not invoked by artifact verification.

Visible installed Beta 2/language acceptance, real provider/model acceptance,
member/paste/first-after-logon checks and a complete real update/rollback sequence
remain separate. Editor profile, PLC writes and inline completion are not included.
The release is named Beta 2 but marked GitHub **Latest** at the user's request;
GitHub's Pre-release flag is therefore disabled.
