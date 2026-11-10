# Startup and IDE responsiveness investigation

Recorded: 2026-11-10. Applies to the Beta 2 startup paths. The operator reported
long loading, intermittent initialization failure and a slower TwinCAT editor.
The changes below require a new application release; the installed application
and operator data were not modified during this investigation.

## Confirmed UI-thread work

`PrepareProjectWorkspaceAsync` called the configuration loader before checking
its already-prepared project. General-chat workspace comparisons also loaded
configuration, and the default `ChatBindingStore` loaded it for state-root and
filename validation. These paths are reached repeatedly by status/automation
and rendering, not just by an explicit connection attempt.

For an installed package, `NativeChatConfiguration.Load` parses the complete
manifest and hashes its fixed executable entrypoints. Three standalone x86
calls against the actual installed selection took **467.5, 405.2 and 382.5 ms**.
The probe used the configuration-loading method only, outside the IDE. It did
not start a service, modify a profile or read chat/provider credentials.
Repeating this work on the IDE dispatcher is a concrete responsiveness defect;
the active-editor read timer did not include it in its reported duration.

The control now loads one configuration snapshot per connection on a background
worker. Concurrent consumers share the pending load. Bindings, workspace
comparisons, project preparation and Knowledge operations use that same state
root. Explicit reconnect revalidates configuration, and a failed refresh cannot
fall back to the previous snapshot. The public configuration loader and its
selector/maintenance/private-profile validation remain unchanged.

## Startup delays

The complete package inventory hashed files serially. On the unchanged 21,538-file
Beta 2 application, the initial sample took 23,429 ms. A subsequent sequential
before/after comparison took **17,907 ms before and 5,651 ms after**. These are
local wall-clock observations, not cold-start guarantees or controlled storage
benchmarks. Cache, antivirus and competing workload can change the result.

Inventory hashing now overlaps at most four file reads. Every path, file size
and SHA-256 is still checked, traversal/manifest order stays identical and all
pending reads are drained on failure. A file's digest starts immediately after
its metadata check; paths are not collected for a later unrestricted pass.
Both implementations verified the same existing manifest without rewriting it.

Chat automation also awaited Knowledge status before starting project selection
and preparation. That request performed one complete package check at entry and
another through its MCP-settings reader. The chat presentation timer could keep
running during this unrelated work. Knowledge status now runs independently of
chat selection, and one logical Knowledge request shares its complete inventory
through the existing operation-scoped verifier. Later requests still reverify;
there is no process-wide package-integrity cache. No timeout was increased and
no lock, policy, package or authentication check was disabled.

These paths explain substantial delay and a plausible presentation-timeout
mechanism. The operator's intermittent installed initialization failure was not
reproduced or conclusively attributed to that mechanism in this investigation.

## Verification and remaining checks

- Fresh x86 Host rebuild passed with existing threading warnings.
- Standalone actual WPF/WebView startup verifies a responsive dispatcher during
  a held configuration load, one load shared by consumers, reuse for 100 config/
  binding/workspace lookups, explicit refresh and refusal after invalid refresh.
  The existing general/project binding and presentation cases remain covered.
- The first cached-lookup sample took 45.2 ms for 100 iterations, including the
  PowerShell/reflection driver. It is not a measurement of real editor latency.
- The focused 25 Node checks passed, covering native chat/MCP/Knowledge and
  inventory scope behavior. The final bounded-read algorithm separately passed
  all ten manifest/scope checks, including tampering and link refusals.
- Both manifest algorithms successfully checked all 21,538 real package files.

Installed cold/warm solution opening, editor responsiveness, Knowledge status
and repeated project-chat initialization need operator confirmation after an
updated build. The older PLC editor-restoration hang remains a separate,
unresolved [solution startup investigation](SOLUTION_STARTUP_INVESTIGATION.md).
No running IDE was attached to, closed or restarted; no PLC source was read or
changed by these probes.
