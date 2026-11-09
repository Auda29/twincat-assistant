# Existing prototype profile rejected by installed Preview 2

After installation, the native window displayed a disconnected local chat.
A read-only `inspectState(root, { recovery: false })` using the installed
package and bundled Node reproduced `OpenCode configuration modified: policy
mismatch; initialize a new state directory`. No native service was running at
the time of diagnosis. Creating a new state directory is not the appropriate
repair for a recognized prototype profile with existing conversations.

The existing global configuration exactly matched the current known Analysis
policy when generated with the previous native configuration's Node path and
repository-relative knowledge-client path. Its Knowledge executable also
pointed at the previous user-local runtime. All nine retained project
configurations matched exact known generated policies with these legacy paths.
The packaged validator deliberately accepts current or verified installer sibling
paths, not developer-checkout paths. Removing the unmanaged extension alone
therefore did not complete the one-time prototype migration.

## Local repair

A bounded one-time migration was performed with no IDE or backend owner running.
It acquired the launcher, profile and project locks, refused pending journals,
verified the installed package inventory and all old/new configuration references,
and matched every old policy in full before changing any configuration. It did
not weaken the production configuration validator or execute legacy code.

Only the global settings/configuration pair and each project's receipt/configuration
pair were changed: twenty JSON files. Runtime paths were rebased to the installed
Preview 2 package; existing project identities, prompt scopes and user choices
were retained. Raw originals and before/after hashes were backed up under the
private state's `migration-backups/<run-id>` directory with a completed journal.
Caught write failures had a byte-preserving rollback path. The repair script and
its concrete local paths remain in ignored `local-data`.

Final readback matched all planned configuration bytes and the installed
package's strict global profile inspection reports `configurationCurrent: true`.
No chat databases, provider credentials, cookies or PLC source files were read
or rewritten. Native configuration was read to validate the old runtime/state
selection and was not changed. No IDE/backend was launched.
An actual window reconnection still needs operator confirmation.

This is a local migration record, not an automatically deployed migration feature.
Future unmanaged-prototype migrations need the same explicit known-policy and
data-preservation treatment. The first-menu-click native fix is a separate
change and is not in the installed/published Setup 1 application.
