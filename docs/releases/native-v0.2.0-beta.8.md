# TwinCAT Assistant 0.2.0 Beta 8

Beta 8 consolidates the `main` branch with the updated composer profile menu
and clearer Debug availability diagnostics on top of Beta 7. No new runtime or
write capabilities are added.

## Changes since Beta 7

- The composer profile selector now uses the shared composer menu, shows the
  active profile marker after native approval, and explains how to create a
  PLC project chat when the current chat has no PLC project context.
- The Debug status banner distinguishes a missing project chat, failed PLC
  project association, confirmed offline state, unavailable status check and
  invalid or changed targets.

## Included scope

Choose **Analysis**, **Editor** or **Debug** in the chat composer. Each
privileged profile is tied to the selected chat and its exact PLC project.

- **Analysis** reads project code and Beckhoff documentation.
- **Editor** reads and previews supported open ST sections. Approve a diff
  to apply it to a supported open XML buffer. The change stays unsaved.
- **Debug** checks the existing TwinCAT PLC login and samples the exact
  scalar symbols you approve after confirming the AMS Net ID and ADS port.

Editor and Debug can both request selective PLC builds. Each build requires
a separate approval for the project, configuration and platform. The host
supplies its compiled interop types to the Engineering MCP to resolve
ambiguous interop references.

Beta 8 includes Engineering MCP 0.5.5 and Knowledge MCP 0.1.2. Standard
TwinCAT ST writes, PLC writes, forcing, download, activation, Online Change,
breakpoints and stepping remain outside this release scope.

## Installation and verification

Close TcXaeShell before running the unsigned Windows setup to update your
managed installation. This release targets TwinCAT 3.1.4024.78 /
TcXaeShell 15 x86 on Windows x64. Application version is `0.2.0-beta.8`
and native extension version is `0.2.0.14`.

Compare the installer with `SHA256.txt`. The built revision and completed
checks are in `BUILD_PROVENANCE.json`. Package and fixture tests run without
a live IDE project or PLC. The installed host workflows remain open for
live acceptance.
