# Application staging inventory (partial implementation)

`scripts/application-manifest.mjs` provides `createManifest(absoluteRoot,
{version, architecture})` and `verifyManifest(absoluteRoot)` for a future
staging builder and installer. Schema 1 is JSON with `schemaVersion`, `version`,
`platform` (`win32`), `architecture` (`x64` or `arm64`) and `files`: an ordered
array of `{path, size, sha256}`. Paths use `/` relative to the staging root;
SHA-256 is lowercase hexadecimal. The manifest itself is excluded. Verification
compares the complete inventory, rejecting unexpected, missing or changed files,
links and special files. Creation refuses to overwrite an existing manifest.
This is an integrity inventory, not a signature or provenance verification.
The installer must independently enforce its own trust and path rules and must
not assume that a valid hash list establishes that its contents are safe.

This module does **not** build or certify a runnable distribution. In particular,
the source-built OpenCode and adapted OpenChamber, root dependencies, Windows
helper, host, and redistribution notices still need an explicit bounded build
and staging layout. Node 22+, .NET 8 desktop/runtime requirements, Engineering
MCP's Node dependencies and Knowledge MCP's Python environment and corpus must
be supplied and validated or explicitly modeled as external prerequisites. The
existing absolute MCP configuration paths and operator state remain unchanged.
No `Build-ApplicationPackage.ps1` is provided yet: emitting a manifest for an
arbitrary directory must not be mistaken for a complete application package.

Integration needs a builder that stages reviewed artifacts in a new empty
output directory, proves the source receipt and upstream versions, copies all
required runtime dependencies and third-party notices, calls `createManifest`,
then verifies a real relocated startup with synthetic state. The host and
installer must agree on the staged root and runtime paths; no user profile,
login, project binding or engineering source should be copied into the package.
This work does not establish native host, MCP, provider or live IDE acceptance.
