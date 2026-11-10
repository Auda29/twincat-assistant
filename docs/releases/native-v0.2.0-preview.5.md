# TwinCAT Assistant 0.2.0 Preview 5

Fixes another native project-setup failure. Node emits its JSON response as UTF-8,
but the Host previously decoded it using the Windows/console encoding. Umlauts
and `ß` in returned solution and project paths could therefore change. The
unchanged strict project-binding check then rejected those corrupted paths.

The native MCP process starter now explicitly selects UTF-8 for its output
channels and decodes JSON strictly. Project validation, package verification,
read-only permissions and lock behavior are not relaxed.

## Evidence

`scripts/Test-NativeMcpEncoding.ps1` invokes the actual native process starter
against a synthetic Node protocol peer. The same Unicode-path test fails with
the previous Host under code page 850 and passes with the fix under 850, 1252
and 65001. No real solution or PLC files are read. A fourth check confirms that
only synthetic filenames were used.

The actual installed Preview 4 backend invocation also passed through the fixed
native starter using an isolated copy of known configuration metadata and forced
code page 850. Original configurations, chats and credentials were unchanged.
An initial PowerShell probe read its own UTF-8 fixture using the wrong default
encoding; this was corrected and is not evidence of a product defect. The
subsequent focused before/after test isolates response decoding.

## Installation

Close TcXaeShell normally, download the setup EXE and `SHA256.txt`, compare the
checksum and choose **Install / update** in the existing installation directory.
Confirm UAC with the same Windows account. Uninstalling Preview 3 or 4 is
unnecessary. The application version is `0.2.0-preview.5`; the native assembly,
file and VSIX version is `0.2.0.5`.

The target remains TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows x64 with an
installed .NET 8 runtime, .NET Framework and WebView2. The installer is unsigned.
Existing project locks still require reviewed recovery with processes quiescent;
the extension does not delete them automatically.

The PR #18 fix remains included. Separate PRs #16 and #17 are not included.
`BUILD_PROVENANCE.json` records the local Windows package build and its checks.
Visible acceptance of this version in the installed IDE remains a subsequent
operator test.
