# Slow solution load while restoring PLC editors

Recorded: 2026-11-05. Status: open investigation, not an established Assistant defect.

## Observed behavior

The operator reported that the first TwinCAT start in the morning sometimes takes an unusually long time or never finishes loading the solution. The captured host was TcXaeShell x86, TwinCAT Build 4024.78, opening `SBZ628_V3`. It remained at `Preparing Solution...` for over 17 minutes.

Read-only process snapshots at 08:35:03 and 08:35:46 showed the main thread waiting in the same path:

```text
SolutionOptions.LoadDocumentStates / ReopenDocumentFrame
  PlcDocumentManager.Open
  VSEditor.Initialize / InitToolBoxView
  ToolBoxView.InitializeDynamicToolboxItems
  VsToolboxService.set_SelectedItemContainer
  VsTask.Wait / UIThreadReentrancyScope.WaitOnTaskComplete
```

The inspected managed runtime did not contain the Assistant assembly. No Assistant Chat, Core, Knowledge or WebView process was observed for that IDE instance. This argues against Assistant code directly blocking the captured load. It does not exclude an indirect effect of previously persisted IDE layout or document state. The incomplete toolbox task and its underlying dependency have not been identified; a deadlock or damaged cache has not been proven.

## Operator follow-up

The operator ended the hung IDE through Task Manager and opened it again. The solution loaded successfully, but the Shell reported:

> An error was encountered while opening associated documents the last time this solution was loaded. Document load is being skipped during this solution load in order to avoid that error.

That restart skipped document restoration. It supports investigation of the editor restoration path but is not a comparison with the Assistant disabled.

## Next checks

- Open the required PLC editors manually, close TwinCAT normally, and compare the next startup with editor restoration enabled.
- Reproduce with the same solution and document state while the Assistant is disabled, using a reversible change with the IDE closed.
- Back up solution-local IDE state before testing a reset. Do not delete toolbox caches or `.suo` files as an assumed fix.
- Enable a Shell activity log on a controlled reproduction if needed. Keep local process evidence and customer project data out of commits.
- Separately assess deferring the Assistant's UI-thread context polling until solution loading is complete. The current tracker starts polling early; this is a hardening candidate, not the cause established by these snapshots.

The installation and compatibility acceptance should include a cold start with previously open PLC editors, a restored Assistant window, toolbox initialization, and a normal close/reopen cycle. This issue is separate from the [Knowledge connection startup investigation](KNOWLEDGE_STARTUP_INVESTIGATION.md).
