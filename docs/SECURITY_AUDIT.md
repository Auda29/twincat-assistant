# Dependency audit and release blockers

This development foundation is **not production-approved**. Component tests and
an npm audit do not prove the safety of an IDE/runtime integration.

## Audit baseline

Reproduction:

```sh
npm audit --json
npm audit --prefix upstream --json
npm ls --prefix upstream simple-git @simple-git/argv-parser braces
```

The root dependency graph had no advisories in the initial local audit.
The pinned OpenChamber `2.1.1` runtime initially inherited `simple-git 3.36.0`,
`@simple-git/argv-parser 1.1.1` and `braces 3.0.3`. npm reported six affected
packages (three critical and three high, including transitive propagation).
The underlying advisories were:

- [GHSA-v5rq-49vh-5v5c](https://github.com/advisories/GHSA-v5rq-49vh-5v5c): unsafe
  editor detection, fixed in `@simple-git/argv-parser 2.0.1`.
- [GHSA-x6jw-m9v5-85vh](https://github.com/advisories/GHSA-x6jw-m9v5-85vh): Git
  trailer-command configuration bypass.
- [GHSA-g4wm-2vf7-vfgr](https://github.com/advisories/GHSA-g4wm-2vf7-vfgr) and
  [GHSA-858h-whjf-mvg5](https://github.com/advisories/GHSA-858h-whjf-mvg5): Git
  configuration/option bypasses. An updated `simple-git` release exists, but a
  replacement must be tested against the real upstream before being accepted.
- [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm): deeply
  nested brace patterns can exhaust the stack. The latest published `braces`
  version at the audit was still `3.0.3`; no non-vulnerable npm release was
  available. It is inherited through `micromatch`/`http-proxy-middleware`.

## Tested update attempt

An exact override to `simple-git 4.0.2` and `@simple-git/argv-parser 2.0.1`
was installed and the real-process integration rerun. It failed during
OpenChamber server startup with:

```text
The requested module 'simple-git' does not provide an export named 'default'
```

The incompatible override was removed; a fresh `npm ci --prefix upstream`
restored the original graph and both integration checks passed. No silently
patched node_modules or compatibility shim is shipped. The original **six
affected packages remain a release blocker**, including the critical Git issues.
A reviewed OpenChamber migration to the new API is required separately.

## Local trust model and reviewed limitations

The operator selects trusted installed code. Version output is a compatibility
check, **not** executable provenance, signing or a sandbox. CLI executable path
overrides and MCP commands must never come from a model or retrieved document.
Version subprocesses receive a secret-free allowlist; the actual trusted server
requires its UI password and optionally the consented provider key. npm integrity
pins archives, not mutable installed files. Native artifact signing/digest
verification remains a release requirement.

The exported `probeServer` is a trusted operator/developer helper, not an
untrusted RPC. It gates the exact tool being invoked; the saved-project upstream
still advertises runtime/mutation tools, and catalog presence does not authorize
them. The probe never invokes them. Its command/environment parameters require
trusted caller construction. Future untrusted callers must use a separately
validated state-bound API rather than exposing this helper as-is.

No-follow regular bounded JSON reads and inode checks are enforced on supported
POSIX systems; operator JSON reads fail closed without native no-follow support.
Parent directory components and installed executable trees are operator-owned
trusted inputs, not a hostile same-user filesystem sandbox. A hard-killed
supervisor leaves its lock deliberately fail-closed: do not auto-recover from
PID reuse or delete a lock without checking the old owned helper processes.

## Residual boundary

Keep the runtime password-protected on loopback, with relay off and an empty
isolated workspace. The launcher strips editor/Git/OpenCode override variables
and unrelated credentials; it does not start inside an untrusted repository.
These constraints reduce exposure but **do not remediate** library vulnerabilities
or restrict every general-purpose upstream UI feature. Do not serve remotely,
use untrusted Git projects, or install unreviewed upstream plugins.

Do not close the release gate with `npm audit fix --force`, ignored advisories,
or untested major overrides. A clean updated upstream graph or a narrowly
reviewed upstream fix plus compatibility and adversarial tests is required.
No security claim is made for external MCP repositories' transitive packages;
their reviewed exact revisions are integration prerequisites, not security scans.

The diagnostic bridge requires a long operator-held token and current-user pipe
access. The operator approval method in the portable domain is a trusted-host
API, **not** authentication: a future bridge/IDE integration must prevent models
and untrusted callers from submitting receipts. Current pipe methods expose no
approvals, apply, rollback or build. See [native acceptance](WINDOWS_ACCEPTANCE.md).
