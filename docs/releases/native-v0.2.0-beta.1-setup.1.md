# TwinCAT Assistant 0.2.0 Beta 1 - Setup 1

Corrects extraction in the Beta 1 Windows installer. The reduced application
contains empty npm scope directories such as `upstream/node_modules/@clack`.
.NET Framework's Windows ZIP writer records these with a trailing backslash.
The installer recognized only a forward slash as a directory marker and tried
to open an empty directory as a file, failing before the installation transaction.

The extractor now normalizes both separator styles before path validation and
directory detection. Path-traversal refusal and create-new file semantics remain.
The same extraction helper is used by setup and artifact verification.

## Download and update

Use this Setup 1 EXE instead of the original Beta 1 installer. Close TcXaeShell,
compare the download against `SHA256.txt`, then choose **Install / update** in
the existing installation directory and confirm same-user UAC.

The application remains `0.2.0-beta.1`, native version `0.2.0.7`. Its full
21,538-file manifest and all application binaries are unchanged. This is an
installer-only revision, not another application migration. A failed original
Beta 1 extraction did not reach the Host installation transaction. An already
successful Beta 1 installation does not need this installer revision.

The target and prerequisites remain TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on
Windows x64, .NET 8, .NET Framework and WebView2. The installer is unsigned.

## Verification

The synthetic wizard fixture reproduces the original extraction failure, then
passes with empty directories and files using both ZIP separator styles. It also
checks traversal refusal for both separators, argument forwarding, errors,
missing parameters, wizard defaults and the UAC manifest. The Windows CI job
now runs this regression.

Artifact verification invokes only the actual EXE's extraction helper in a fresh
temporary directory, then checks every extracted application file against the
manifest. It never invokes installer Main/Run, Host discovery, a real transaction
or UAC. The former archive-hash check alone did not cover extraction behavior.

`BUILD_PROVENANCE.json` distinguishes the unchanged Beta 1 application source
from this installer source revision. Existing release assets remain unchanged.
Installed IDE, language and real update/rollback acceptance remain separate.
