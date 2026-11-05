# Windows package and installer integration

The builder accepts an existing absolute application staging directory and
produces a versioned self-extracting Windows executable:

```powershell
.\scripts\Build-WindowsInstaller.ps1 -PackageDirectory C:\staging\application -OutputDirectory C:\staging\installer
```

The application inventory uses the contract from `a56eb368`: exactly
`{schemaVersion:1,version,platform:'win32',architecture:'x64'|'arm64',files:[{path,size,sha256}]}`.
The manifest is excluded from its sorted `/`-relative list. The validator checks
sizes, SHA256, complete inventory, path safety and reparse points. It adds
Windows filename restrictions. The application architecture is independent of
the initial x86 TcXaeShell host target. The inventory is not a signature.

The executable accepts `-Action Install|Rollback`, `-InstallRoot`,
`-HostExecutable`, `-HostSha256`, `-HostFileVersion`, `-XaeFile` and
`-XaeFileVersion`. Paths must be absolute. It checks the explicitly selected
TcXaeShell executable name, SHA256, version 15 and x86 PE header and the supplied
XAE file name and version; it refuses while the selected TcXaeShell is running or a running shell path cannot be inspected.
It stores selected application files in version directories, validates before
selection and rollback, and retains earlier versions. It does not migrate or
modify chat, login, configuration or binding stores. An existing unrelated root
is refused. Tests use only synthetic temporary roots.

**This executable is a package lifecycle candidate, not yet a usable product
installer.** It does not register or deploy the extension into the selected
shell, connect the packaged runtime to the existing host, verify the XAE file
belongs to the selected host, or verify all installed runtime prerequisites. It
must not be presented as host support. The x86 shell discovery observation in
NATIVE_PROTOTYPE.md used a particular manually unpacked extension and cache
marker, not a generally validated registration API. Worker 1's host identity
contract and Worker 2's actual layout/start integration must be connected before
end-user installation. Test crash recovery, concurrent updates and real rollback
in a separate host installation before release.

The manual Windows workflow takes an existing application staging artifact from
a specified run, builds the executable and uploads it as a candidate. Synthetic
fixtures are only used for tests and are never uploaded. Once available, the
application builder can provide that staging artifact via
`Build-ApplicationPackage.ps1 -OutputDirectory <absolute>`; the present
workflow does not claim to build private upstreams itself.

## Third-party and host dependencies

`native/IdeHost/Host.props` references `TCatSysManagerLib` with `Private=true`,
and NATIVE_PROTOTYPE.md records that existing VSIX builds copy the local
Beckhoff interop assembly. No vendor redistribution license was found in this
repository; the existing dependency review does not establish redistribution
rights. Do not distribute such a payload as if rights were established. A future
host integration must obtain a compatible interop assembly from the selected
TwinCAT installation or establish an actual redistribution right. Do not assume
that an arbitrary local DLL is binary compatible. The repository itself also
has no project license (README); upstream notices must be retained. The package
builder does not establish provenance, legal rights, prerequisite completeness,
or detect proprietary binaries hidden inside archives.

The executable and CI candidate are unsigned and have not passed an actual
TwinCAT host installation, update or rollback. No Git, npm or compilation is
needed to execute the generated executable, but PowerShell and .NET Framework
are required on the target. This is not evidence that all application runtime
prerequisites have been met.
