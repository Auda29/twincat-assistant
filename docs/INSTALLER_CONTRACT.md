# Windows installer integration contract (candidate, not a release)

The current checkout does not produce an end-user installer. The target is a
versioned Windows installer for a specifically discovered and accepted
TcXaeShell x86 instance, independent of Windows architecture and TwinCAT build
number. No shell path is inferred. Other hosts remain unvalidated. The current
fixture lifecycle never registers an extension, starts a host or changes a real
installation. It does not establish installation acceptance.

## Staging adapter

Worker 2 should provide a relocatable staging directory through
`scripts/Build-ApplicationPackage.ps1 -OutputDirectory <absolute path>`.
The proposed adapter entry point `scripts/Build-WindowsInstaller.ps1` consumes
`-PackageDirectory` and `-OutputDirectory`, both absolute. Its schema is a
coordination proposal, not evidence that the other worker already implements it:

```json
{"schemaVersion":1,"version":"1.2.3","platform":"windows","architecture":"x86","files":[{"path":"host/extension.vsix","sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}]}
```

`application-manifest.json` is at the staging root; paths use forward slashes,
are relative and unique ignoring case. SHA256 values are lowercase hex. Every
regular file except the manifest must appear once, and links/reparse points,
traversal, missing, changed and unlisted files are rejected. The manifest is
not a signature or proof of origin. The package producer must include Host,
Core, Chat, both MCPs, their runtime dependencies, notices and license material,
or state precise independently checked prerequisites. The present structural
validator does not prove those components exist or that redistribution is
permitted. Reconcile schema with Worker 2, add component and prerequisite checks,
and authenticate the actual release before permitting distribution.

`Build-WindowsInstaller.ps1` deliberately fails even for a structurally valid
package. The manually triggered Windows CI workflow runs fixture tests, invokes
the staging adapter, then enforces this gate; it uploads nothing. Private source,
interop, license and redistributable prerequisites must be supplied and checked
without printing credentials. An actual installer must be implemented and
validated before adding any artifact upload.

## Fixture lifecycle and remaining work

`pwsh -NoProfile -File scripts/Test-InstallerFixture.ps1` creates and deletes
only a randomly named temporary fixture. Its explicit marker and versioned
payloads exercise initial selection, update, rollback, hash validation and data
sentinels. It is not an installer and must not be pointed at operator data.
Existing application data is not migrated; real upgrades must preserve current
configuration, chats, provider logins and project bindings under the existing
start contract. Retained versions and an atomic selection file are a prototype,
not a completed crash recovery, concurrency, trust or security design.

Integration requires: verified host inventory/identity and registration from
Worker 1; the actual package and its component contract from Worker 2; a real
installer and explicit host selection; runtime prerequisite detection; license
and third-party redistribution review; signed artifact and CI upload; and a
separate controlled Windows host test for first install, upgrade, rollback,
startup and persistence. Do not close IDE or unrelated services automatically.
No fixture test demonstrates real host support or authorizes a PLC action.
