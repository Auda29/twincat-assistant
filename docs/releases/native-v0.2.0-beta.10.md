# TwinCAT Assistant 0.2.0 Beta 10

Beta 10 streamlines the **Debug** profile so that selecting Debug in a bound
project chat connects automatically to the logged-in PLC's configured target
(read-only) without requiring a manual modal dialog for ADS port or symbol
allowlist entry.

## Changes since Beta 9

- **Automatic read-only Debug connection (enabled by default):**
  - When you are logged into your PLC in TwinCAT and select **Debug** in the
    chat composer, the Assistant automatically uses the bound PLC project's
    configured AMS Net ID and ADS port (`851–899`) with read-only access to all
    symbols (`symbolScope: "all"`) and displays a non-blocking status notice
    instead of opening a modal dialog.
  - Added the **Connect Debug automatically** (*Debug automatisch verbinden*)
    toggle to the Assistant's `...` menu, persisted in `preferences.json` as
    `autoDebug` (default `true`, backward-compatible with existing preference
    files). Turning it off—or working with a project that omits a canonical
    `<AdsPort>`—falls back to the manual confirmation dialog with any detected
    ADS port pre-filled.
  - Debug remains strictly read-only (`readOnly: true`, empty write allowlist,
    up to 8 scalar symbols per read) and rechecks the TwinCAT login, project
    association and configured target address before, during and after reads.
- **Engineering MCP `0.5.8` (`engineering-v0.5.8`, `4869c39260a88c031c3dbccb72750ce10222edfd`):**
  - `project.online` reports `configuredAdsPort` (`851–899` or `null`) from
    validated `<PlcProjectDef><AdsPort>` XML and rechecks the XML after login
    observation.
  - `debug.context` accepts host-only `symbolScope: "all"` (with `symbols`
    omitted) alongside the existing explicit symbol allowlist mode.
- **In-place profile upgrade:** Existing Beta 7–9 managed configurations upgrade
  in place to the updated Debug system prompt without losing operator state.
- Verified `project.online` (`configuredAdsPort: 851`), `session.describe` and
  `project.association` against an isolated synthetic TcXaeShell 15 x86 /
  TwinCAT 3.1.4024.78 host before packaging.

## Included scope

Choose **Analysis**, **Editor** or **Debug** in the chat composer. Each
privileged profile is tied to the selected chat and its exact PLC project.

- **Analysis** reads project code and Beckhoff documentation.
- **Editor** reads and previews supported open ST sections. Approve a diff
  to apply it to a supported open XML buffer. The change stays unsaved.
- **Debug** checks the existing TwinCAT PLC login and samples scalar symbols
  read-only from the connected PLC target (automatically by default, or via
  manual target/symbol confirmation when disabled in the `...` menu).

Editor and Debug can both request selective PLC builds. Each build requires
a separate approval for the project, configuration and platform.

Beta 10 includes Engineering MCP 0.5.8 and Knowledge MCP 0.1.2. Standard
TwinCAT ST writes, PLC writes, forcing, download, activation, Online Change,
breakpoints and stepping remain outside this release scope.

## Installation and verification

Close TcXaeShell before running the unsigned Windows setup to update your
managed installation. This release targets TwinCAT 3.1.4024.78 /
TcXaeShell 15 x86 on Windows x64. Application version is `0.2.0-beta.10`
and native extension version is `0.2.0.16`.

Compare the installer with `SHA256.txt`. The built revision and completed
checks are in `BUILD_PROVENANCE.json`. Package and fixture tests run without
a live PLC. Full installed-host workflow acceptance remains open.
