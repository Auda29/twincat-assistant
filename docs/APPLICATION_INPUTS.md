# Building application inputs

New inputs include the reviewed [Knowledge validation overlay](BETA_HARDENING_20261110.md).
The pinned wheel remains the base input; original/patched module hashes, original
source and a corrected RECORD identify the modified distribution explicitly.
Existing input directories are not patched in place. Build a fresh output root;
the application builder rejects old unpatched Knowledge inputs.

`Build-ApplicationInputs.ps1` prepares the four input directories used by
`Build-ApplicationPackage.ps1`. It does not change the developer source checkout,
operator configuration, existing Python environments, TwinCAT or PLC projects.
Run on Windows x64 with Node 22+, npm, Git, `uv`, the .NET Framework compiler,
and the pinned .NET SDK needed by this checkout. Downloads and all build work
are isolated under the selected output root; production has no npm, uv or SDK
requirement. The default output is `artifacts/application-inputs`; each output
name must be absent. Use a fresh `-OutputRoot` for a separate run.

```powershell
.\scripts\Build-ApplicationInputs.ps1 -Component Engineering `
  -EngineeringCheckout 'C:\absolute\pinned\engineering'
.\scripts\Build-ApplicationInputs.ps1 -Component Licenses
.\scripts\Build-ApplicationInputs.ps1 -Component KnowledgeProbe `
  -KnowledgeArtifacts 'C:\absolute\release\packages'
# Owner permission for bundling the pinned Knowledge release with Assistant:
$license = (Resolve-Path 'upstream/permissions/knowledge-development.txt').Path
$licenseHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $license).Hash.ToLowerInvariant()
.\scripts\Build-ApplicationInputs.ps1 -Component Knowledge `
  -KnowledgeArtifacts 'C:\absolute\release\packages' `
  -KnowledgeLicense $license -KnowledgeLicenseSha256 $licenseHash
```

`All` runs all three builds and requires the license arguments before it starts.
It refuses already existing component outputs; if a later build fails, completed
components remain and can be retained while the missing component is retried
individually. An explicitly supplied license and digest are a reviewed input,
not a machine determination of copyright or redistribution permission.

The Engineering input is built from the detached revision in
`upstream/engineering-lock.json`; the checked-out HEAD must match. The current
private candidate is `83a7d226fbe2492e917a7fe6ea6fa26f0498ac6a`.
The script builds in a separate clone with `npm ci`, then performs a second
locked production install restricted to the MCP and core workspaces. It copies
compiled MCP and core files, production dependencies and license, materializes
the core workspace as a regular package, and refuses unexpected links. No
untracked local config, sample project or credentials are copied. It moves the
output inside its temporary build root and calls `plc_read_pou` against a new
synthetic saved-file project. Only after this succeeds does it create and verify
the schema-1 manifest. The output version is the pinned revision. The test does
not access an IDE, ADS, PLC or model. The builder also compiles the upstream
net472 native provider and records its revision, assembly version and SHA-256
under `native/host/receipt.json`. The application builder verifies the receipt
and assembly identity before copying the module into `host/x86`.

Knowledge uses the hashes in `upstream/knowledge-lock.json` for its artifacts,
the official Python 3.12.10 x64 embeddable ZIP and locked wheel-only
requirements installed by `uv --require-hashes`. The Python ZIP SHA-256 is
`4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3`,
recorded from the official release's Sigstore bundle digest. This pins the
archive bytes; this builder does not independently verify the Sigstore signature.
The embedded interpreter, Python license, wheel metadata and dependency licenses
are retained. A relative .NET Framework launcher passes the original arguments
and stdio to `python/python.exe -I -B -m twincat_knowledge`. `-B` prevents
Python from modifying the manifest-protected package with bytecode caches. The
output is moved before a real MCP catalog and `tc_get_corpus_status` probe with
empty synthetic state, and the full manifest is checked before and after the
probe. No documentation corpus is included. Windows UCRT and the host's .NET
Framework are prerequisites.

`KnowledgeProbe` performs the same real relocation/protocol test in a temporary
directory without publishing an input, so it does not invent a release license.
The pinned 0.1.1 wheel metadata declares no license and the pinned source tree
contains no `LICENSE` or `COPYING` file. On 2026-11-05 the owner confirmed that
the component is their own code and may be distributed with Assistant. That
confirmation is recorded in
[the permission document](../upstream/permissions/knowledge-0.1.1.txt), passed
explicitly with its SHA-256, and retained in the Knowledge input's `LICENSE`
file. It is a permission record, not a newly chosen public open-source license.
Python and dependency licenses remain in the runtime; this permission does not
replace their terms. The technical probe alone is not a permission record.

The current branch adopts private development candidate `0.1.2.dev20261112`,
not a published Knowledge release. Its canonical wheel preserves the reviewed
provenance module with LF line endings; the overlay accepts the exact reviewed
LF hash pair as well as the earlier CRLF pair and updates the version-specific
RECORD. `knowledge-development.txt` permits private integration only. Public
release and standalone installation commands refuse this development pin.
Use the reviewed canonical artifact directory and a fresh output root for
development input builds. See [the integration record](MCP_P1_INTEGRATION_20261112.md).

The Licenses component downloads the exact running Node version's official
Windows x64 ZIP, checks its SHA-256 against the official HTTPS `SHASUMS256.txt`,
and verifies the archived `node.exe` is byte-identical to the builder's binary
before extracting `LICENSE`. It does not verify the detached Node checksum
signature, so this establishes consistency with the HTTPS source, not a
standalone signature chain. WebView2 license and notice come from the restored
`1.0.4258.31` NuGet archive: its restore metadata must match the host lock, the
archive SHA-512 must match the local NuGet checksum, and `dotnet nuget verify`
must accept its signature. The exact files are extracted from that archive.

The artifacts under `artifacts/application-inputs` are ignored build outputs;
`application-manifest.json` provides file integrity and records x64, not a
publisher signature. `build-provenance.json` records the upstream pins and input
hashes. Build tools or generated binaries may vary bytewise; verify the produced
manifests rather than assuming byte-for-byte reproducibility across tool versions.
The current installer accepts x64 or arm64 backends; this input builder supports
only x64 until separate Python archives and dependencies are pinned and tested.

Run `node --test tests/application-inputs.test.mjs` for the license-refusal and
relocated launcher streaming checks. The real MCP probes are part of the
Engineering and KnowledgeProbe commands above and use synthetic data only. The
final full application package, installer, live TwinCAT host and actual provider
or documentation corpus need separate acceptance. In particular, installer
rollback is not end-to-end acceptance of managed prompt-policy compatibility.
