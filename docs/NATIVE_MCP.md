# Native read-only MCP integration

Preview release **0.1.8** connects the existing Knowledge and TwinCAT MCP servers to
the native OpenChamber/OpenCode chat. It also includes the 0.1.7 docked-window
editor-binding fix. The x86 operator payload was updated to 0.1.8 on 2026-10-28.
All 13 installed payload hashes and the preserved 0.1.7 rollback copy are
verified. Both MCP paths are configured without a Knowledge TOML; actual server
calls using those paths pass with a synthetic project and no model request.
Installed IDE startup and project-context acceptance remain open. See the
[source-only release notes](releases/native-v0.1.8.md).

## Setup

First configure the protected chat runtime using
[Configure-NativeChat.ps1](../scripts/Configure-NativeChat.ps1). Install/build the
two MCP checkouts at the revisions in [Installation](INSTALLATION.md):
Knowledge `6c9f1f629574e814abc0c321fd45bb54d81a90c1`, engineering
`3c303837e6d6f72bec1b9d38e778a7c32c61ae5c`. Engineering needs its built workspace
dependencies, not only a copied `dist/index.js`.

Run from the Assistant checkout, with absolute paths to those installations:

```powershell
.\scripts\Configure-NativeMcp.ps1 `
  -KnowledgeExecutable 'C:\dev\twincat-knowledge-mcp\.venv\Scripts\twincat-knowledge.exe' `
  -EngineeringEntrypoint 'C:\dev\twincat-mcp\packages\mcp\dist\index.js'
```

If an activated documentation corpus exists, add
`-KnowledgeConfig 'C:\corpora\knowledge.toml'`. This is optional. No corpus is
currently configured for the operator. Setup neither downloads documentation
nor ingests/activates a corpus. Without a TOML configuration, the native project
chat explains that no corpus is configured; the server reports `not_available`
for corpus status. Saved-project tools remain usable.

The script writes `%LOCALAPPDATA%\TwinCatAssistant\native-mcp.json` and adds its
path as `McpSettingsPath` in `native-chat.json`. It preserves the other host
settings and backs up replaced JSON files. Existing unrelated JSON files are
refused. MCP settings contain paths only, with this exact schema:

```json
{
  "knowledge": {
    "executable": "C:\\dev\\twincat-knowledge-mcp\\.venv\\Scripts\\twincat-knowledge.exe"
  },
  "engineering": {
    "entrypoint": "C:\\dev\\twincat-mcp\\packages\\mcp\\dist\\index.js"
  }
}
```

Reconnect the Assistant after setup or configuration changes. A connected
Knowledge process does not establish that an activated corpus is available.
Connection states appear under **Diagnose**; the project agent checks corpus status
before documentation research and uses the server's returned sources.

## Project and disclosure boundaries

The native host resolves a project identity independently of a supported ST
source editor: IDE PID, solution path and exact `.plcproj` path. It first uses
the active document's project, then selected project metadata, then a bounded
unique-PLC fallback. Ambiguous/incomplete resolution clears project context.
The fallback visits project metadata only; it does not enumerate POU source.
COM access stays on the IDE UI thread. DUTs/methods can therefore retain project
context without becoming supported native source attachments.

Each solution/project/MCP-configuration combination gets an immutable private
chat location under `<StateRoot>\projects\<hash>`. OpenCode never runs with the
customer PLC folder as its chat working directory. Generated configuration
selects `configuredProjectFiles`, `readOnly: true`, an empty write allowlist and
exactly one `.plcproj`. It reads that project's referenced saved files. This is
the existing backend's project boundary, not a filesystem sandbox; legitimate
project references can point outside the project folder.

Project chats bind both their metadata and actual OpenCode directory to the
current IDE/project. Delayed setup, session creation and attachment recheck
identity. A different project, closed solution, another live IDE owner or older
workspace prevents prompting in that thread. History stays readable; a new
project chat uses the current configuration. Unknown changes to generated
policies are refused rather than overwritten.

General chats use the original private workspace and have only Knowledge MCP
tools. Native setup removes a legacy global engineering configuration before
launch, preserving provider/model settings and profile-integrity checks. A
native launch without MCP setup refuses a legacy global engineering profile.
An existing project conversation cannot be relabelled as general to retain its
engineering tools.

Submitting a project question authorizes the selected model to retrieve saved
project files through the reviewed read tools. Opening a project does not submit
source to a model. **Anhängen** separately stages the supported active POU's
Automation Interface snapshot; optional automatic attachment remains off by
default. This snapshot is not verified unsaved editor-buffer text and can differ
from the saved files returned by engineering tools. There are no PLC writes,
implicit saves, builds, ADS/runtime backends or machine-control actions. Shell
and general filesystem tools remain denied by the existing agent policy.

## Verification

Local candidate checks on 2026-10-27 pass: 51 Node tests, 8 browser contracts,
80 shared native policy/context/chat checks, 28 WPF view checks, and both x86
and amd64 host builds without warnings/errors. The real MCP process test passes,
as do 37 compiled-control/WebView2/runtime checks with no Knowledge corpus.
Earlier real MCP checks also use the synthetic activated corpus. These counts
describe component evidence, not installed host acceptance.

Portable checks include generated-policy integrity, separate same-name PLC
projects, provider-policy preservation, MCP metadata filtering and invalid-path
refusal. On Windows, the setup test uses a temporary `LOCALAPPDATA` and confirms
that host settings survive and unrelated JSON cannot be overwritten.

```powershell
npm run check
# Optional real-process test; skipped unless both checkouts are supplied:
$env:TC_KNOWLEDGE_CHECKOUT = 'C:\dev\twincat-knowledge-mcp'
$env:TC_ENGINEERING_CHECKOUT = 'C:\dev\twincat-mcp'
npm run test:native-mcp
```

The real-process test prepares a synthetic activated Knowledge corpus and two
synthetic PLC projects containing identically named POUs. It checks actual
source returns, foreign-project refusal, structured documentation sources,
missing-corpus status and both MCP connections through the protected native
runtime. The compiled WPF/WebView2 fixture additionally checks project switching,
reconnection, project-only context and general-chat isolation:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass `
  -File scripts/Test-ChatIntegration.ps1 `
  -DotNetRoot 'C:\path\to\pinned-dotnet' `
  -McpSettingsPath 'C:\fixtures\native-mcp.json'
```

All fixtures use isolated state, synthetic PLC files and synthetic provider
credentials. They make no model request. Passing these tests does not prove
installed IDE lifecycle behavior, actual documentation quality, live unsaved
buffer semantics, PLC compilation or machine behavior. Operator acceptance with
a real activated corpus and live amd64 host remains open. Source-only release
and Beckhoff interop redistribution constraints are unchanged.
