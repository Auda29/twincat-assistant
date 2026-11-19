# TwinCAT Assistant 0.2.0 Beta 7

The chat composer now has one profile selector for Analysis and Build tools.
Selecting Build tools in a project chat opens the native permission prompt.
Selecting Analysis revokes that chat's grant. General chats open in Analysis
and have Build tools disabled. Each selective PLC build still requires its own
confirmation.

Beta 7 includes Engineering MCP 0.5.4 and Knowledge MCP 0.1.2. XML editing
and Debug live values are still in development. The Engineering interop issue
reported on a live host remains open.

Close TcXaeShell before running the unsigned setup program. Update the existing
managed installation in place. The supported target is TwinCAT 3.1.4024.78 /
TcXaeShell 15 x86 on Windows x64. Application version is `0.2.0-beta.7` and
native extension version is `0.2.0.13`.

Check the setup program against `SHA256.txt`. The exact source revision and
completed package checks are recorded in `BUILD_PROVENANCE.json`. Automated
tests cover the profile selector and package. Acceptance in a live TwinCAT IDE
is still open.
