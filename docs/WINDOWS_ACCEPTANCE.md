# Native Windows acceptance gate

A read-only host prototype is included. The x86 development payload has passed a
user-reported empty-solution menu/tool-window/docking smoke test, but native
editor/chat acceptance and measured cold-start behavior are still open;
the amd64 payload has not been installed. Do not interpret this checklist as an installer. See
[prototype evidence](NATIVE_PROTOTYPE.md). Record evidence separately for **x86
TcXaeShell and amd64 TcXaeShell64** before claiming support or enabling writes.

Candidate `0.1.2` implements active-POU following and compact context with optional
diagnostics. Both payloads build, and policy/context plus synthetic WPF checks
pass. That x86 `0.1.2` payload had verified installation/package loading
and a visible tool window. Full in-process lifecycle,
frame/document, actual unsaved-buffer and responsiveness checks are pending.
User screenshots confirm the real PLC_A/PLC_B header changes and unsaved-comment
status. Seven live direct-reader lifecycle/isolation checks also pass. These
are core x86 evidence, not completion of the full checklist.
See [M1.2 evidence and next steps](ACTIVE_EDITOR.md).

On 2026-10-26, x86 candidate 0.1.4 was installed after UAC confirmation, with all
13 payload hashes verified and 0.1.3 backed up outside extension discovery.
Its command/tool window opens in the normally launched `ContextChecks` solution;
the direct reader distinguishes PLC_A/PLC_B and the local chat listener starts.
Its compact chat/source UI acceptance below still requires operator observation:
Computer Use fails with `native pipe unavailable` / `os error 2`.
The operator subsequently confirms the compact header, PLC_B project-chat
assignment and visible FB_Motor composer reference after the explicit attachment
action, without submitting a message. The operator also confirms that the same
PLC_B chat blocks attachment/input while PLC_A is active and restores its matching
assignment after returning to PLC_B. Provider conversation acceptance follows
below for the corrected 0.1.5 package.

## Environment record

Current x86 evidence: the user confirms the 0.1.1 menu is visible and its window
opens, docks, closes and reopens with no solution loaded. A screenshot shows the
selected shell PID and zero ST targets. Initial opening was perceived as slow;
single-click cold-start behavior is not measured. This does not establish live
editor, dirty-state, WebView2/chat or 64-bit acceptance.

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
- Follow the exact active POU automatically without Refresh/Read/dropdown steps;
  do not require full catalog loading. Recheck identity before showing results.
- Switch between two POUs, then non-ST/no editor; clear obsolete source and keep
  binding/uncertainty visible. Repeat solution switch/close, same-path reopen and
  multiple-IDE isolation. No source may enter chat/MCP/session transport merely
  because an editor was activated.
- Measure a cold single-click window opening; the reported initial delay is not
  sufficient evidence of either a lost command or acceptable latency.

## Future write/compiler tracer (M3/M4, not current authorization)

The following items remain part of the eventual MVP gate, but must not be run
as part of the current read-only editor acceptance. They require a separate scope
decision, implemented approval enforcement and explicit operator authorization.

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

### Candidate 0.1.4: installed synthetic project

Use only `ContextChecks`; no production PLC is connected. Record the installed
payload hash/version separately from component-test results.

- Open the Assistant: the chat starts automatically, its sidebar is collapsed,
  and connection/model diagnostics remain under Details.
- With PLC_A/FB_Motor active, create a project chat. Before clicking **Attach POU to chat**, the composer contains no native source. After the click, inspect
  the visible reference and its snapshot/unsaved-buffer caveat before sending.
- Switch to PLC_B/FB_Motor: source attachment and sending in the PLC_A chat are
  refused. Return to PLC_A and explicitly validate/rebind as needed.
- Modify only synthetic ST without saving. Confirm the changed snapshot status;
  do not treat it as universal proof of exact editor-buffer semantics.
- Reload/reopen, select an existing thread and explicitly use its matching
  project binding. No source is automatically reattached. Confirm the workspace
  initialization message stays absent during repeated navigation.
- If no ST editor is active, a general chat may be created without inventing
  project context. Its POU attachment remains unavailable.
- Provider configuration/consent and an actual provider conversation require
  the operator's own UI actions. No password/API key is requested in chat.

The compiled-control integration script covers these contracts with synthetic
snapshots and no model call; it does not close this installed acceptance gate.

Candidate 0.1.5 adds a provider-order regression: after sign-in, choose the
authenticated provider in native Details and explicitly grant it before selecting
a model in the normal chat picker. No model must be required to press that grant
button. Sign-in alone must leave provider use denied. The real-control fixture
checks this with an isolated fake credential and no provider/model call.
The x86 update is installed with all 13 payload hashes verified, prior 0.1.4
preserved outside extension discovery, and the installed ContextChecks smoke
passing. This does not replace operator verification of the corrected workflow.
The operator subsequently confirms OpenAI visibility, successful explicit grant
and working model selection in 0.1.5. After explicitly attaching PLC_B/FB_Motor,
the operator asks which value its implementation assigns to bRun and confirms
the model's exact reply `bRun := TRUE;`. The scoped x86 chat goal is accepted;
extended lifecycle/host and the full documentation/MCP gate remain separate.

### Full documentation/MCP gate

Install the real built extension; verify selected session and project survive
reopen/rebind without stale attachment. Use an operator-provided validated
corpus and explicitly approved provider. Confirm full `structuredContent` and
traceable source anchors enter an actual answer, not only a catalog response.
Try file-write, shell, alternate-agent, plugin and runtime bypass paths against
actual upstream permission evaluation. Verify no runtime action was dispatched.

Record input, expected result, actual result, exact versions and artifacts for
each step. A browser host fixture, file-only MCP or in-memory domain adapter
cannot close these gates.
