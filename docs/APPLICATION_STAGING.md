# TwinCAT Assistant application staging and installed runtime

`Build-ApplicationPackage.ps1` creates a relocatable staging directory, not an
installer or host acceptance:

```powershell
.\scripts\Build-ApplicationPackage.ps1 -OutputDirectory 'C:\staging\assistant-package'
```

The output must be an absolute normalized path that does not exist; its parent
must exist. Build on Windows with Node 22+, npm, Git, the pinned source-runtime
build and receipt described in [upstream README](../upstream/README.md), and
Release builds of WindowsHost and the x86 IDE host. Build the host **after** the
installation-selection changes. Node and its npm installation must have the
normal Windows layout. The builder also requires these explicit prebuilt inputs:

- `artifacts/application-inputs/node/LICENSE`: the official license and notices
  for the exact Node binary used by the builder, reviewed for redistribution.
- `artifacts/application-inputs/webview2/LICENSE.txt` and `NOTICE.txt`: the
  pinned WebView2 SDK's redistributed license and notice.
- `artifacts/application-inputs/knowledge/`: a relocatable Knowledge 0.1.1
  runtime with `twincat-knowledge.exe` at its root, its Python runtime,
  dependencies, a root `LICENSE`, other applicable notices and
  `application-manifest.json` schema 1.
- `artifacts/application-inputs/engineering/`: a relocatable Engineering MCP
  runtime with `packages/mcp/dist/index.js`, its dependencies, root `LICENSE`,
  other applicable notices and its
  own schema-1 `application-manifest.json`. Its manifest version is the pinned
  revision `3c303837e6d6f72bec1b9d38e778a7c32c61ae5c`.

Both input manifests must match the backend architecture and verify in full;
the Knowledge input manifest version must be `0.1.1`. The builder probes the
staged Knowledge executable's version. A manifest and version probe are not a
proof that an arbitrary Python environment is relocatable or was produced from
its claimed source. Supply independently built, reviewed relocatable inputs;
ordinary Python virtual environments with embedded absolute paths are not a
sufficient input. The builder fails if any of these inputs are missing and does
not produce a completed output directory. Its exact input-manifest format is the
same as the application manifest below; the manifest is excluded from its own
file list. The input directories must contain no credentials, operator corpus,
profiles, engineering projects or links.

The builder validates the source receipt and its inputs, exact upstream
revisions and only the reviewed server overlay. It copies tracked Chat server
files and generated `dist` and `server/built-in-extensions` from the source
build; the source receipt authenticates its named inputs and three artifact
hashes, not every generated asset. The builder installs both locked production
npm dependency graphs into an isolated temporary profile/cache, probes the
actual Core, Chat, Knowledge and WindowsHost versions, writes and rechecks the
final manifest, and renames staging into place. It needs network access to the
lockfile's npm sources and may run their installation scripts. The package
inventory is an integrity check, not publisher authentication. License and
redistribution review remains an integration gate.

The resulting fixed paths are:

- `host/x86/`: unpacked x86 host, VSIX manifest, pkgdef and required WebView2
  files. The builder excludes `TCatSysManagerLib.dll`, `.vsix` and debug files.
  The installer obtains matching interop from the validated local TwinCAT host.
- `runtime/node/node.exe` and `licenses/node/LICENSE`: the packaged Node binary
  and its supplied license; `runtime/opencode/opencode.exe`: source-built Core.
- `runtime/mcp/knowledge/twincat-knowledge.exe` and
  `runtime/mcp/engineering/packages/mcp/dist/index.js`: packaged MCP entries,
  their input manifests, dependencies and licenses. `runtime/mcp/default-settings.json`
  supplies the empty default selection when no operator MCP settings exist.
- `src/`: tracked own Core modules, including later-added prompt modules, and the
  package marker; `upstream/node_modules/@openchamber/web/`: pinned Chat server
  and source-built UI; root and upstream `node_modules/`: locked dependencies.
- `native/WindowsHost/bin/Release/net8.0-windows/`: Windows ownership and JSON
  helper; `runtime/source-receipt.json`, upstream locks and `licenses/`: build
  inputs and notices; `application-manifest.json`: complete package inventory.

For the x86 TcXaeShell 15 / TwinCAT 3.1.4024.78 target, the installer writes
`assistant-installation.json` beside the loaded host assembly with exactly
`{schemaVersion:1,targetId:"tcxaeshell-x86-tc4024",version,applicationRoot}`.
The host reads this selector from its own extension directory, refuses malformed
or mismatched records, checks the selected manifest version and hashes the fixed
Node, launcher, MCP and helper entries before using them. It runs the package's
Node and `src/native-chat.mjs`; the Node entry verifies the entire package before
preparing user state or starting processes. The other native entries are
`src/native-chat-model.mjs`, `src/native-mcp.mjs`, and `src/native-knowledge.mjs`.
Runtime/helper overrides are rejected in package mode. Without a selector the
existing development configuration and runtime selection continue to apply; an
installer-owned extension with a missing selector fails closed.

An existing `native-chat.json` still supplies `StateRoot`, `DotNetRoot` and
optional `McpSettingsPath`; installed selection replaces its development Node
and repository paths in memory only. When the config is absent, the default
state is `%LOCALAPPDATA%\TwinCatAssistant\operator-state`; .NET defaults to the
system Program Files `dotnet` directory. If an old configured .NET root no longer
exists, the same system default is used. A compatible external .NET 8 runtime is
required. Credentials remain in the existing DPAPI store. Existing MCP settings
and an optional Knowledge corpus config are retained; in package mode only the
executable/entrypoint paths resolve to the selected package. Without an existing
MCP settings path, the package's empty default enables the two packaged entries.
Actual corpus, provider and tool readiness remain separately reported. The
existing JSON stdin protocol remains `state`, `port`, `password`, optional
`mcpConfig`; `ready` confirms only the protected loopback endpoint. Closing
stdin stops the owned process chain. Installation selection and rollback do not
rewrite `native-chat.json`, credentials or chat data. Existing managed runtime
configuration still follows its own update rules, and prompt-policy rollback
compatibility is an independent integration gate.

Schema 1 contains exactly `schemaVersion`, `version`, `platform` (`win32`),
`architecture` and `files`, an ordered array of `{path,size,sha256}`. Paths are
relative with `/`; hashes are lowercase SHA-256; the manifest itself is
excluded. Verification rejects changed, extra, missing, linked and special files.
Backend architectures use Node names `x64`, `arm64` and `ia32` (32-bit x86),
independently of the x86 IDE host. The current installer contract accepts only
`x64` and `arm64`; `ia32` is not a claim of installer support or acceptance.
Matching source and runtime artifacts are needed for each architecture. A
manifest cannot authenticate code when an attacker can replace it and the files;
the installer must separately establish package trust and validate extraction.
Checking the complete package adds startup I/O; installed-host timing is open.

`node --test --test-concurrency=1 tests/application-manifest.test.mjs tests/integration/application-package.test.mjs tests/integration/application-package-builder.test.mjs tests/integration/installation-selection.test.mjs`
exercises tampering, synthetic package construction and relocation, native
selector validation, rollback selection and synthetic process startup. Windows
fixtures need a built WindowsHost and .NET 8 SDK at `local-data/dotnet8`, or
`TC_TEST_DOTNET_ROOT` set to its root. A prior real-source backend-only staging
was relocated and reached protected native readiness with synthetic state and no
MCP or provider. That predates the now-required bundled host and MCP inputs and
is not an end-to-end installer, MCP, provider or live IDE acceptance. Real
relocatable MCP artifacts, full package build and startup, install/update and
rollback integration, and installed-host checks remain open.
