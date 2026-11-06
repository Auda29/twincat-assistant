# Local alpha delivery candidate, 2026-11-05

The complete x64 application package and installer candidate were built on
`feat/alpha-delivery-foundations`. The owner confirmed permission to bundle
Knowledge MCP 0.1.1 with Assistant; the
[permission record](../upstream/permissions/knowledge-0.1.1.txt) is included in
the Knowledge runtime and its SHA-256 is recorded in its build provenance.
Third-party licenses and notices are retained separately.

## Artifacts

- Application: `artifacts/delivery-20261105/application/`, version
  `0.1.0-dev.1`, backend `win32/x64`, with 22,442 manifest-listed files.
- Installer: `artifacts/delivery-20261105/installer/TwinCAT-Assistant-0.1.0-dev.1-x64-setup.exe`,
  524,043,776 bytes.
- Installer SHA-256:
  `62497e2c3f3d6c67bfc2b440aadd123c4eca169fba217e29d186002ddb510677`.
- Target: the first alpha TcXaeShell/x86 host with TwinCAT 3.1.4024.78.
  Backend and native host architecture are separate. Package version
  `0.1.0-dev.1` is the existing application version, not a new host acceptance
  or a statement that all roadmap gates are closed.

These are ignored local build outputs, not a published release. The installer
is unsigned. It was inspected, not installed or executed against a real host.

## Build and verification

After the existing Engineering and runtime-notice input builds:

```powershell
$permission = (Resolve-Path upstream/permissions/knowledge-0.1.1.txt).Path
$digest = (Get-FileHash -LiteralPath $permission -Algorithm SHA256).Hash.ToLowerInvariant()
./scripts/Build-ApplicationInputs.ps1 -Component Knowledge -KnowledgeLicense $permission -KnowledgeLicenseSha256 $digest
./scripts/Build-ApplicationPackage.ps1 -OutputDirectory (Join-Path (Get-Location).Path 'artifacts/delivery-20261105/application')
node scripts/test-built-application.mjs artifacts/delivery-20261105/application
./scripts/Build-WindowsInstaller.ps1 -PackageDirectory (Resolve-Path artifacts/delivery-20261105/application).Path -OutputDirectory (Join-Path (Get-Location).Path 'artifacts/delivery-20261105/installer')
./scripts/Test-WindowsInstallerPayload.ps1 -InstallerPath artifacts/delivery-20261105/installer/TwinCAT-Assistant-0.1.0-dev.1-x64-setup.exe
```

Use fresh output paths when repeating the build; existing outputs are refused.
The real built package passed protected loopback startup, actual saved-project
Engineering MCP reading, modern Knowledge MCP corpus-status reading with no
corpus, restart with preserved synthetic state, and final full package integrity
verification. No provider call, PLC action or real project was involved.

The executable's embedded application inventory was read without calling its
entrypoint: all 22,442 sizes and SHA-256 hashes match, and required installer
scripts and host targets are present. The Windows installer workflow now runs
both the real package smoke check and embedded-payload verification before
uploading a supplied staging artifact's installer. The original workflow lacked an input producer. The follow-up
[actual CI run](https://github.com/Auda29/twincat-assistant/actions/runs/42034006930)
now passed with a reviewed import of this inventory and Core refreshed from
commit 46f96a17697128721c6c1651a8a0f09dc2297d43, including recoverable profile
updates. Its new EXE is a separate candidate; the local EXE hash above does not
identify it. See [CI evidence and host handoff](ALPHA_CI_HOST_HANDOFF.md).

## Remaining acceptance

Real installation, update, rollback and in-process host behavior remain subject
to [host acceptance](HOST_ACCEPTANCE_MATRIX.md). The current manually installed
prototype is not silently adopted or overwritten. Known prompt-policy and
interruption limits remain in [relocation](INSTALLER_RELOCATION.md) and the
[installer contract](INSTALLER_CONTRACT.md). Actual model answers and embedded
ST rendering remain open under [profile prompts](PROFILE_PROMPTS.md).
