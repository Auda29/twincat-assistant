# TwinCAT Assistant 0.2.0 Beta 3

Beta 3 addresses long Assistant loading and editor/UI stalls reported with Beta 2.
It also introduces the shared code/conversation icon in setup, Chat and the IDE's
Other Windows menu.

## Changes

- Configuration and packaged entrypoint verification run on a background worker
  once per connection. Status, rendering and chat-binding lookups reuse the
  connection snapshot instead of repeatedly parsing and hashing on TwinCAT's UI
  thread. Explicit reconnect revalidates configuration; failed refreshes cannot
  reuse the preceding snapshot.
- Complete package verification overlaps at most four file reads. All file
  sizes, hashes, path/link checks and manifest ordering remain. A local sequential
  comparison on the same 21,538-file Beta 2 package took 17.9 seconds before and
  5.7 seconds after; storage/cache/antivirus conditions affect these timings.
- Knowledge status no longer blocks project-chat selection. One logical
  Knowledge request shares its complete package inventory, while later requests
  reverify. No startup timeout was increased or policy/lock check disabled.
- The new blue code/conversation symbol replaces the TC letter mark and is
  embedded in the setup EXE/window, Chat and native menu. Windows icon sizes
  cover 16–256 pixels; the menu command-table revision is advanced.

All Beta 2 fixes and features remain included, including version-aware setup,
Windows ZIP-directory extraction, saved DE/EN selection and product cleanup.

## Installation

Close TcXaeShell normally. Download the setup EXE and `SHA256.txt`, compare the
checksum and choose **Install / update** in the existing managed installation
directory, normally `C:\Program Files\TwinCatAssistant`. Confirm UAC with the same
Windows account that uses the Assistant. Existing managed installations need no
prior uninstall; chats, sign-ins, preferences and project bindings retain their
paths. Application version is `0.2.0-beta.3`; native assembly/file/VSIX version is
`0.2.0.9`.

The installation target remains TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows
x64 with .NET 8, .NET Framework and WebView2. The installer is unsigned. It does
not start/stop the IDE or PLC, and does not adopt an unpackaged prototype.

## Verification and limits

`BUILD_PROVENANCE.json` records exact source/artifact identities and successful
checks. The fresh local Windows Host/application/installer build is separate
from hosted portable/source CI. Unchanged pinned Core/Chat/MCP inputs are reused
only after receipt/hash validation. Native WPF/WebView startup, wizard/icon and
package smoke checks use private synthetic state without IDE attachment, real
credentials or model requests. Actual EXE extraction is verified against every
embedded application-file hash without executing installer Main/Run or UAC.

The repeated UI-thread work is confirmed and corrected. The operator's
intermittent installed initialization failure was not reproduced or conclusively
attributed; the release removes plausible timeout pressure but does not claim
that every initialization failure is resolved. Installed cold/warm solution and
editor responsiveness, repeated project initialization, language/member/paste/
first-after-logon and complete real update/rollback acceptance remain open.
The older PLC editor-restoration hang is a separate investigation. Editor
profile, PLC writes and inline completion are not included.

This release is named Beta 3 and marked GitHub **Latest** at the user's request;
GitHub's Pre-release flag is therefore disabled.
