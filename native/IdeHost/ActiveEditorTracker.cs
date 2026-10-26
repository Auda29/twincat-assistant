using System;
using System.Diagnostics;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TwinCatAssistant.Native
{
    internal sealed class ActiveEditorTracker : IDisposable, IVsSelectionEvents, IVsRunningDocTableEvents
    {
        private readonly NativeActiveEditorReader _reader;
        private readonly IVsMonitorSelection _selection;
        private readonly IVsRunningDocumentTable _rdt;
        private readonly Action<ActiveEditorSnapshot, string> _render;
        private readonly ActiveEditorPublication _publication = new ActiveEditorPublication();
        private readonly DispatcherTimer _debounce, _poll;
        private Events _events;
        private SolutionEvents _solutions;
        private DocumentEvents _documents;
        private TextEditorEvents _text;
        private uint _selectionCookie, _rdtCookie;
        private bool _disposed, _closing, _reading;
        internal double LastReadMilliseconds { get; private set; }
        internal double MaxReadMilliseconds { get; private set; }

        internal ActiveEditorTracker(DTE dte, IVsMonitorSelection selection, IVsRunningDocumentTable rdt,
            Func<string, bool?> dirty, Action<ActiveEditorSnapshot, string> render)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _reader = new NativeActiveEditorReader(dte, System.Diagnostics.Process.GetCurrentProcess().Id, dirty);
            _selection = selection;
            _rdt = rdt;
            _render = render;
            _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
            _debounce.Tick += OnDebounce;
            // Custom TwinCAT editors may not emit DTE text events, especially
            // after the first dirty transition. Poll just the active node, never
            // the catalog; this also notices unsupported/non-ST editor changes.
            _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _poll.Tick += OnPoll;
            try
            {
                if (_selection == null || _rdt == null) throw new InvalidOperationException("Native editor services are unavailable.");
                ErrorHandler.ThrowOnFailure(_selection.AdviseSelectionEvents(this, out _selectionCookie));
                ErrorHandler.ThrowOnFailure(_rdt.AdviseRunningDocTableEvents(this, out _rdtCookie));
                _events = dte.Events;
                _solutions = _events.SolutionEvents;
                _solutions.BeforeClosing += OnSolutionClosing;
                _solutions.AfterClosing += OnSolutionClosed;
                _solutions.Opened += OnSolutionOpened;
                _solutions.ProjectRemoved += OnProjectRemoved;
                _documents = _events.DocumentEvents;
                _documents.DocumentOpened += OnDocument;
                _documents.DocumentSaved += OnDocument;
                _documents.DocumentClosing += OnDocument;
                // DTE text notifications are optional in a custom editor. RDT,
                // selection events and the bounded active-node poll remain used.
                try { _text = _events.TextEditorEvents; _text.LineChanged += OnTextChanged; } catch { _text = null; }
                Invalidate("Resolving the active editor...");
                _poll.Start();
            }
            catch { Dispose(); throw; }
        }

        private void OnDebounce(object sender, EventArgs args) { ThreadHelper.ThrowIfNotOnUIThread(); _debounce.Stop(); ReadNow(); }
        private void OnPoll(object sender, EventArgs args) { ThreadHelper.ThrowIfNotOnUIThread(); if (!_debounce.IsEnabled) ReadNow(); }
        private void OnDocument(Document document) { ThreadHelper.ThrowIfNotOnUIThread(); Invalidate("Updating active editor context..."); }
        private void OnTextChanged(TextPoint start, TextPoint end, int hint) { ThreadHelper.ThrowIfNotOnUIThread(); Invalidate("Updating active editor context..."); }
        private void OnProjectRemoved(Project project) { ThreadHelper.ThrowIfNotOnUIThread(); Invalidate("The project changed. Resolving the active editor..."); }
        // BeforeClosing can be cancelled by the operator. Invalidate immediately,
        // but only suspend following once AfterClosing confirms the close.
        private void OnSolutionClosing() { ThreadHelper.ThrowIfNotOnUIThread(); Invalidate("The solution is closing."); }
        private void OnSolutionClosed() { ThreadHelper.ThrowIfNotOnUIThread(); _closing = true; Invalidate("This IDE has no open solution."); }
        private void OnSolutionOpened() { ThreadHelper.ThrowIfNotOnUIThread(); _closing = false; Invalidate("Resolving the active editor..."); }

        private void Invalidate(string reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            _publication.Invalidate();
            _render(null, reason);
            _debounce.Stop();
            if (!_closing) _debounce.Start();
        }

        internal ActiveEditorSnapshot ReadForChat()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || _closing || _reading) return null;
            ReadNow();
            return _publication.Current;
        }

        private void ReadNow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || _closing || _reading) return;
            _reading = true;
            var generation = _publication.Generation;
            try
            {
                var active = CaptureCurrentFrame();
                if (_publication.Current != null && !_publication.Current.Identity.Equals(active.Identity))
                {
                    _publication.Invalidate();
                    generation = _publication.Generation;
                    _render(null, "Resolving the active editor...");
                }
                var watch = Stopwatch.StartNew();
                var result = _reader.Read(active);
                result.ContextGeneration = generation;
                LastReadMilliseconds = watch.Elapsed.TotalMilliseconds;
                MaxReadMilliseconds = Math.Max(MaxReadMilliseconds, LastReadMilliseconds);
                var current = CaptureCurrentFrame();
                if (_publication.TryPublish(generation, result, current.Identity)) _render(result, null);
                else if (!_disposed && !_closing)
                {
                    if (generation == _publication.Generation) Invalidate("The active editor changed. Resolving context...");
                    _debounce.Start();
                }
            }
            catch (Exception error)
            {
                // Only local controlled validation messages reach the UI. COM
                // failures contain neither source text nor project metadata.
                if (!_disposed && generation == _publication.Generation)
                {
                    _publication.Invalidate();
                    _render(null, error is InvalidOperationException ? error.Message
                        : "Active editor context is unavailable. It will retry automatically.");
                }
            }
            finally { _reading = false; }
        }

        private NativeActiveDocument CaptureCurrentFrame()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            object value, moniker;
            ErrorHandler.ThrowOnFailure(_selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out value));
            var frame = value as IVsWindowFrame;
            if (frame == null) throw new InvalidOperationException("Open a Structured Text POU in this IDE.");
            ErrorHandler.ThrowOnFailure(frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out moniker));
            if (!(moniker is string path) || string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("The active document frame has no exact document identity.");
            return _reader.Capture(path);
        }

        public int OnElementValueChanged(uint elementid, object oldValue, object newValue)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (elementid == (uint)VSConstants.VSSELELEMID.SEID_DocumentFrame) Invalidate("Resolving the active editor...");
            return VSConstants.S_OK;
        }
        public int OnCmdUIContextChanged(uint cookie, int active) { return VSConstants.S_OK; }
        public int OnSelectionChanged(IVsHierarchy oldHierarchy, uint oldItem, IVsMultiItemSelect oldSelection,
            ISelectionContainer oldContainer, IVsHierarchy newHierarchy, uint newItem, IVsMultiItemSelect newSelection,
            ISelectionContainer newContainer) { return VSConstants.S_OK; }
        public int OnAfterAttributeChange(uint cookie, uint attributes) { ThreadHelper.ThrowIfNotOnUIThread(); Invalidate("Updating active editor context..."); return VSConstants.S_OK; }
        public int OnAfterSave(uint cookie) { ThreadHelper.ThrowIfNotOnUIThread(); Invalidate("Updating active editor context..."); return VSConstants.S_OK; }
        public int OnBeforeLastDocumentUnlock(uint cookie, uint lockType, uint readLocks, uint editLocks)
        { ThreadHelper.ThrowIfNotOnUIThread(); Invalidate("Updating active editor context..."); return VSConstants.S_OK; }
        public int OnBeforeDocumentWindowShow(uint cookie, int firstShow, IVsWindowFrame frame)
        { ThreadHelper.ThrowIfNotOnUIThread(); Invalidate("Resolving the active editor..."); return VSConstants.S_OK; }
        public int OnAfterDocumentWindowHide(uint cookie, IVsWindowFrame frame)
        { ThreadHelper.ThrowIfNotOnUIThread(); Invalidate("Updating active editor context..."); return VSConstants.S_OK; }
        public int OnAfterFirstDocumentLock(uint cookie, uint lockType, uint readLocks, uint editLocks) { return VSConstants.S_OK; }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            _disposed = true;
            _publication.Dispose();
            _debounce.Stop(); _debounce.Tick -= OnDebounce;
            _poll.Stop(); _poll.Tick -= OnPoll;
            if (_selectionCookie != 0) { try { _selection.UnadviseSelectionEvents(_selectionCookie); } catch { } _selectionCookie = 0; }
            if (_rdtCookie != 0) { try { _rdt.UnadviseRunningDocTableEvents(_rdtCookie); } catch { } _rdtCookie = 0; }
            if (_solutions != null)
            {
                try { _solutions.BeforeClosing -= OnSolutionClosing; _solutions.AfterClosing -= OnSolutionClosed;
                    _solutions.Opened -= OnSolutionOpened; _solutions.ProjectRemoved -= OnProjectRemoved; } catch { }
            }
            if (_documents != null)
            {
                try { _documents.DocumentOpened -= OnDocument; _documents.DocumentSaved -= OnDocument; _documents.DocumentClosing -= OnDocument; } catch { }
            }
            if (_text != null) { try { _text.LineChanged -= OnTextChanged; } catch { } }
            _text = null; _documents = null; _solutions = null; _events = null;
        }
    }
}
