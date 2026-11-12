# TwinCAT Assistant 0.2.0 Beta 5

Beta 5 integrates the released Engineering and Knowledge MCP updates and keeps
experimental source editing inactive. The full source-development code remains
in the repository for later work; this release does not enable it.

## Changes

- Explicitly enable project-bound **Build tools** for one chat. Each selective
  PLC build requires a separate native confirmation of its target,
  configuration and platform. Analysis remains the default read-only profile.
- Build output and diagnostics belong to the requested build. A clickable
  diagnostic requires unchanged source/project evidence and one exact current
  native ErrorItem; navigation has no path-only fallback.
- Knowledge adds symbol and library search/get plus error-code lookup, for nine
  tools in total. Results retain source citations, uncertainty and ambiguity.
  Documented versions do not establish installed library versions.
- Pinned chat history survives transient build/save/frame context invalidation
  and temporary host invisibility. List actions pause until the exact project
  context is confirmed again. Unpinned lists retain normal dismissal behavior.
- Source discovery, live source read/preview and source apply/rollback/journal
  tools are absent from the release catalog and UI. Native dispatch additionally
  refuses source operations and the provider receives no editor accessor.
  Existing read-only editor context and explicit attachments remain available.
- Exact configuration migration removes the former development source
  permissions while retaining chats, settings and sign-ins. Unknown modified
  policies remain refused.

## Installation and verification

Close TcXaeShell normally, then run the setup EXE in the existing managed
installation directory, normally `C:\Program Files\TwinCatAssistant`. No
uninstall is needed. Application version is `0.2.0-beta.5`; native assembly,
file and VSIX version is `0.2.0.11`. Compare the installer against `SHA256.txt`.

The supported installation target remains TwinCAT 3.1.4024.78 / TcXaeShell 15
x86 on Windows x64. The installer is unsigned. It does not close the IDE or
perform activation, download or other PLC/runtime actions.

`BUILD_PROVENANCE.json` records the actual build source and completed checks.
Package, installer and rollback fixtures use isolated synthetic state. They do
not certify installed-host behavior or real provider sign-ins after rollback.
The operator performs the installed-host live test after this release.

## Remaining limits

Normal TwinCAT ST writes still lack a verified exact-section transaction;
retained XML-buffer development support does not close that gap. Cancellation
records a request and does not invoke the IDE-wide build cancel. Unknown
submitted outcomes retain their guard and are not automatically retried.

Real-host build/event/dirty-state/navigation acceptance and real-corpus typed
extraction quality and performance remain open. Properties and unsupported
editors are not enabled. TwinCAT 4026/Shell64 remains unaccepted and unavailable
as an installer target. No workstation installation is part of the release build.

Engineering 0.5.3 retains two disclosed dependency advisories, one high and one
low, concerning availability. See its [release notes](https://github.com/Auda29/twincat-mcp-mono/releases/tag/engineering-v0.5.3).
The local transport scope does not establish a universal mitigation for those
dependency paths.

This beta is published as GitHub Latest at the operator's request; GitHub's
Pre-release flag is disabled.
