# TwinCAT Assistant 0.2.0 Preview 2 - Setup 1

Installer revision for the existing Preview 2 application. Double-click the EXE
to open a German setup window. Windows requests administrator rights automatically.
Use the same Windows account that normally runs the Assistant.

- Install/update or restore a retained compatible previous version.
- Choose the installation folder; the default is `C:\Program Files\TwinCatAssistant`.
- See progress activity, transaction output and a final success/error result.
- Open the saved log in `%LOCALAPPDATA%\TwinCatAssistant\installer-logs`.

Close TcXaeShell before installing. Download the EXE and `SHA256.txt` and compare
the checksum. The unsigned alpha still needs TwinCAT 3.1.4024.78 / TcXaeShell 15
x86 on Windows x64, .NET 8 runtime, .NET Framework and WebView2. The wizard checks
prerequisites; it does not install missing prerequisites or start TwinCAT.

Explicit CLI arguments remain supported, without interactive parameter prompts.
User data, host restrictions, ownership checks and transactional recovery remain
under the existing installer logic. An unpackaged prototype must first be removed
with a verified backup; it is not overwritten or adopted automatically.

This EXE is a locally built installer, not a new hosted-CI artifact. The retained
Preview 2 application input has manifest SHA-256
`6ee4eb9bb5c83de02f60cdea06ecf918e33c50d4baf5a3da2c39a01b2e7da271`.
The release includes build provenance. Wizard forwarding/error/default/UAC
checks and embedded application size/hash verification passed. Installed UI,
first-after-logon and real install/update/rollback acceptance remain open.

The application version remains `0.2.0-preview.2`. Setup 1 changes installation
UX and elevation; the prior Preview 2 release assets remain unchanged.
