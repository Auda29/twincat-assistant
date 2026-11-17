# TwinCAT Assistant 0.2.0 Beta 6

Beta 6 addresses chat restarts after abrupt shutdown and removes the technical
build toolbar from normal conversations. It includes Engineering MCP 0.5.4 and
retains Knowledge MCP 0.1.2. Experimental ST-editor functions remain inactive.

## Changes

- Windows runtime ownership now uses an exclusive native file handle, released
  even when the launcher or its job is terminated abruptly. Recognized stale
  legacy locks can recover after exclusive acquisition and confirmation that
  their owner is absent. Live owners, malformed metadata and redirected or
  hard-linked lock files remain refused.
- Build availability is checked at startup and after editor context settles.
  Bursts are debounced, checks are single-flight, and stale results are ignored.
  Readiness checks never start a build or grant profile permissions.
- The normal chat no longer has a build toolbar or full-width activation banner.
  Enable or revoke Build tools through the existing **…** menu. Technical status,
  manual checks and output remain under **Diagnostics → Advanced**.
- Native status shows the correlated, bounded build-unavailability reason.
  Engineering distinguishes missing, ambiguous and failed interface inspection
  and identifies the interface without exposing paths or loader exceptions.

## Installation

Close TcXaeShell normally and run the setup EXE in the existing managed
installation directory. No uninstall is required. Verify the installer against
`SHA256.txt`. Application version is `0.2.0-beta.6`; native assembly, file and
VSIX version is `0.2.0.12`. The installer remains unsigned.

The supported target is still TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows
x64, with .NET 8, .NET Framework and WebView2. 4026/Shell64 is not enabled.

## Verification and remaining limits

`BUILD_PROVENANCE.json` records the exact source commit and completed release
checks. Windows real-process fixtures cover abrupt helper/owner termination,
descendant cleanup, restart and lock refusal/recovery. Standalone WPF/WebView
fixtures cover the hidden technical controls, menu selection, background checks
and existing context/approval guards. Package and installer fixtures use isolated
state, including synthetic credentials. They are not installed-host acceptance.

The previously reported `association_interop_unavailable` incident is not claimed
fixed. The new diagnostics distinguish its possible causes; the exact interface
failure still requires evidence from a supported live host. Build/event/dirty-state
and navigation acceptance, real provider sign-in after rollback, and real-corpus
typed extraction quality/performance remain open.

Analysis remains read-only. Each selective PLC build requires native approval.
Source editing, activation, download, Online Change and runtime control remain
unavailable. No normal-ST transaction or source rollback feature is enabled.

Engineering retains its disclosed dependency availability risks; see the
[Engineering 0.5.4 release](https://github.com/Auda29/twincat-mcp-mono/releases/tag/engineering-v0.5.4).
Publication as GitHub Latest does not close the remaining acceptance gates.
