using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private static readonly EngineeringOperationGuard EngineeringSourceGuard = new EngineeringOperationGuard();
        private static Guid sourceGuardSession, sourceGuardJournal;
        private static string sourceGuardKind;
        private static SourcePreview sourceGuardPreview;
        private readonly ComboBox _sourceTargets = new ComboBox { MinWidth = 200, Margin = new Thickness(0, 3, 4, 3) };
        private readonly TextBox _sourceProposed = new TextBox { AcceptsReturn = true, AcceptsTab = true, MaxLength = 16384, MinHeight = 100, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap };
        private readonly TextBox _sourceBefore = new TextBox { IsReadOnly = true, AcceptsReturn = true, MinHeight = 80, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap };
        private readonly TextBox _sourceStatus = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Margin = new Thickness(0, 4, 0, 4) };
        private Button _sourceDiscover, _sourceRead, _sourcePreviewButton, _sourceApply, _sourceRollback, _sourceReconcile;
        private bool _sourceBusy, _sourceSettingText;
        private SourcePreview _sourcePreview;
        private SourcePreview _sourceApplied;
        private Guid _sourceJournal;
        private string _sourceMoniker;

        private sealed class SourceTarget
        {
            internal string ObjectId, Section, Name;
            public override string ToString() { return Name + " — " + Section; }
        }
        private sealed class SourcePreview
        {
            internal string ObjectId, Section, Before, After, Baseline, Proposed, Revision, Moniker;
            internal Guid PreviewId;
        }

        private void BuildEngineeringSourcePanel()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
            panel.Children.Add(new TextBlock { Text = HostText.Get("EngineeringSourceScope"), TextWrapping = TextWrapping.Wrap });
            var row = new WrapPanel();
            _sourceDiscover = Secondary(AsyncButton(HostText.Get("EngineeringSourceDiscover"), DiscoverSourceAsync));
            _sourceRead = Secondary(AsyncButton(HostText.Get("EngineeringSourceRead"), ReadSourceAsync));
            _sourcePreviewButton = Secondary(AsyncButton(HostText.Get("EngineeringSourcePreview"), PreviewSourceAsync));
            _sourceApply = Secondary(AsyncButton(HostText.Get("EngineeringSourceApply"), ApplySourceAsync));
            _sourceRollback = Secondary(AsyncButton(HostText.Get("EngineeringSourceRollback"), RollbackSourceAsync));
            _sourceReconcile = Secondary(AsyncButton(HostText.Get("EngineeringSourceReconcile"), ReconcileSourceAsync));
            row.Children.Add(_sourceDiscover); row.Children.Add(_sourceTargets); row.Children.Add(_sourceRead); row.Children.Add(_sourcePreviewButton); row.Children.Add(_sourceApply); row.Children.Add(_sourceRollback); row.Children.Add(_sourceReconcile);
            panel.Children.Add(row);
            panel.Children.Add(new TextBlock { Text = HostText.Get("EngineeringSourceBefore") }); panel.Children.Add(_sourceBefore);
            panel.Children.Add(new TextBlock { Text = HostText.Get("EngineeringSourceProposed") }); panel.Children.Add(_sourceProposed);
            panel.Children.Add(_sourceStatus);
            var expander = new Expander { Header = HostText.Get("EngineeringSourceTitle"), Content = panel, Margin = new Thickness(8, 2, 8, 4) };
            _toolbar.Children.Add(expander);
            _sourceTargets.SelectionChanged += (s, e) => { InvalidateSourcePreview(); _sourceBefore.Clear(); UpdateSourceButtons(); };
            _sourceProposed.TextChanged += (s, e) => { if (!_sourceSettingText) InvalidateSourcePreview(); UpdateSourceButtons(); };
            EngineeringSourceSessionChanged();
        }

        private void EngineeringSourceContextChanged()
        {
            InvalidateSourcePreview();
            if (_engineeringProjectSuspended) _sourceStatus.Text = HostText.Get("EngineeringSourceContext");
            UpdateSourceButtons();
        }
        private void EngineeringSourceSessionChanged()
        {
            InvalidateSourcePreview(); _sourceApplied = null; _sourceJournal = Guid.Empty; _sourceMoniker = null;
            _sourceTargets.Items.Clear(); _sourceBefore.Clear();
            _sourceSettingText = true; _sourceProposed.Clear(); _sourceSettingText = false;
            _sourceStatus.Text = EngineeringSourceGuard.HasUnresolved
                ? HostText.Format("EngineeringSourceUnknown", new object[] { EngineeringSourceGuard.OperationId.ToString("D"), sourceGuardJournal.ToString("D") })
                : HostText.Get("EngineeringSourceSessionOnly");
            UpdateSourceButtons();
        }
        private void InvalidateSourcePreview() { _sourcePreview = null; _engineering?.RevokeApproval(); }
        private void UpdateSourceButtons()
        {
            if (_sourceDiscover == null) return;
            var ready = !_disposed && _engineering != null && !_engineeringProjectSuspended && !_sourceBusy;
            var unblocked = ready && !EngineeringSourceGuard.HasUnresolved;
            _sourceDiscover.IsEnabled = unblocked; _sourceRead.IsEnabled = unblocked && _sourceTargets.SelectedItem is SourceTarget;
            _sourcePreviewButton.IsEnabled = unblocked && _sourceTargets.SelectedItem is SourceTarget;
            _sourceApply.IsEnabled = unblocked && !_engineeringBusy && !EngineeringBuildGuard.HasUnresolved && _sourcePreview != null;
            _sourceRollback.IsEnabled = unblocked && !_engineeringBusy && !EngineeringBuildGuard.HasUnresolved && _sourceApplied != null && _sourceJournal != Guid.Empty;
            _sourceReconcile.IsEnabled = ready && EngineeringSourceGuard.HasUnresolved && _engineering.Binding != null && sourceGuardSession == _engineering.Binding.SessionId;
        }
        private string ActiveSourceMoniker()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            object frameObject, monikerObject;
            if (_engineeringSelection == null || ErrorHandler.Failed(_engineeringSelection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out frameObject))) return null;
            var frame = frameObject as IVsWindowFrame;
            if (frame == null || ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out monikerObject))) return null;
            var moniker = monikerObject as string;
            try { return !string.IsNullOrEmpty(moniker) && Path.IsPathRooted(moniker) && moniker.EndsWith(".TcPOU", StringComparison.OrdinalIgnoreCase) && Path.GetFullPath(moniker) == moniker ? moniker : null; }
            catch { return null; }
        }
        private bool SourceCurrent(EngineeringProviderSession session, string moniker)
        { Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread(); return !_disposed && session != null && session == _engineering && session.Binding != null && !_engineeringProjectSuspended && moniker != null && ActiveSourceMoniker() == moniker; }

        private async Task DiscoverSourceAsync()
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var session = _engineering; var moniker = ActiveSourceMoniker();
            if (_sourceBusy || EngineeringSourceGuard.HasUnresolved || !SourceCurrent(session, moniker)) return;
            _sourceBusy = true; InvalidateSourcePreview(); _sourceTargets.Items.Clear(); _sourceBefore.Clear(); UpdateSourceButtons();
            try
            {
                var id = Guid.NewGuid(); var result = await SourceCallAsync(session, id, "source.targets", new { moniker }, _engineeringLifetime.Token);
                if (!SourceCurrent(session, moniker)) return;
                object entries, count, truncated;
                if (EngineeringProviderSession.Text(result, "moniker") != moniker || EngineeringProviderSession.Text(result, "source") != "liveBuffer"
                    || !result.TryGetValue("truncated", out truncated) || !Equals(truncated, false) || !result.TryGetValue("targets", out entries) || !(entries is object[] rows)
                    || rows.Length > 64 || !result.TryGetValue("count", out count) || !Equals(count, rows.Length) || !SourceDigest(EngineeringProviderSession.Text(result, "bufferDigest")) || !SourceRevision(EngineeringProviderSession.Text(result, "projectRevision"))) throw new InvalidOperationException();
                var targets = new List<SourceTarget>();
                foreach (var item in rows)
                {
                    var target = item as Dictionary<string, object>; var objectId = EngineeringProviderSession.Text(target, "objectId"); var name = EngineeringProviderSession.Text(target, "qualifiedName"); var kind = EngineeringProviderSession.Text(target, "kind"); object sections;
                    if (target == null || string.IsNullOrEmpty(objectId) || objectId.Length > 1024 || !ValidSourceObjectId(moniker, objectId, name) || string.IsNullOrEmpty(name) || name.Length > 512 || (kind != "pou" && kind != "method" && kind != "action") || !target.TryGetValue("sections", out sections) || !(sections is object[] list) || list.Length < 1 || list.Length > 2) throw new InvalidOperationException();
                    var seen = new HashSet<string>();
                    foreach (var value in list) { var section = value as string; if ((section != "declaration" && section != "implementation") || !seen.Add(section)) throw new InvalidOperationException(); targets.Add(new SourceTarget { ObjectId = objectId, Name = name, Section = section }); }
                }
                _sourceMoniker = moniker; foreach (var target in targets) _sourceTargets.Items.Add(target);
                if (targets.Count > 0) _sourceTargets.SelectedIndex = 0;
                _sourceStatus.Text = HostText.Get("EngineeringSourceChoose");
            }
            catch { _sourceMoniker = null; _sourceTargets.Items.Clear(); _sourceStatus.Text = HostText.Get("EngineeringSourceUnavailable"); }
            finally { _sourceBusy = false; UpdateSourceButtons(); }
        }
        private async Task ReadSourceAsync()
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var session = _engineering; var target = _sourceTargets.SelectedItem as SourceTarget; var moniker = _sourceMoniker;
            if (_sourceBusy || target == null || EngineeringSourceGuard.HasUnresolved || !SourceCurrent(session, moniker)) return;
            _sourceBusy = true; InvalidateSourcePreview(); UpdateSourceButtons();
            try
            {
                var result = await SourceCallAsync(session, Guid.NewGuid(), "source.read", new { objectId = target.ObjectId, section = target.Section }, _engineeringLifetime.Token);
                var text = EngineeringProviderSession.Text(result, "text"); object truncated;
                if (!SourceCurrent(session, moniker) || _sourceTargets.SelectedItem != target || EngineeringProviderSession.Text(result, "objectId") != target.ObjectId || EngineeringProviderSession.Text(result, "section") != target.Section || EngineeringProviderSession.Text(result, "source") != "liveBuffer" || !result.TryGetValue("truncated", out truncated) || !Equals(truncated, false) || text == null || text.Length > 16384 || !SourceDigest(EngineeringProviderSession.Text(result, "bufferDigest")) || !SourceRevision(EngineeringProviderSession.Text(result, "projectRevision"))) throw new InvalidOperationException();
                _sourceBefore.Text = text; _sourceSettingText = true; _sourceProposed.Text = text; _sourceSettingText = false;
                _sourceStatus.Text = HostText.Get("EngineeringSourceReadReady");
            }
            catch { _sourceStatus.Text = HostText.Get("EngineeringSourceUnavailable"); }
            finally { _sourceSettingText = false; _sourceBusy = false; UpdateSourceButtons(); }
        }
        private async Task PreviewSourceAsync()
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var session = _engineering; var target = _sourceTargets.SelectedItem as SourceTarget; var moniker = _sourceMoniker; var proposed = _sourceProposed.Text;
            if (_sourceBusy || target == null || proposed.Length > 16384 || EngineeringSourceGuard.HasUnresolved || !SourceCurrent(session, moniker)) return;
            _sourceBusy = true; InvalidateSourcePreview(); UpdateSourceButtons();
            try
            {
                var id = Guid.NewGuid(); var result = await SourceCallAsync(session, id, "source.preview", new { objectId = target.ObjectId, section = target.Section, proposedText = proposed }, _engineeringLifetime.Token);
                object truncated; var before = EngineeringProviderSession.Text(result, "beforeText"); var after = EngineeringProviderSession.Text(result, "proposedText");
                var baseline = EngineeringProviderSession.Text(result, "baselineDigest"); var digest = EngineeringProviderSession.Text(result, "proposedDigest"); var revision = EngineeringProviderSession.Text(result, "projectRevision");
                if (!SourceCurrent(session, moniker) || _sourceTargets.SelectedItem != target || _sourceProposed.Text != proposed || EngineeringProviderSession.Text(result, "previewOperationId") != id.ToString("D") || EngineeringProviderSession.Text(result, "objectId") != target.ObjectId || EngineeringProviderSession.Text(result, "section") != target.Section || EngineeringProviderSession.Text(result, "source") != "liveBuffer" || !result.TryGetValue("truncated", out truncated) || !Equals(truncated, false) || before == null || before.Length > 16384 || after != proposed || !SourceDigest(baseline) || !SourceDigest(digest) || !SourceRevision(revision)) throw new InvalidOperationException();
                _sourceBefore.Text = before; _sourcePreview = new SourcePreview { ObjectId = target.ObjectId, Section = target.Section, Before = before, After = after, Baseline = baseline, Proposed = digest, Revision = revision, PreviewId = id, Moniker = moniker };
                _sourceStatus.Text = HostText.Get("EngineeringSourcePreviewReady");
            }
            catch { _sourceStatus.Text = HostText.Get("EngineeringSourceUnavailable"); }
            finally { _sourceBusy = false; UpdateSourceButtons(); }
        }
        private async Task ApplySourceAsync() { await MutateSourceAsync(false); }
        private async Task RollbackSourceAsync() { await MutateSourceAsync(true); }
        private async Task MutateSourceAsync(bool rollback)
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var session = _engineering; var p = rollback ? _sourceApplied : _sourcePreview; var journal = _sourceJournal;
            if (_sourceBusy || p == null || _engineeringBusy || EngineeringBuildGuard.HasUnresolved || EngineeringSourceGuard.HasUnresolved || !SourceCurrent(session, p.Moniker) || (rollback && journal == Guid.Empty)) return;
            _sourceBusy = true; UpdateSourceButtons();
            try
            {
                if (rollback)
                {
                    var check = await SourceCallAsync(session, Guid.NewGuid(), "source.journal", new { journalId = journal.ToString("D") }, _engineeringLifetime.Token);
                    if (!ValidSourceJournal(check, p, journal) || EngineeringProviderSession.Text(check, "outcome") != "succeeded" || !Equals(check["rollbackSubmitted"], false)) throw new InvalidOperationException();
                }
                if (!SourceCurrent(session, p.Moniker) || (!rollback && (_sourcePreview != p || _sourceProposed.Text != p.After))) return;
                if (!ConfirmSource(p, rollback)) return;
                if (!SourceCurrent(session, p.Moniker) || (!rollback && (_sourcePreview != p || _sourceProposed.Text != p.After))) return;
                var operation = Guid.NewGuid(); var expires = DateTime.UtcNow.AddSeconds(90); var baseline = rollback ? p.Proposed : p.Baseline; var proposed = rollback ? p.Baseline : p.Proposed;
                var kind = rollback ? "source.rollback" : "source.apply";
                var request = rollback ? session.CreateRequest(kind, operation, new { journalId = journal.ToString("D"), approvalExpiresUtc = expires.ToString("o"), baselineDigest = baseline, proposedDigest = proposed, projectRevision = p.Revision }) : session.CreateRequest(kind, operation, new { previewId = p.PreviewId.ToString("D"), approvalExpiresUtc = expires.ToString("o"), baselineDigest = baseline, proposedDigest = proposed, projectRevision = p.Revision });
                if (!EngineeringSourceGuard.TryBegin(operation)) return;
                sourceGuardSession = session.Binding.SessionId; sourceGuardJournal = rollback ? journal : operation; sourceGuardKind = kind; sourceGuardPreview = p;
                session.ApproveSource(kind, operation, request, expires, p.ObjectId, p.Section, p.PreviewId.ToString("D"), rollback ? journal.ToString("D") : null, baseline, proposed);
                if (!rollback) _sourcePreview = null;
                var envelope = await SourceEnvelopeAsync(session, operation, request, _engineeringLifetime.Token);
                if (_disposed || session != _engineering || session.Binding == null) throw new InvalidOperationException();
                var status = EngineeringProviderSession.Text(envelope, "status"); object resultValue;
                var result = envelope.TryGetValue("result", out resultValue) ? resultValue as Dictionary<string, object> : null;
                if (status == "succeeded")
                {
                    if (EngineeringProviderSession.Text(envelope["provenance"] as Dictionary<string, object>, "source") != "liveBuffer" || result == null || EngineeringProviderSession.Text(result, "journalOperationId") != sourceGuardJournal.ToString("D") || EngineeringProviderSession.Text(result, "baselineDigest") != baseline || EngineeringProviderSession.Text(result, "proposedDigest") != proposed || EngineeringProviderSession.Text(result, "source") != "liveBuffer") throw new InvalidOperationException();
                    EngineeringSourceGuard.Reconcile(operation);
                    if (!rollback) { _sourceApplied = p; _sourceJournal = operation; } else { _sourceApplied = null; _sourceJournal = Guid.Empty; }
                    _sourceStatus.Text = HostText.Format("EngineeringSourceDone", new object[] { operation.ToString("D"), sourceGuardJournal.ToString("D") });
                }
                else if (status == "refused" || status == "conflict" || status == "unsupported") { EngineeringSourceGuard.Reconcile(operation); _sourceStatus.Text = HostText.Get("EngineeringSourceRefused"); }
                else throw new InvalidOperationException();
            }
            catch { _sourceStatus.Text = EngineeringSourceGuard.HasUnresolved ? HostText.Format("EngineeringSourceUnknown", new object[] { EngineeringSourceGuard.OperationId.ToString("D"), sourceGuardJournal.ToString("D") }) : HostText.Get("EngineeringSourceUnavailable"); }
            finally { session?.RevokeApproval(); _sourceBusy = false; UpdateSourceButtons(); }
        }
        private async Task ReconcileSourceAsync()
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var session = _engineering; var operation = EngineeringSourceGuard.OperationId; var journal = sourceGuardJournal;
            if (_sourceBusy || operation == Guid.Empty || session?.Binding == null || session.Binding.SessionId != sourceGuardSession) return;
            _sourceBusy = true; UpdateSourceButtons();
            try
            {
                var result = await SourceCallAsync(session, Guid.NewGuid(), "source.journal", new { journalId = journal.ToString("D") }, _engineeringLifetime.Token);
                if (sourceGuardPreview == null || !ValidSourceJournal(result, sourceGuardPreview, journal) || (sourceGuardKind == "source.rollback" && !Equals(result["rollbackSubmitted"], true))) throw new InvalidOperationException();
                if (_disposed || session != _engineering || session.Binding == null) throw new InvalidOperationException();
                var outcome = EngineeringProviderSession.Text(result, sourceGuardKind == "source.rollback" ? "rollbackOutcome" : "outcome");
                if (outcome != "succeeded" && outcome != "conflict" && outcome != "unsupported") throw new InvalidOperationException();
                EngineeringSourceGuard.Reconcile(operation);
                // A lost response is not used to manufacture a rollback authorization.
                _sourceApplied = null; _sourceJournal = Guid.Empty;
                _sourceStatus.Text = HostText.Format("EngineeringSourceReconciled", new object[] { outcome, journal.ToString("D") });
            }
            catch { _sourceStatus.Text = HostText.Format("EngineeringSourceUnknown", new object[] { operation.ToString("D"), journal.ToString("D") }); }
            finally { _sourceBusy = false; UpdateSourceButtons(); }
        }
        private static bool ValidSourceJournal(Dictionary<string, object> result, SourcePreview p, Guid journal)
        { return result != null && EngineeringProviderSession.Text(result, "journalOperationId") == journal.ToString("D") && EngineeringProviderSession.Text(result, "previewOperationId") == p.PreviewId.ToString("D") && EngineeringProviderSession.Text(result, "objectId") == p.ObjectId && EngineeringProviderSession.Text(result, "section") == p.Section && EngineeringProviderSession.Text(result, "beforeText") == p.Before && EngineeringProviderSession.Text(result, "proposedText") == p.After && EngineeringProviderSession.Text(result, "baselineDigest") == p.Baseline && EngineeringProviderSession.Text(result, "proposedDigest") == p.Proposed && EngineeringProviderSession.Text(result, "projectRevision") == p.Revision && result.ContainsKey("rollbackSubmitted"); }
        private static bool ValidSourceObjectId(string moniker, string id, string name)
        {
            var parts = id.Split('|'); Guid parsed;
            return parts.Length == 3 && parts[0] == moniker && parts[2] == name && Guid.TryParseExact(parts[1], "D", out parsed)
                && parsed != Guid.Empty && parts[1] == parsed.ToString("D");
        }
        private static bool SourceDigest(string s) { if (s == null || s.Length != 64) return false; foreach (var c in s) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false; return true; }
        private static bool SourceRevision(string s) { return !string.IsNullOrEmpty(s) && s.Length <= 1024; }
        private static async Task<Dictionary<string, object>> SourceCallAsync(EngineeringProviderSession session, Guid id, string operation, object args, CancellationToken cancellation)
        {
            var envelope = await SourceEnvelopeAsync(session, id, session.CreateRequest(operation, id, args), cancellation); object value;
            if (EngineeringProviderSession.Text(envelope, "status") != "succeeded" || !envelope.TryGetValue("result", out value) || !(value is Dictionary<string, object> result)) throw new InvalidOperationException();
            if (operation == "source.targets" || operation == "source.read" || operation == "source.preview")
            {
                var dirty = EngineeringProviderSession.Text(result, "dirtyState");
                var provenance = envelope["provenance"] as Dictionary<string, object>;
                if ((dirty != "clean" && dirty != "dirty") || EngineeringProviderSession.Text(provenance, "source") != "liveBuffer"
                    || EngineeringProviderSession.Text(provenance, "dirtyState") != dirty) throw new InvalidOperationException();
            }
            return result;
        }
        private static async Task<Dictionary<string, object>> SourceEnvelopeAsync(EngineeringProviderSession session, Guid id, string request, CancellationToken cancellation)
        {
            var binding = session.Binding; var response = await EngineeringDispatchBoundedAsync(session, request, cancellation);
            if (response == null || response.Length > 512 * 1024 || binding == null || !binding.Same(session.Binding)) throw new InvalidOperationException();
            var envelope = EngineeringProviderSession.Json().DeserializeObject(response) as Dictionary<string, object>; object version;
            if (envelope == null || !envelope.TryGetValue("protocolVersion", out version) || !Equals(version, 1) || EngineeringProviderSession.Text(envelope, "sessionId") != binding.SessionId.ToString("D") || EngineeringProviderSession.Text(envelope, "operationId") != id.ToString("D")) throw new InvalidOperationException();
            object provenanceObject, contextObject, pid; DateTime started;
            var provenance = envelope.TryGetValue("provenance", out provenanceObject) ? provenanceObject as Dictionary<string, object> : null;
            var context = provenance != null && provenance.TryGetValue("context", out contextObject) ? contextObject as Dictionary<string, object> : null;
            if (context == null || EngineeringProviderSession.Text(context, "sessionId") != binding.SessionId.ToString("D")
                || !context.TryGetValue("idePid", out pid) || !Equals(pid, binding.IdePid)
                || EngineeringProviderSession.Text(context, "contextEpoch") != binding.ContextEpoch
                || !string.Equals(EngineeringProviderSession.Text(context, "solutionFile"), binding.SolutionFile, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(EngineeringProviderSession.Text(context, "projectFile"), binding.ProjectFile, StringComparison.OrdinalIgnoreCase)
                || !DateTime.TryParse(EngineeringProviderSession.Text(context, "ideStartedUtc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out started) || started != binding.IdeStartedUtc) throw new InvalidOperationException();
            return envelope;
        }
        private static bool ConfirmSource(SourcePreview p, bool rollback)
        {
            var window = new Window { Title = HostText.Get(rollback ? "EngineeringSourceRollback" : "EngineeringSourceApply"), Width = 780, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            var panel = new DockPanel { Margin = new Thickness(12) }; window.Content = panel;
            var label = new TextBlock { Text = HostText.Format("EngineeringSourceConfirm", new object[] { p.ObjectId, p.Section, p.Revision }), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
            var cancel = new Button { Content = HostText.Get("EngineeringSourceCancel"), Margin = new Thickness(4), MinWidth = 100, IsCancel = true }; var approve = new Button { Content = HostText.Get("EngineeringSourceConfirmButton"), Margin = new Thickness(4), MinWidth = 100 }; approve.Click += (s, e) => window.DialogResult = true; buttons.Children.Add(cancel); buttons.Children.Add(approve);
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition()); panel.Children.Add(grid);
            var before = new TextBox { Text = rollback ? p.After : p.Before, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 4, 0) };
            var after = new TextBox { Text = rollback ? p.Before : p.After, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(after, 1); grid.Children.Add(before); grid.Children.Add(after);
            return window.ShowDialog() == true;
        }
    }
}
