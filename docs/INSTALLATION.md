# Local development and operator setup

For packaged installation, use the [Beta 5 release instructions](releases/native-v0.2.0-beta.5.md).
The commands below are for development checkouts, not prerequisites for using the EXE installer.

This is a development checkout, not a supported TwinCAT installer. Commands are
run at the repository root. Windows operator commands now require the built
native helper for safe JSON reads and Job Object ownership. Build it before
running `init`, `doctor`, tests, or `start` on Windows. See
[native prototype](NATIVE_PROTOTYPE.md) for build commands and the separate
32-/64-bit host targets. Native IDE installation acceptance remains open.

The 0.1.4 chat-integration candidate also requires the locked root and `upstream`
Node dependencies. `Build-Native.ps1` verifies and embeds the adapter against the
pinned OpenChamber assets before compiling. A mismatched distribution is refused.
See [project chat setup and consent](CHAT_INTEGRATION.md) for the native workflow
and `Test-ChatIntegration.ps1` for the actual WPF/WebView2/runtime fixture check.
Candidate `0.1.2` and its active-editor verification boundary are documented in
[M1.2](ACTIVE_EDITOR.md). The real compiled native view can be rendered with
synthetic inputs using `powershell.exe -NoProfile -STA -File scripts/Test-NativeView.ps1`
after building; that check does not attach to an IDE or initialize WebView2.
Native candidate `0.1.3` can also start the protected local runtime from Connect
chat after one-time [native chat configuration](NATIVE_CHAT.md#development-setup).

## Prerequisites and pinned dependencies

- Node.js 22 or newer and npm. Local checks passed with Node `26.11.1` and
  `22.23.3`; the container npm version is `11.20.0`.
- .NET SDK `8.0.425` (see `global.json`; latest 8.0 patch roll-forward allowed).
- Python 3.13 and `uv` for the knowledge upstream integration fixture.
- Chromium installed through Playwright for production-panel contract tests.
- Access to both private MCP repositories when cloning the pinned sources.

| Dependency | Exact pin |
| --- | --- |
| OpenChamber web / SDK | `2.1.1` |
| OpenCode CLI / schema | `2.0.23` |
| Knowledge MCP | `0.1.2`, `84c437aea8e27da930a246e7bb3ae310e8e5d31d` |
| Engineering monorepo MCP | `engineering-v0.5.3`; exact source revision in `upstream/engineering-lock.json` |

Root and `upstream/` npm lockfiles pin their own dependencies. MCP source trees
are deliberately not vendored. Review upstream licenses before redistribution.

## Build and test this repository

```sh
npm ci --no-audit --no-fund
npx playwright install chromium
npm run check

dotnet restore engineering/EngineeringSafety.sln --locked-mode
dotnet build engineering/EngineeringSafety.sln --no-restore --nologo -m:1 /p:UseSharedCompilation=false
dotnet run --project engineering/tests/Engineering.Safety.Tests/Engineering.Safety.Tests.csproj --no-build --no-restore
```

The C# tests are a console regression runner, **not** `dotnet test`. They exit
nonzero on any failure and print the checked count. .NET 8 pipe portability does
not establish native Windows/TwinCAT compatibility.

`npm run build` creates `dist/extension`; installation and test boundaries are in
[the extension guide](EXTENSION.md). Build output is ignored by Git.

## Install real upstreams

The versioned release pins are recorded in `upstream/knowledge-lock.json` and
`upstream/engineering-lock.json`. Build relocatable bundled inputs using
[the input guide](APPLICATION_INPUTS.md). Public builds and the standalone
Knowledge installer continue to refuse any future development-only pin.

For the Windows Assistant, install the pinned Knowledge release in its own
environment with [Install-Knowledge.ps1](../scripts/Install-Knowledge.ps1):

```powershell
$knowledge = .\scripts\Install-Knowledge.ps1
$knowledge.KnowledgeExecutable
```

This requires `gh` with access to the Knowledge repository and `uv`. The
[release lock](../upstream/knowledge-lock.json) pins the 0.1.2 wheel, dependency
requirements, tool schema, source receipt and checksums. The script verifies all downloaded artifacts,
installs hashed dependencies and the wheel, and checks both CLI and package
versions. The default path is
`%LOCALAPPDATA%\TwinCatAssistant\knowledge-runtime\0.1.2`; an absolute
`-InstallRoot` selects another new directory. Completed matching installations
are verified and reused. An incomplete or unrelated directory is refused.
The installer does not change Assistant configuration, existing environments,
the selected corpus, or provider/chat data. A failed installation leaves the
previous executable configured; use a new empty path to retry.

Then use [native MCP setup](NATIVE_MCP.md) to select the returned executable.
For development fixtures or non-Windows setups, use the source instructions
below. The protocol profile is unchanged; five typed tools extend the existing
four topic/status tools. Since version 0.1.1, marked abandoned loader caches recover after process termination;
it does not remove unmarked legacy caches or reduce full-corpus startup time.

Use a new ignored directory; do not reuse/change a customer checkout.

```sh
npm ci --prefix upstream --no-audit --no-fund
mkdir -p local-data/upstream
git clone https://github.com/Auda29/twincat-knowledge-mcp.git local-data/upstream/knowledge
git -C local-data/upstream/knowledge checkout --detach v0.1.2
(cd local-data/upstream/knowledge && uv sync --frozen --no-dev --python 3.13)
git clone https://github.com/Auda29/twincat-mcp-mono.git local-data/upstream/engineering
git -C local-data/upstream/engineering checkout --detach engineering-v0.5.3
(cd local-data/upstream/engineering && npm ci --no-audit --no-fund && npm run build)
```

Use your normal Git credential manager; do not put tokens in URLs or settings.
If an upstream installation already exists, validate its revision and build
instead of overwriting it. An installed knowledge server alone supplies no corpus.

## Real-process integration tests

```sh
TC_KNOWLEDGE_CHECKOUT="$(pwd)/local-data/upstream/knowledge" TC_ENGINEERING_CHECKOUT="$(pwd)/local-data/upstream/engineering" node --test --test-concurrency=1 tests/integration/upstream.test.mjs
```

Both paths must select the exact pinned, built checkouts. The fixture imports
upstream test builders, which are not a supported production corpus API. Its
fixture corpus and ST project are synthetic. This suite is separate from the
quick `npm run check` and requires installed upstream packages.

To verify the installed Knowledge wheel rather than an editable source install,
set `TC_KNOWLEDGE_EXECUTABLE` to the returned executable and
`TC_KNOWLEDGE_CHECKOUT` to the pinned source checkout, then run
`node --test tests/integration/knowledge-release.test.mjs`. It verifies CLI,
package metadata, MCP version/protocol, search-to-topic retrieval and one shared
broker using a synthetic corpus. The checkout supplies only the fixture builder;
the release environment supplies the imported package. No model call occurs.

## Initialize operator state

Create an operator JSON file outside tracked source (for example under
`local-data/`). `{}` is valid for an unconfigured panel/server bootstrap. To
configure read-only MCPs, use nonempty **absolute** paths:

```json
{
  "knowledge": {
    "executable": "/absolute/knowledge/.venv/bin/twincat-knowledge",
    "config": "/absolute/operator/knowledge.toml"
  },
  "engineering": {
    "entrypoint": "/absolute/engineering/packages/mcp/dist/index.js",
    "config": "/absolute/operator/engineering.json"
  }
}
```

Engineering configuration must explicitly have `readOnly: true` and no nonempty
`writeAllowlist`. The launcher requires `engineering.enabled: true` and
`engineering.backend: "configuredProjectFiles"`; native, implicit and runtime
backends are rejected. The integration test shows a synthetic saved-file configuration.

```sh
npm run assistant -- init --settings /absolute/operator/settings.json --state /absolute/new/assistant-state
npm run assistant -- doctor --state /absolute/new/assistant-state
npm run assistant -- start --state /absolute/new/assistant-state --port 3123
```

`init` never adopts/overwrites an existing directory. `doctor` rejects policy
changes instead of repairing them; corpus status remains `not_probed` until a
real MCP query. Do not equate valid config with corpus or IDE availability.

`start` requires a UI password through `OPENCHAMBER_UI_PASSWORD`. Supply it via
your local secret-management mechanism; do not commit it or place it in command
arguments. The UI is at `http://127.0.0.1:3123`. The command stays in the foreground;
Ctrl+C stops the owned helper group. Never select a PLC project as the launch
working directory; the launcher uses its empty isolated workspace.

No model is enabled by default. Optional `provider: "openai"` allows explicit
provider consent before choosing a model; optional `model: "provider/model"`
also sets the default model. Both require `sourceCodeDisclosureAccepted: true`,
and a provider/model pair must agree. The native Details selector follows this
order because upstream hides denied providers even after sign-in.
Only the selected supported
provider environment key is forwarded (OpenAI, Anthropic or Google); other
credentials and arbitrary OpenCode overrides are discarded. This consent is
not evidence of successful model use or a provider's retention policy.

State contains settings, policy, isolated upstream data/cache/session directories
and the launcher lock. Keep it private; local backups and logs may contain source.
After a supervisor crash, `launcher.lock` deliberately blocks restart. Inspect
its owner PID and the old helper processes; stop only confirmed owned instances
before manually removing that exact lock. No automatic PID-based recovery is
implemented. Operator-selected executables/MCP scripts are trusted code, and a
version check does not verify their provenance; never accept model-supplied paths.
No production retention/installer/update policy is promised. Do not change
security policies through the general upstream UI; initialize a new reviewed
state after intentional configuration changes.
