# Managed configuration across installed versions

Installed applications use `versions/<version>/application` beneath the same
installer-owned root. On a version change, the global profile and project
configuration may still contain the previous package's absolute Node,
Knowledge client and MCP paths. Before accepting such a configuration, the
current runtime validates both complete package manifests, package markers,
the installer root marker and the current selection. It only accepts exact
sibling version layouts with the fixed packaged entrypoints and no redirected
version directories. It never launches code from the previous package. An
inventory is an integrity check, not a signature or proof of publisher identity.

The existing profile consent lock and project lock/journal serialize the
respective updates. The prior configuration must exactly match a current or
explicitly enumerated earlier Analysis prompt, the fixed read-only permissions,
and the corresponding settings. Unknown prompts, policy changes, foreign
paths, corrupted packages and unrelated installations remain refused. The
project's original workspace and receipt are retained; no chats, credentials,
provider consent or project binding are copied into a new profile. MCP paths
are reselected from the active package using the existing native MCP setup.

Rollback requires that both installed versions contain this relocation support
and recognize the precise prompts they encounter. An older binary cannot be
made to understand a newer prompt or this migration after it has been shipped;
those incompatible rollbacks must fail closed rather than accept unreviewed
policy. Earlier unpackaged prototypes without an installer-owned selector and
retained manifest-checked version are not adopted by this migration.

The global profile's existing two-file update still fails closed if interrupted
between configuration and settings writes; it does not yet have a recovery
journal. The project update has its existing validated journal. Process start,
installation state updates and profile migration do not form a single
cross-process transaction, and package manifest verification does not protect
against replacement by someone who can rewrite the package and its manifest.

`tests/integration/application-package.test.mjs` constructs two separate
synthetic packages, starts the protected launcher for version 1, version 2 and
rollback to version 1, and opens the same synthetic project each time. It also
checks older prompts, modified permissions, unknown prompts, changed package
files and chat/login/binding sentinels. `tests/managed-relocation.test.mjs`
checks the bounded path and installer selection rules independently. These tests
make no provider calls, perform no PLC actions and do not establish live IDE
acceptance.
