# Local development and operator setup

This is a development checkout, not a supported TwinCAT installer. Commands are
run at the repository root. Windows operator commands now require the built
native helper for safe JSON reads and Job Object ownership. Build it before
running `init`, `doctor`, tests, or `start` on Windows. See
[native prototype](NATIVE_PROTOTYPE.md) for build commands and the separate
32-/64-bit host targets. Native IDE installation acceptance remains open.
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
| Knowledge MCP | `6c9f1f629574e814abc0c321fd45bb54d81a90c1` |
| Engineering monorepo MCP | `3c303837e6d6f72bec1b9d38e778a7c32c61ae5c` |

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

Use a new ignored directory; do not reuse/change a customer checkout.

```sh
npm ci --prefix upstream --no-audit --no-fund
mkdir -p local-data/upstream
git clone https://github.com/Auda29/twincat-knowledge-mcp.git local-data/upstream/knowledge
git -C local-data/upstream/knowledge checkout --detach 6c9f1f629574e814abc0c321fd45bb54d81a90c1
(cd local-data/upstream/knowledge && uv sync --frozen --no-dev --python 3.13)
git clone https://github.com/Auda29/twincat-mcp-mono.git local-data/upstream/engineering
git -C local-data/upstream/engineering checkout --detach 3c303837e6d6f72bec1b9d38e778a7c32c61ae5c
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

No model is enabled by default. An optional `model: "provider/model"` additionally
requires `sourceCodeDisclosureAccepted: true`. Only the selected supported
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
