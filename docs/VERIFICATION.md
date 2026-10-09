# Verification record — local integration foundation

All results below come from executed commands in the Linux development
container. They do not establish native Windows/TwinCAT compatibility or a
production security approval.

## Component and upstream evidence

- `npm ci --no-audit --no-fund`: locked root installation succeeded.
- `npm run check`: Node contracts, actual extension build and Chromium tests.
  Closing rerun: **21 Node tests and 7 browser cases passed**, build succeeded.
  Browser stability rerun: `playwright test --repeat-each=3`, **21 passed**.
- The same component command was exercised under Node 22; configured CI uses
  Node 22 on Ubuntu 24.04. A workflow being present is not a remote CI result.
- `npm ci --prefix upstream --no-audit --no-fund`: clean pinned installation.
- Real upstream suite: two checks passed against the pinned knowledge/engineering
  sources. It checks actual structured documentation and saved ST content, then
  real CLI startup, unauthenticated API denial, login, session creation, agent
  warmup and discovery of both configured MCP servers. No model was invoked.
- `.NET restore --locked-mode` and serial build succeeded without warnings or
  errors; 34 console regressions exercise production domain code and real pipes.

Exact reproducible commands are in [installation](INSTALLATION.md) and
[bridge](BRIDGE.md). Corpora/projects/in-memory engineering adapters are clearly
synthetic test fixtures, not native editor or Beckhoff corpus evidence.

## Review-driven regressions

Independent reviews found approval/shutdown and binding-switch replay, ambiguous
hash serialization, unknown-state races/reconciliation, capability revocation,
malformed peer handling, starvation deadlines, frame bounds and response-ID
handling. These were reproduced with failing tests and addressed in the domain
and pipe regressions. Closing review additionally reproduced a nonnumeric
handshake protocol crash; the handler now rejects nonnumeric JSON kinds.

Panel race tests first failed against the original production bundle, then
passed after pending attach generation/disposal guards and directory/session
invalidation. The closing run exposed a host-fixture scheduling race: delayed
results could be released before receipt of the SDK attach message. Tests now
wait for the host queue and visible new binding before release, with three
repeat passes and no weakening of assertions. Version subprocesses now strip secrets; explicit file-only backend
validation and bounded/no-follow JSON reads have regression coverage.

Trust-model limitations are explicit, not silently considered fixed: operator
paths select trusted code, installed-file integrity is not verified by a version
string, a private same-user state tree is required, stale locks fail closed,
and native durable crash recovery remains unimplemented.

## Release blockers

The root audit reported no advisories. The upstream runtime still has six
affected packages (three critical, three high); the tested major-version fix
broke real OpenChamber startup and was reverted. See [security audit](SECURITY_AUDIT.md).
No native IDE host, adapter, live writes, real compiler, runtime integration,
provider turn or validated Beckhoff corpus is shipped. Full extension installation
and Windows lifecycle/dirty-editor acceptance remain open.
