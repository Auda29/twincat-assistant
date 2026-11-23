# TwinCAT Assistant 0.2.0 Beta 9

Beta 9 updates the bundled Engineering MCP to `0.5.7`, fixing two real TwinCAT
3.1.4024.78 host issues that blocked Debug login detection and PLC project
association in Beta 8.

## Changes since Beta 8

- **Engineering MCP `0.5.7` (`engineering-v0.5.7`, `375dcfe8102a20baebe8c542fc7d4d837107ecff`):**
  - Accepts TwinCAT's real `<TreeItem><PlcProjectDef><ProjectPath>…</ProjectPath></PlcProjectDef></TreeItem>`
    XML shape (alongside legacy `<PlcDef>`) while keeping strict canonical-path
    and ambiguity guards intact.
  - Replaces `Type.InvokeMember` (`IDispatch`) calls in the association and build
    adapters with exact `PropertyInfo.GetValue` / `MethodInfo.Invoke` vtable
    calls, eliminating the `AccessViolationException` on `ITcSmTreeItem.ChildCount`.
- Verified `session.describe`, `project.association` and `project.online` against
  an isolated synthetic TcXaeShell 15 x86 / TwinCAT 3.1.4024.78 host before
  packaging.

## Included scope

Choose **Analysis**, **Editor** or **Debug** in the chat composer. Each
privileged profile is tied to the selected chat and its exact PLC project.

- **Analysis** reads project code and Beckhoff documentation.
- **Editor** reads and previews supported open ST sections. Approve a diff
  to apply it to a supported open XML buffer. The change stays unsaved.
- **Debug** checks the existing TwinCAT PLC login and samples the exact
  scalar symbols you approve after confirming the AMS Net ID and ADS port.

Editor and Debug can both request selective PLC builds. Each build requires
a separate approval for the project, configuration and platform.

Beta 9 includes Engineering MCP 0.5.7 and Knowledge MCP 0.1.2. Standard
TwinCAT ST writes, PLC writes, forcing, download, activation, Online Change,
breakpoints and stepping remain outside this release scope.

## Installation and verification

Close TcXaeShell before running the unsigned Windows setup to update your
managed installation. This release targets TwinCAT 3.1.4024.78 /
TcXaeShell 15 x86 on Windows x64. Application version is `0.2.0-beta.9`
and native extension version is `0.2.0.15`.

Compare the installer with `SHA256.txt`. The built revision and completed
checks are in `BUILD_PROVENANCE.json`. Package and fixture tests run without
a live PLC. Full installed-host workflow acceptance remains open.
