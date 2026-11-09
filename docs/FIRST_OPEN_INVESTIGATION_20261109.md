# Assistant needs a second menu click

Reported on 2026-11-09 for the newly installed Preview 2 application: the first
View > Other Windows > TwinCAT Assistant command does not show the pane; the
second does. The installed setup log reports successful Preview 2 registration.
No IDE was started, closed, attached to or modified during this investigation.

## Cause

`AssistantPackage` calls `AsyncPackage.ShowToolWindowAsync` and relies on that
method to show the frame. The package has no async tool-window factory override;
the base `GetAsyncToolWindowFactory` returns null.

Read-only IL inspection of the SDK reference and the actual installed Shell
library confirms that this selects a synchronous creation fallback:

1. With an existing pane, `ShowToolWindowAsync` calls `ShowWindowFrame`, which
   invokes `IVsWindowFrame.Show()`.
2. With a new pane, it passes `ShowWindowFrame` as an after-creation callback to
   `CreateToolWindowAsync`.
3. That method's no-async-factory branch directly calls synchronous
   `Package.CreateToolWindow(Type,int,ProvideToolWindowAttribute,object)` and
   returns its pane without invoking the callback. It creates but does not show
   the window. The next command takes the existing-pane path instead.

Installed library inspected:
`Common7\IDE\PublicAssemblies\Microsoft.VisualStudio.Shell.15.0.dll`, file version
`15.0.36101.20`, SHA-256
`fecca76754ec25ab87fcc9088e0f8d30e80d316e17cc87d7c5d2729b2528f943`.
The reference package `Microsoft.VisualStudio.Shell.15.0` 15.9.28307 has the same
control flow. This is a first-show omission, independently of backend startup.

## Fix and verification

After obtaining the pane and returning to the UI thread, explicitly validate
its frame and call `ErrorHandler.ThrowOnFailure(frame.Show())` before the
remaining service binding. This also handles a newly created synchronous pane.
The official [SDK analyzer example](https://microsoft.github.io/VSSDK-Analyzers/analyzers/VSSDK003.html)
uses this explicit frame-show step after `ShowToolWindowAsync` as well.
Other opening failures now log exception type and HRESULT to the IDE ActivityLog
instead of being silently discarded; no editor source is logged.

The x86 Release host rebuild passed with the pinned SDK 8.0.425, existing
EmbeddedChat input and installed interop reference. Six pre-existing VSTHRD010
warnings remain in KnowledgeIntegration/DiagnosticsIntegration. This is a host
build, not a new full runtime/package build or live first-click acceptance.

The running installation and published Setup 1 have not been replaced. A new
versioned application package is needed to deliver this native DLL change.
Live confirmation should use one menu invocation after a fresh normal Shell
start with the Assistant pane initially closed, then close/reopen the pane.
