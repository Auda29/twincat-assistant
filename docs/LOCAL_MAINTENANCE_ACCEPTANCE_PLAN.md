# Proposed local maintenance acceptance

The operator asked to test on the current workstation. A VM is not technically
required for a coordinated maintenance window, but a fresh RootSuffix alone
does not isolate the globally installed extension. This is a proposed temporary
replacement and restoration of that extension, not concurrent isolated
installation. No replacement or IDE launch has been authorized or executed by
this preparation. The working prototype and operator data remain untouched.

The production installer continues to reject registered-host installation with
the private test override. Do not remove that refusal or run it with the override
cleared. A separate, reviewed maintenance coordinator is required for this test.

## Prerequisites before requesting the maintenance window

1. A native guard is implemented in `c5790d6`, with 65 synthetic checks and 29
   existing private-configuration checks passed. Its marker resides beside the
   host assembly. In marked maintenance mode, missing/wrong process override,
   different Windows SID, changed configuration or invalid metadata must refuse
   before resolving any default configuration, credentials, preferences,
   bindings or WebView data. Unmarked production behavior stays unchanged.
2. Rebuild the final native/package inputs and verify their full inventories.
   The existing `b38c...` package lacks this additional guard. It must not be
   relabelled or silently adopted as the guarded candidate.
3. Prove the guard with synthetic negative fixtures and the actual copied DLL.
   Repeat private public-Load/Native80 acceptance on the exact guarded package.
   The guard must resolve the original extension directory correctly when the
   IDE shadow-copies its assembly.
4. Prepare an explicit transaction with immutable candidate/backup identities,
   current SID, exact private StateRoot, configuration path/hash, host path,
   marker metadata, a journal and independent restoration commands. Validate
   the same bindings immediately before each write. Test interruption and
   restoration with synthetic host directories before touching the real host.
5. Check WebView policies read-only. Refuse a policy forcing foreign browser
   storage. Do not change registry policy, host prerequisites or ACLs for the test.

## Proposed maintenance sequence

The lab-only marker is `assistant-test-maintenance.json` beside the original
assembly CodeBase, not a shadow-copy cache location. It contains exactly five
literal JSON properties: `schemaVersion` equal to integer 1,
`configurationPath` equal to the canonical process override with ordinal
matching, `configurationSha256` and `selectorSha256` as lowercase SHA-256, and
`operatorSid` equal to the current Windows SID. The selector digest binds its
package-root/version bytes; public Load still validates the actual selected
package. Marker/config/selector must be bounded regular single-link files with
no redirected ancestors. Unknown or duplicate members are refused. The marker
is never included as a package default.

Only after the concrete scripts, artifacts and recovery checks are reviewable,
ask the operator to agree to the temporary extension replacement and close all
engineering IDEs normally. Do not terminate processes. Refuse installation if
any relevant IDE remains open or process identity is uncertain.

Capture and verify a fresh backup of the actual extension files, extension-change
marker and original `native-chat.json` bytes. Compare the known 13-file prototype
against its recovered baseline. Keep the primary backend checkout at recovery
`f6e018a`, runtime source `491ed3a`; do not build or switch it.

Create a fresh private profile under the existing host-tests contract. Copy no
operator credentials, cookies, chats, corpus or projects. Bind SID and exact
`<profile>\state` before the swap. Stage the guarded candidate and selector, then
perform the journalled replacement only with confirmed same-user elevation.
The original global `native-chat.json` is never rewritten.

Launch one deliberately identified Shell with a new RootSuffix and process-only
`TWINCAT_ASSISTANT_TEST_CONFIG`. Do not launch an ordinary engineering session
during the maintenance window. Verify the effective package location and actual
candidate identity; do not assume the extension cache selected it correctly.
Use only synthetic engineering projects. Keep the installed test marker present
until the original prototype is restored, so an unconfigured launch fails closed.

Observe docking/focus, WebView/chat, FB/member editor selection and attachments,
saved/dirty semantics and actual ST copying. Computer Use is currently unavailable;
visible checks therefore require operator observation unless the official
connection becomes available. No PLC/ADS/compiler/runtime actions are part of
this test. Provider/model calls require their existing disclosure consent.

Close the owned test Shell normally. Restore the complete original prototype and
extension-change marker, then verify every file and original configuration hash.
Finally verify normal backend startup without migrating the operator profile.
On interruption, follow the journal and independent restoration path before any
normal IDE launch. Do not guess stale locks or silently continue a partial swap.

## Limits of this proposal

The first C0 DLL does not implement the new guard. Do not load it in the IDE
under the private environment and call that isolated. Its true runtime/policy
rollback proof remains the separately recorded synthetic-package evidence.

First-after-logon requires the operator to save work and actually log off/on;
a warm restart does not satisfy it. Schedule that only after the basic local UI
test and restoration have succeeded. Installed update/rollback and all other
unobserved gates remain open until their exact candidate and path are tested.

This plan prepares a local alternative to the VM. It does not weaken the private
configuration contract or prove the maintenance coordinator safe before its
implementation and synthetic recovery tests have passed.
