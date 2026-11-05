# Windows installer and integration contract

The versioned executable is built on Windows from an absolute staging directory:

```powershell
.\scripts\Build-WindowsInstaller.ps1 -PackageDirectory C:\staging\application -OutputDirectory C:\staging\installer
```

The package uses the schema in `a56eb368`: `{schemaVersion:1,version,
platform:'win32',architecture:'x64'|'arm64',files:[{path,size,sha256}]}`. It
must additionally contain the unpacked x86 extension in `host/x86/`, including
`extension.vsixmanifest` and `TwinCatAssistant.IdeHost.dll`. The manifest must
identify `TwinCatAssistant.Native.X86`, IntegratedShell and x86. Host process
architecture is independent of the application architecture. The staging
builder must omit `TCatSysManagerLib.dll` and opaque `.vsix` files; the installer
refuses them. The complete manifest checks file sizes and hashes, path safety,
ordering, inventory and links. It does not authenticate the publisher.

The production EXE accepts `-Action Install|Rollback -InstallRoot <absolute>`
and optionally `-HostId <inventory host id>`. It runs its included
`Get-AssistantHostInventory.ps1 -RequireAlphaTarget` against this machine; no
supplied inventory parameter is accepted. It requires exactly one selected
registered `tcxaeshell-x86-tc4024` instance, matching DTE and executable paths,
TwinCAT `3.1.4024.78`, registered TwinCAT install root, x86 shell, WebView2 and
.NET Framework from inventory. It refuses a running selected shell or one whose
process path cannot be inspected. Close it manually; nothing is terminated.
Other shells and builds remain unaccepted.

Before registration or rollback, the selected application's Windows helper and
its runtime contract are checked. A matching .NET 8 runtime must be installed
in the system Program Files `dotnet` directory; having only a later major
runtime is insufficient. Preflight reports this prerequisite without changing
the application package or installing a system runtime. It requires no SDK.

The interop DLL must be at `Components/TcBlockDiagram/Common/TCatSysManagerLib.dll`
under the registered TwinCAT installation. Its .NET assembly identity must match
the host assembly's reference; the installer copies that local DLL into the
installed extension and the local rollback snapshot. This verifies identity,
not every API or live loader behavior. The source build still uses its explicit
`TwinCatInteropPath` and `Private=true`; the staging builder must remove the
copied DLL from the distribution before generating its manifest. The repository
contains no evidence that redistribution of the Beckhoff DLL is permitted, and
no project license is specified. Retain applicable upstream notices. The
installer does not determine rights for arbitrary nested archives.

The extension is deployed to the target's `Common7/IDE/Extensions/
TwinCatAssistant.Native.X86`; the existing `extensions.configurationchanged`
file's timestamp is updated without changing contents. `/UpdateConfiguration`
is not used. Existing unowned extension directories and other discovered
Assistant payloads are refused; no old installation is adopted or deleted.
The chosen install root must be separate from the host extension search path
and initially empty. A lock serializes operations. Each version retains its
manifest-validated application and extension snapshot there. The extension is
staged outside the search directory, swapped while the host is closed, and
restored after a caught failure; a journal makes an interrupted transaction fail
closed for manual recovery. Current state and the old payload are retained.
Snapshots are checked against application files, local interop and installer
metadata. This does not cover every power-loss or concurrent host-start window;
inspect `transaction.pending` and the paths it records before recovery. Never
delete an unknown extension or user data. Existing config, chats, logins,
project bindings and license data are not migrated or modified.

## Packaged runtime selection

Each installed extension contains `assistant-installation.json` with
`schemaVersion: 1`, `targetId`, `version` and the absolute `applicationRoot`
of the selected version. Rollback deploys the previous snapshot and therefore
its previous selection. The native host now reads this file, validates the
selected manifest and fixed entrypoints and resolves Node, the native launcher,
Windows helper and both MCPs from that package. Existing state and credentials
remain at their operator paths. See [application staging](APPLICATION_STAGING.md)
for the exact layout. Missing or malformed installed selectors are rejected;
without an installer ownership marker, the development configuration remains
available. The full package is checked before preparing user state.

Manifest and selector tests establish these boundaries, not a real installed
host. A startup test after moving a synthetic package passes; changing between
two version roots with existing managed MCP paths and prompt policies requires
its own compatibility implementation and test before claiming operational
update/rollback. Unpackaged older prototypes are not installer rollback targets.

The manual CI workflow downloads an explicitly named application staging
artifact from a specified run and uploads the built EXE as a candidate. No
synthetic package is uploaded. It may consume a future
`Build-ApplicationPackage.ps1 -OutputDirectory <absolute>` output. Neither CI
nor fixture tests establish a live host acceptance or release readiness.
The EXE is unsigned; real installation, upgrade, rollback, host loading,
prerequisites, recovery and the license review remain to be checked in a
separate controlled environment. The current test creates synthetic hosts,
assemblies, package and installation roots only.
