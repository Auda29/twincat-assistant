# Alpha candidate 0.2.0-preview.2

This candidate combines the distribution feature boundary and product naming
with the installer-selected runtime, profile recovery journal and direct-host
acceptance tooling. It has actual code changes relative to the first CI
candidate at `46f96a17697128721c6c1651a8a0f09dc2297d43`; it is not an
update/rollback fixture made by relabelling that package.

The application package uses SemVer `0.2.0-preview.2`. Both VSIX manifests and
the native host AssemblyVersion/FileVersion use numeric `0.2.0.2`. The 64-bit
payload remains an unaccepted prototype target. Numbering does not grant host
support or a completed alpha acceptance. The installed unowned prototype stays
`0.1.9` until a separately recorded installer transaction changes it.

The first real Windows CI candidate is from
[run 42034006930](https://github.com/Auda29/twincat-assistant/actions/runs/42034006930).
Its `reviewed-input` and hosted `windows-2022` jobs both succeeded. The hosted
job ran protected packaged startup, both real MCP probes on synthetic data,
restart/state preservation, installer build and embedded-payload verification.
Artifact `13418347605`, `windows-package-candidate`, has 518586701 bytes and
ZIP SHA-256 `1d62ed49619ac6d9debb61e6b02f420a39f5f26ff0af9b7751c1235763c73ef1`.
That evidence applies to its exact CI source SHA above, not to this later
candidate. A new build and CI run are required for this version.

Live installation, actual update/rollback with the same isolated state, visible
host UI, member editors and first-after-logon startup remain acceptance gates.
See [host evidence](../HOST_ACCEPTANCE_20261105.md),
[delivery scope](../DELIVERY_SCOPE.md) and
[the installation handoff](../ALPHA_CI_HOST_HANDOFF.md).
