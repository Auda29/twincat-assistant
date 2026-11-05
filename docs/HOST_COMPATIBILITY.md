# TwinCAT installation variants and Assistant host compatibility

Research date: 2026-11-03. Sources: official Beckhoff installation, system
requirements and Automation Interface documentation, plus Microsoft extension
documentation. Repository inspection is based on the 0.2.0 preview source.

This is a documented installation matrix, not an Assistant compatibility
certification. No IDE was launched, no integration test was performed, and no
TwinCAT installation, package configuration or operator profile was changed.
In this document, full Visual Studio integration is distinct from XAE Shell.

## Separate the dimensions

Record these independently when identifying an installation:

- TwinCAT/XAE version, including the complete 4024.x or 4026.x build number.
- Installation mechanism: classic setup or Package Manager-managed components.
- Host identity: TcXaeShell, TcXaeShell64, or a particular full Visual Studio
  instance, edition and version.
- Host process architecture, independently of Windows or XAR architecture.
- Installed integration packages and, where applicable, the selected Remote
  Manager version.

A 4026 installation does not by itself establish a 64-bit host. An installed
Visual Studio does not establish that TwinCAT is integrated into that instance.
Beckhoff manages host integration separately and documents distinct DTE ProgIDs.
[S3], [S10]

## Documented host combinations

All rows below still require their own Assistant acceptance. The existing local
x86 Shell observations are not a certification of every 4024/4026 sub-build.

| TwinCAT family | Engineering host | Host architecture | Installation/integration route | Evidence |
| --- | --- | --- | --- | --- |
| 4024 | TcXaeShell based on Visual Studio 2017 | 32-bit | Classic full setup; select the supplied Shell | [S1], [S2], [S5] |
| 4024 | Full Visual Studio 2017/2019 | 32-bit | Install Visual Studio first; select its integration in TwinCAT setup | [S1], [S2], [S12] |
| 4026 | TcXaeShell based on Visual Studio 2017 | 32-bit | Package Manager with the corresponding host integration selected | [S3], [S4], [S5] |
| 4026 | TcXaeShell64 based on Visual Studio 2022 | 64-bit | Package Manager with the separate Shell64 integration selected | [S3], [S5] |
| 4026 | Full Visual Studio 2017/2019 | 32-bit | Package Manager integration into an installed IDE instance | [S3], [S4], [S12] |
| 4026 | Full Visual Studio 2022 | 64-bit | Package Manager integration into the installed IDE instance | [S3], [S4], [S12] |
| 4026, exact package/build to establish | Full Visual Studio 2026 | Separate newer host target | Listed by current Beckhoff documentation; exact applicable sub-build and Assistant support remain open | [S3], [S5], [S10] |

Beckhoff's 4024 integration chapter also describes full Visual Studio 2013/2015
and excludes Express. These older hosts are not included in the current
Assistant target ranges. Beckhoff's product overview dates Visual Studio 2019
support to 4024.10 and later. [S1], [S4]

The current 4026 integration page explicitly names Visual Studio Professional.
Do not infer Community/Enterprise acceptance for our extension from that page
or from the presence of one of those IDE editions. Edition-specific discovery,
integration and extension registration need confirmation. [S3]

### Documentation caveats

- The legacy 4024 integration page describes a default VS2017 Shell, but other
  passages on the same page refer to a VS2019 Shell. The product overview and
  current system requirements identify TcXaeShell with VS2017. Detect the
  actual installed host rather than relying on that inconsistent legacy wording.
  [S1], [S4], [S5]
- The reviewed version-specific 4024 setup documentation does not establish a
  4024 + VS2022/Shell64 combination. General system requirements or third-party
  compatibility tables are not sufficient evidence for that specific claim.
  [S1], [S2], [S5]
- Current Beckhoff pages list VS2026, while the reviewed CLI configuration page
  still enumerates IDE switches through VS2022. Obtain the installed TcPkg
  version and its help before claiming a newer CLI switch or package name. [S3], [S6]
- The references establish host families, not a complete per-sub-build, Windows
  edition, IDE edition or extension compatibility matrix.

## Installation mechanisms

### Build 4024: classic setup

The documented route uses `TC31-Full-Setup 3.1`. It can install Engineering and
Runtime together, Engineering only, or Runtime only. For Engineering only, the
Custom selection deselects XAR. The setup offers integration into already
installed Visual Studio versions and installation of the supplied XAE Shell.
If Visual Studio is added later, the legacy integration instructions prescribe
Repair/Modify to integrate TwinCAT into it. [S1], [S2], [S13]

An XAR-only machine has no engineering host for the native Assistant tool
window. A real-time runtime is not an installation prerequisite for the
Assistant's saved-project/code analysis; the appropriate XAE installation is
the relevant host prerequisite. This is an Assistant scope conclusion, not a
change to Beckhoff's XAR requirements.

### Build 4026: Package Manager

On Windows, 4026 uses TwinCAT Package Manager. Its GUI delegates to the `tcpkg`
CLI. Workloads collect product functionality and dependencies; individual
packages are separately managed. Beckhoff documents Stable, Testing and
Outdated feeds, local-directory feeds and NuGet servers. The documented
Beckhoff access path requires a myBeckhoff account. [S7]

The CLI documentation identifies these workload names: [S8]

| Workload | Role |
| --- | --- |
| `TwinCAT.Standard.XAE` | Engineering |
| `TwinCAT.Standard.XAR` | Runtime |
| `TwinCAT.StandardRM.XAE` | Remote Manager engineering |

Host integration can be selected or changed in Settings / Integration before
or after installation. In an existing installation, the CLI documentation
requires both the integration configuration and the corresponding integration
package; a selected switch alone is not proof of a completed integration. [S3], [S6]

| Documented configuration name | Documented integration package |
| --- | --- |
| `useTcXaeShell` | `tcxaeshell.ext` |
| `useTcXaeShell64` | `tcxaeshell64.ext` |
| `useVS2017` | `vs2017.ext` |
| `useVS2019` | `vs2019.ext` |
| `useVS2022` | `vs2022.ext` |

These are integration-package identifiers. They should not be presented as
verified identifiers of the base Shell installer or as a complete workload
dependency graph. Query the actual feed/installed inventory for those details.

Documented read-only inventory commands, not executed in this research:

```powershell
tcpkg -h
tcpkg list -i
tcpkg config list
```

Use the CLI's installed version and command help when collecting later evidence.
Keep the research/discovery path separate from `config set`, package install,
repair, update, import and migration operations. [S6], [S8], [S14]

### Migration and Remote Manager

Installing 4026 over an existing 4024 system is a migration. Package Manager's
guided procedure inventories installed TwinCAT functions, simulates workload
mapping and, after confirmation, removes old setups and installs replacement
workloads. It is not a harmless second installation for our compatibility test.
The documented offline migration package is `TwinCAT.XAE.MigrateCli`. [S7], [S9]

Remote Manager is a separate case from retaining two complete base setups.
Multiple Remote Manager workload versions can coexist. Beckhoff documents older
versions in the Outdated feed and explicitly requires TcXaeShell or Visual
Studio 2017/2019 integration for Remote Manager 4024 and earlier. A 4026 package
environment therefore does not establish that 4024 engineering runs inside its
64-bit host. Record both the host and the selected engineering version. [S11]

### Offline and third-party distribution

Offline deployment can use downloaded workloads/packages in an internal feed,
or a `.tcpkgzip` containing the selected configuration and required packages for
loading on the destination system. Offline is a supported deployment mode, not
a separate IDE architecture. [S15], [S16]

Beckhoff also documents custom `.nupkg` packages, signing and third-party
certificate handling. This makes a Package Manager distribution of the
Assistant worth evaluating, but does not prove that our VSIX/host registration
is already representable or tested that way, or that a Beckhoff feed will carry
our package. Package structure, upgrade/removal behavior and certificate trust
would need a separate prototype. The first Assistant installer should also
accommodate classic 4024 systems without making Package Manager mandatory. [S7], [S14], [S17]

## Host identity and current Assistant implications

Beckhoff publishes these Automation Interface ProgIDs. They distinguish host
families; a ProgID alone does not identify a particular running instance or the
project to which an Assistant action may be bound. [S10]

| Host | DTE ProgID |
| --- | --- |
| TcXaeShell | `TcXaeShell.DTE.15.0` |
| TcXaeShell64 | `TcXaeShell.DTE.17.0` |
| Visual Studio 2017 | `VisualStudio.DTE.15.0` |
| Visual Studio 2019 | `VisualStudio.DTE.16.0` |
| Visual Studio 2022 | `VisualStudio.DTE.17.0` |
| Visual Studio 2026 | `VisualStudio.DTE.18.0` |

Microsoft documents VS2022 as a 64-bit process, requiring compatible compiled
extensions/dependencies and the appropriate VSIX architecture declaration.
Keeping shared implementation code does not remove those host-specific packaging
requirements. [S12]

Static inspection of the current repository, without changing or installing it:

- The [x86 manifest](../native/IdeHost/X86/source.extension.vsixmanifest)
  declares `Microsoft.VisualStudio.IntegratedShell`, version range `[15.0,17.0)`
  and `ProductArchitecture=x86`.
- The [Amd64 manifest](../native/IdeHost/Amd64/source.extension.vsixmanifest)
  declares `Microsoft.VisualStudio.Community`, `[17.0,18.0)` and `amd64`.
  It does not explicitly establish a validated TcXaeShell64 installation target,
  every full Visual Studio edition, or a version 18 host.
- The two host projects share code but use different Shell/DTE SDK references.
  Successful builds are evidence of compilation, not installation or IDE behavior.

Consequently, the Assistant installer needs host-instance discovery, validated
target IDs/version ranges and architecture-specific native dependencies. It
must not copy one payload into a guessed IDE folder based only on "4026" or on
the Windows architecture. Package Manager presence, configured integration,
installed integration and an actually usable XAE project are separate observations.

Existing operator data, provider logins and project bindings remain unchanged
by this research. Future package/path migration must preserve them under the
[startup contract](START_CONTRACT.md) and [runtime integration rules](RUNTIME_INTEGRATION.md).

## Next evidence to collect before integration tests

Update 2026-11-05: items 1–3 were collected read-only for the existing machine,
and the first alpha target is defined. See the
[host acceptance matrix](HOST_ACCEPTANCE_MATRIX.md),
[`native/host-targets.json`](../native/host-targets.json) and
`scripts/Get-AssistantHostInventory.ps1`. Items 4–5 remain open.

1. Identify the existing machine's exact TwinCAT/XAE build and setup mechanism.
   The earlier Shell executable version `15.0.0.0` identifies a host generation,
   not a TwinCAT build. The earlier discovery of VS2019 Community and VS2022
   Build Tools also does not establish TwinCAT integration into either product.
2. Inventory candidate IDE instances, editions, executables and process
   architectures. Match applicable documented ProgIDs and actual installation
   registrations without launching an IDE or creating COM instances.
3. Where Package Manager exists, record its version, installed package/workload
   versions and relevant integration settings through reviewed read-only queries.
4. Resolve exact 4026 package/sub-build coverage, Community/Enterprise edition
   applicability and Shell64 VSIX target identity. Preserve unknown results.
5. Agree the first host/version target, then test installation, tool window,
   docking/focus, active POU/member identity, snapshot provenance, chat restore,
   attachments, both MCPs and shutdown in a separate test setup. Testing another
   TwinCAT family must not silently migrate the operator's current installation.

No proposed combination becomes supported until those host-specific checks pass.
The separate [Knowledge startup investigation](KNOWLEDGE_STARTUP_INVESTIGATION.md)
is unaffected by the installation family.

## Sources

All pages below were retrieved for this research on 2026-11-03. They are live
documentation and may change; the exact selected build still needs its own evidence.

[S1]: https://infosys.beckhoff.com/content/1033/tc3_installation/179467147.html "Beckhoff: Build 4024 integration in Visual Studio"
[S2]: https://infosys.beckhoff.com/content/1033/tc3_installation/6162705803.html "Beckhoff: Installing TwinCAT 3 Engineering, legacy setup"
[S3]: https://infosys.beckhoff.com/content/1033/tc3_installation/20646416267.html "Beckhoff: Build 4026 integration in Visual Studio"
[S4]: https://www.beckhoff.com/en-us/products/automation/twincat/twincat-3-build-4026/ "Beckhoff: Build 4026 features and previous Build 4024"
[S5]: https://infosys.beckhoff.com/content/1033/tc3_overview/6162419083.html "Beckhoff: XAE and XAR system requirements"
[S6]: https://infosys.beckhoff.com/content/1033/tc3_installation/21007204747.html "Beckhoff: tcpkg config"
[S7]: https://infosys.beckhoff.com/content/1033/tc3_installation/15698617995.html "Beckhoff: Package Manager for Build 4026"
[S8]: https://infosys.beckhoff.com/content/1033/tc3_installation/21007205899.html "Beckhoff: tcpkg list and Standard workload names"
[S9]: https://infosys.beckhoff.com/content/1033/tc3_installation/20646419723.html "Beckhoff: Migrating existing TwinCAT software"
[S10]: https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242746251.html "Beckhoff: Handling different Visual Studio versions"
[S11]: https://infosys.beckhoff.com/content/1033/tc3_installation/20646415115.html "Beckhoff: Managing Remote Manager versions"
[S12]: https://learn.microsoft.com/en-us/visualstudio/extensibility/migration/update-visual-studio-extension?view=vs-2022 "Microsoft: Updating extensions for Visual Studio 2022"
[S13]: https://infosys.beckhoff.com/content/1033/tc3_installation/447485323.html "Beckhoff: Engineering and Runtime installation choices"
[S14]: https://infosys.beckhoff.com/content/1033/tc3_installation/15698626059.html "Beckhoff: tcpkg command-line overview"
[S15]: https://infosys.beckhoff.com/content/1033/tc3_installation/20646399115.html "Beckhoff: Offline package management"
[S16]: https://infosys.beckhoff.com/content/1033/tc3_installation/20646417419.html "Beckhoff: Offline tcpkgzip deployment"
[S17]: https://infosys.beckhoff.com/content/1033/tc3_installation/22039122699.html "Beckhoff: Signing custom packages"
