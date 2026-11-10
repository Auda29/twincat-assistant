using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TwinCatAssistant.Native
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideMenuResource("Menus.ctmenu", 3)]
    [ProvideToolWindow(typeof(AssistantToolWindow))]
    [Guid("4dcc0b19-7b34-497f-bf44-7f1f612b7afb")]
    public sealed class AssistantPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
#if HOST_X86
            if (IntPtr.Size != 4) throw new InvalidOperationException("Use the 64-bit host package in this IDE.");
#else
            if (IntPtr.Size != 8) throw new InvalidOperationException("Use the 32-bit host package in this IDE.");
#endif
            var commands = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commands == null) throw new InvalidOperationException("IDE command service unavailable.");
            commands.AddCommand(new MenuCommand((sender, args) => {
                JoinableTaskFactory.RunAsync(async () => {
                    try
                    {
                        var window = await ShowToolWindowAsync(typeof(AssistantToolWindow), 0, true, DisposalToken);
                        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                        var frame = window?.Frame as IVsWindowFrame;
                        if (frame == null) throw new InvalidOperationException("Assistant tool window frame unavailable.");
                        // SDK 15.9's synchronous creation fallback does not run
                        // ShowToolWindowAsync's after-frame-creation callback.
                        // Show explicitly on the first invocation as well.
                        ErrorHandler.ThrowOnFailure(frame.Show());
                        var dte = await GetServiceAsync(typeof(SDTE)) as DTE;
                        var rdt = await GetServiceAsync(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
                        var selection = await GetServiceAsync(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
                        ((AssistantControl)window.Content).Bind(dte, selection, rdt, path => GetDirty(rdt, path));
                    }
                    catch (OperationCanceledException) when (DisposalToken.IsCancellationRequested) { }
                    catch (Exception error)
                    {
                        // Keep failures diagnosable without recording editor text.
                        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                        ActivityLog.LogError("TwinCAT Assistant", "OpenToolWindow failed: " + error.GetType().Name + " (0x" + error.HResult.ToString("X8") + ")");
                    }
                }).FileAndForget("TwinCatAssistant/OpenToolWindow");
            }, new CommandID(new Guid("bd34d6a6-75aa-4df3-9b1a-f3b341fc4544"), 0x0100)));
        }

        internal static bool? GetDirty(IVsRunningDocumentTable table, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (table == null) return null;
            IVsHierarchy hierarchy;
            uint itemId, cookie;
            IntPtr data;
            var hr = table.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, path, out hierarchy, out itemId, out data, out cookie);
            if (ErrorHandler.Failed(hr) || data == IntPtr.Zero || cookie == 0)
            {
                if (data != IntPtr.Zero) Marshal.Release(data);
                return null;
            }
            try
            {
                var document = Marshal.GetObjectForIUnknown(data) as IVsPersistDocData;
                int dirty;
                if (document == null || ErrorHandler.Failed(document.IsDocDataDirty(out dirty))) return null;
                return dirty != 0;
            }
            finally { Marshal.Release(data); }
        }
    }

    [Guid("20236295-d6fc-4a9a-ad7b-1d7505dc9d22")]
    public sealed class AssistantToolWindow : ToolWindowPane
    {
        public AssistantToolWindow() : base(null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Content = new AssistantControl();
            UpdateCaption();
            HostText.Changed += UpdateCaption;
        }
        private void UpdateCaption()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Caption = HostText.Get("ReadOnlyCaption");
            if (Frame is IVsWindowFrame frame) frame.SetProperty((int)__VSFPROPID.VSFPROPID_Caption, Caption);
        }
        public override void OnToolWindowCreated()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            base.OnToolWindowCreated();
            var control = (AssistantControl)Content;
            try
            {
                // Shell layout restoration creates the pane without executing
                // the menu command. Bind once the pane has its IDE service site.
                var dte = GetService(typeof(SDTE)) as DTE;
                var rdt = GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
                var selection = GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
                control.Bind(dte, selection, rdt, path => AssistantPackage.GetDirty(rdt, path));
            }
            catch { control.Bind(null, null, null, null); }
        }
        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing) { HostText.Changed -= UpdateCaption; (Content as AssistantControl)?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
