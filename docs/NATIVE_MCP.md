# Native read-only MCP integration

Native 0.1.9 adds [automatic documentation setup](KNOWLEDGE_SETUP.md). Users can
prepare installed Beckhoff help from **Wissenssuche einrichten**; a manual TOML
is an optional advanced configuration. The 0.1.8 installation record below is
historical.

Preview release **0.1.8** connects the existing Knowledge and TwinCAT MCP servers to
the native OpenChamber/OpenCode chat. It also includes the 0.1.7 docked-window
editor-binding fix. The x86 operator payload was updated to 0.1.8 on 2026-10-28.
All 13 installed payload hashes and the preserved 0.1.7 rollback copy are
verified. Both MCP paths are configured without a Knowledge TOML; actual server
calls using those paths pass with a synthetic project and no model request.
Installed IDE startup and project-context acceptance remain open. See the
[source-only release notes](releases/native-v0.1.8.md).

## Setup

### Large-object attachment fallback

Native chat attaches a complete context snapshot up to 16,000 characters,
including metadata. Above that limit it stages a **gespeicherte Referenz**
instead of refusing the attachment or truncating source. This reference carries
the solution, PLC project, document moniker, tree path, owning POU and member,
but no declaration or implementation. The same explicit attachment/automatic
attachment setting and project/session checks apply. Staging alone does not
submit a message or call an MCP tool.

After submission, the model is instructed to resolve the exact saved object
through `engineering_plc_list_pous` and read its returned ID with
`engineering_plc_read_pou`, checking project, source file and member identity.
Unavailable tools or a missing/ambiguous saved object must be reported rather
than replaced by another object. The reference reads saved files at tool-call
time; it does not freeze their contents, save the editor or include unsaved
changes. Small objects continue to use the existing direct snapshot format.

Portable policy and composer regressions cover this fallback. Run
`node --test tests/integration/saved-object-reference.test.mjs` with
`TC_ENGINEERING_CHECKOUT` pointing to the built pinned engineering checkout
to verify actual MCP listing and ID-based reads of a large synthetic POU and
its method. This test uses no provider or PLC runtime. Actual model tool
selection and the installed native UI still require host acceptance.

### Configuration

First configure the protected chat runtime using
[Configure-NativeChat.ps1](../scripts/Configure-NativeChat.ps1). Install Knowledge
and build the engineering checkout at the revisions in [Installation](INSTALLATION.md):
Knowledge `0.1.1` at `fff24f410efa687bf39fe6829f5d068b1e80cf8f`, engineering
`3c303837e6d6f72bec1b9d38e778a7c32c61ae5c`. Engineering needs its built workspace
dependencies, not only a copied `dist/index.js`.

The Windows Knowledge release can be installed independently of a development
checkout. Run from the Assistant checkout:

```powershell
$knowledge = .\scripts\Install-Knowledge.ps1
.\scripts\Configure-NativeMcp.ps1 `
  -KnowledgeExecutable $knowledge.KnowledgeExecutable `
  -EngineeringEntrypoint 'C:\dev\twincat-mcp\packages\mcp\dist\index.js'
```

If an activated documentation corpus exists, add
`-KnowledgeConfig 'C:\corpora\knowledge.toml'`. This is an optional advanced
configuration. Setup neither downloads documentation nor imports a corpus.
Without an explicit TOML, native 0.1.9 creates its private configuration and
offers **Wissenssuche einrichten** for installed help. Before activation, the
server reports `not_available`; saved-project tools remain usable. The local
German operator help is now prepared with 49,970 articles.

The script writes `%LOCALAPPDATA%\TwinCatAssistant\native-mcp.json` and adds its
path as `McpSettingsPath` in `native-chat.json`. It preserves the other host
settings and backs up replaced JSON files. Existing unrelated JSON files are
refused. MCP settings contain paths only, with this exact schema:

```json
{
  "knowledge": {
    "executable": "C:\\Users\\example\\AppData\\Local\\TwinCatAssistant\\knowledge-runtime\\0.1.1\\.venv\\Scripts\\twincat-knowledge.exe"
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

For an upgrade, retain an existing `KnowledgeConfig` argument if one is
configured, and keep the same engineering entrypoint. The previous executable
and corpus remain in place. The selected release is recorded in
[knowledge-lock.json](../upstream/knowledge-lock.json); a CLI version probe and
an MCP initialization response must both identify 0.1.1. Changing a development
checkout alone is not a release installation. Package metadata can otherwise
remain at the old version while editable source already reports a new one.

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
