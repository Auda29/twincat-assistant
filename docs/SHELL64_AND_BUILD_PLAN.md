# Next priorities: 4026/Shell64, then build diagnostics

Operator priority, 2026-11-10:

1. TwinCAT 3.1 Build 4026 with TcXaeShell64.
2. Explicit project build and navigable compiler diagnostics.
3. Full Visual Studio 2022 integration after Shell64; further hosts separately.

The operator confirms 4026/Shell64 is not installed. Read-only inventory still
finds 4024.78 in TcXaeShell x86 and Visual Studio 2019. Package Manager is present;
its presence alone does not make the installed XAE a 4026 installation. The
collector now distinguishes that from a matching classic 4024 setup record.
No migration, installation, IDE launch or PLC project build was performed.

## Shell64 work

Beckhoff documents TcXaeShell64 as VS2022-based and identifies its DTE ProgID as
`TcXaeShell.DTE.17.0`, distinct from full VS2022's `VisualStudio.DTE.17.0`.
See [system requirements](https://infosys.beckhoff.com/content/1033/tc3_overview/6162419083.html)
and [DTE identities](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842697995.html).

The amd64 project now explicitly targets x64 rather than AnyCPU. That is a build
foundation, not Shell64 installation support. Its VSIX still has an unverified
Community installation target, and installer/selector paths are currently x86-only.
Those restrictions are not silently relaxed by this preparation.

Verification: the existing 940 native regressions and 13 inventory tests pass.
The fresh amd64 Release build succeeds with existing threading warnings; its
assembly PE machine is AMD64 (`0x8664`). This is independent of PR #26's hardening
work and proves neither actual Shell64 loading nor compatibility of the installed
4026 Automation Interface. No vendor interop is added to the repository.

Next implementation needs exact 4026 build and installed Shell64 evidence for:

- Actual installation identity, executable/PE architecture and extension discovery.
- Per-host XAE/Automation Interface version, rather than a global registry guess.
- Correct Shell64 VSIX target, payload selection and local interop reference.
- Host selection UI and separate managed installation/rollback ownership.
- Actual package load, WebView, editor/member/dirty-state and both MCPs.

Keep 4024/x86 intact. Installing or migrating TwinCAT requires a separate explicit
decision; extension installation eligibility remains closed for unaccepted hosts.

## Build and diagnostics slice

The first version should be a manual, explicit native action on the exact bound
PLC project. Analysis remains read-only; no compiler tool is automatically granted
to the agent. Build is not activation, download, online change or PLC start.

[Beckhoff's build interface](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842451979.html)
uses `SolutionBuild.BuildProject` and `LastBuildInfo`. A nonblocking build/event
flow is needed: do not wait synchronously on the IDE dispatcher or silently switch
the target platform/configuration. Refuse ambiguous binding, existing builds and
unsaved states that could trigger an implicit save. Do not build customer projects
as an implementation test.

Capture project/configuration, start/completion, timeout/cancellation and context
generation. Reject results after project/solution changes or unrelated builds.
Distinguish fresh build output from an Error List snapshot; never label historical
errors as fresh. Diagnostics carry severity, message, verified file/member and
line/column. Navigation stays within the exact bound project's validated files.
Sending diagnostic text to Chat is an explicit disclosure action.

Use synthetic success/failure/cancellation cases first, then confirm the actual
4026/Shell64 API and menu navigation on an installed host. These build/diagnostic
features are a plan, not implemented compiler functionality in this preparation.
