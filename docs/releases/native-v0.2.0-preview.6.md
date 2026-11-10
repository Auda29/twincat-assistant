# TwinCAT Assistant 0.2.0 Preview 6

Fixes startup showing “Zuerst eine Unterhaltung öffnen.” (“Open a conversation
first”) while the loading screen remains visible. A new conversation can already
be confirmed before React renders its input field. The native Host previously
treated this valid state as not ready and rejected the binding.

Confirmed chat metadata now allows binding independently of DOM mounting.
Presentation still waits for the matching history and rendered input field.
The WebView2 composition control renders behind the opaque WPF loading screen.
Mouse and keyboard input remain disabled until presentation is permitted.
Project, session, document and permission checks are retained.

## Verification

`node scripts/test-native-chat-startup.mjs` starts only an owned synthetic
backend state and a standalone WPF/WebView2 control. It uses the actual native
chat creation path, embedded integration and built Chat. An intentionally delayed
DOM signal reproduces the observed window for both general and project chats.
Checks cover binding before mount, disabled loading input, subsequent
presentation and another automation pass without a duplicate conversation.
This UI test replaces MCP preparation with synthetic metadata. The separate
package smoke test checks both real MCPs on synthetic files.

The previous Preview 5 Host rejects the same startup scenario before
presentation with “Der neue allgemeine Chat ist nicht verfügbar.” (“The new
general chat is unavailable”). The new Host passes all twelve checks. No IDE is
started, no engineering project is opened, no model prompt is sent and no
operator profile is changed. An initially incorrect test-page login was replaced
with the actual native login before navigation. Failed fixture runs do not count
as additional product defects or successful evidence.

## Installation

Close TcXaeShell normally and run setup using **Install / update** in the existing
installation directory. The application version is `0.2.0-preview.6`; the native
assembly, file and VSIX version is `0.2.0.6`.

The target remains TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows x64.
Fixes from PR #18 and #19 are included; the separate PRs #16 and #17 are not.
`BUILD_PROVENANCE.json` records the local package build. Visible acceptance in
the installed IDE remains an operator test.
