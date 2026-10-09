# Native Windows acceptance gate

A read-only host prototype is included. The x86 development payload has reached
extension discovery, but native menu/tool-window/editor acceptance is still open;
the amd64 payload has not been installed. Do not interpret this checklist as an installer. See
[prototype evidence](NATIVE_PROTOTYPE.md). Record evidence separately for **x86
TcXaeShell and amd64 TcXaeShell64** before claiming support or enabling writes.

## Environment record

- Windows version/build and user privileges.
- TwinCAT XAE build, exact selected shell executable/version, bitness and components.
- Classic Visual Studio SDK compatibility and supported VSIX/package registration.
- Automation Interface installation/API version; WebView2 runtime/version.
- .NET target for the in-process IDE package (separate from the portable .NET 8
  bridge), OpenChamber/OpenCode versions and MCP commit pins.

## Safe host and lifecycle tracer

1. Install a minimal package into the selected shell, not an unrelated VS host.
2. Dock/reopen/resize the WebView2 window and test focus/keyboard navigation.
3. Allow only the intended authenticated loopback origin. Reject navigation and
   host messages from external pages; expose no generic COM/shell interface.
4. Implement Windows helper ownership with robust process-tree termination;
   test normal close, crash, cancellation, second start, and stale locks.
5. Bind two simultaneously running IDE instances unambiguously; switching or
   closing a solution invalidates all approval receipts and stale UI context.

## Native engineering tracer (synthetic project only)

Use an existing non-safety ST Program, FunctionBlock or Function. Connect no
production PLC. Preserve declaration and implementation before/after each step.

- Read both sections, object identity and a reliable revision.
- Modify unsaved editor content: distinguish it from saved XML; known dirty or
  unknown dirty state must reject proposals and writes.
- Review and approve exact content and baseline. Mutate the object between read
  and apply: reject without saving, merging or overwriting the user's changes.
- Apply on the correct COM/IDE thread without blocking the UI. Inject first-
  and second-section failures, timeout, disconnection and cancellation.
- Persist backup before the first adapter write; read back both sections.
  Report partial and unknown outcomes; never retry an ambiguous write.
- Roll back only through a separately approved proposal with matching post-state;
  newer user content blocks restoration. Do not rely on universal IDE undo.
- Run a specifically approved build with vetted project build hooks. Verify a
  successful build and an intentional compiler error with fresh diagnostics and
  exact solution/project/configuration correlation. Never activate/download.

## Chat and documentation tracer

Install the real built extension; verify selected session and project survive
reopen/rebind without stale attachment. Use an operator-provided validated
corpus and explicitly approved provider. Confirm full `structuredContent` and
traceable source anchors enter an actual answer, not only a catalog response.
Try file-write, shell, alternate-agent, plugin and runtime bypass paths against
actual upstream permission evaluation. Verify no runtime action was dispatched.

Record input, expected result, actual result, exact versions and artifacts for
each step. A browser host fixture, file-only MCP or in-memory domain adapter
cannot close these gates.
