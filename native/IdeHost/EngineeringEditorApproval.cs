using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private sealed class EditorRequest
        {
            internal EngineeringProviderSession Session;
            internal EngineeringBinding Binding;
            internal string Raw, Operation;
            internal Guid Id;
            internal long Generation;
            internal Dictionary<string, object> Arguments;
            internal bool Approved;
            internal Dictionary<string, object> Creation;
            internal CancellationToken Cancellation;
        }
        private readonly ConcurrentDictionary<Guid, EditorRequest> _editorRequests = new ConcurrentDictionary<Guid, EditorRequest>();
        private readonly object EditorSeenGate = new object();
        private readonly HashSet<Guid> EditorSeenOperations = new HashSet<Guid>();
        private Guid EditorSeenSession;

        // Called by the provider's UI-thread approval callback only after a
        // pre-authorized native action did not match. The pending request is
        // independently registered by the child transport, not by the model.
        private bool ConfirmEditorEngineeringApproval(object providerApproval)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (providerApproval == null || !EditorProfileIsCurrent() || _engineeringProjectSuspended || _engineeringBusy || _sourceBusy
                    || EngineeringBuildGuard.HasUnresolved || EngineeringSourceGuard.HasUnresolved) return false;
                var id = (Guid)EditorProperty(providerApproval, "OperationId"); EditorRequest pending;
                if (id == Guid.Empty || !_editorRequests.TryGetValue(id, out pending) || pending.Approved || pending.Generation != _editorGeneration
                    || pending.Cancellation.IsCancellationRequested || pending.Session != _engineering || pending.Binding == null || !pending.Binding.Same(pending.Session.Binding)) return false;
                var raw = EditorProperty(providerApproval, "RawRequestJson") as string;
                var kind = EditorProperty(providerApproval, "Operation") as string;
                if (_activeEngineeringProfile != "editor" && kind != "build.start") return false;
                var expected = EditorProperty(providerApproval, "Expected");
                if (raw != pending.Raw || kind != pending.Operation || (Guid)EditorProperty(providerApproval, "SessionId") != pending.Binding.SessionId
                    || !EditorExpectedObject(expected, pending.Binding) || EditorProperty(providerApproval, "CanonicalRequestDigest") as string != EngineeringAction.HashRequest(raw)) return false;
                var expiry = (DateTime)EditorProperty(providerApproval, "ExpiresUtc"); DateTime expiryArgument;
                if (expiry.Kind != DateTimeKind.Utc || expiry <= DateTime.UtcNow || expiry > DateTime.UtcNow.AddMinutes(2)
                    || !DateTime.TryParse(EditorText(pending.Arguments, "approvalExpiresUtc", 64), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out expiryArgument) || expiryArgument != expiry) return false;
                var target = EditorProperty(providerApproval, "TargetObjectId") as string;
                var section = EditorProperty(providerApproval, "Section") as string;
                var preview = EditorProperty(providerApproval, "PreviewId") as string;
                var journal = EditorProperty(providerApproval, "JournalId") as string;
                var baseline = EditorProperty(providerApproval, "BaselineDigest") as string;
                var proposed = EditorProperty(providerApproval, "ProposedDigest") as string;
                SourcePreview source = null; Guid journalId = Guid.Empty;
                string configuration = null, platform = null;
                if (kind == "build.start")
                {
                    if (!EditorKeys(pending.Arguments, "approvalExpiresUtc", "configuration", "platform") || target != pending.Binding.ProjectFile
                        || section != null || preview != null || journal != null || baseline != null || proposed != null
                        || !_engineeringPlcBuildVerified) return false;
                    configuration = EditorProperty(providerApproval, "Configuration") as string;
                    platform = EditorProperty(providerApproval, "Platform") as string;
                    if (string.IsNullOrEmpty(configuration) || configuration.Length > 256 || string.IsNullOrEmpty(platform) || platform.Length > 256
                        || configuration != EditorText(pending.Arguments, "configuration", 256) || platform != EditorText(pending.Arguments, "platform", 256)
                        || configuration != _engineeringConfiguration || platform != _engineeringPlatform) return false;
                }
                else if (kind == "source.apply" || kind == "source.rollback")
                {
                    var rollback = kind == "source.rollback"; var referenceKey = rollback ? "journalId" : "previewId";
                    if (!EditorKeys(pending.Arguments, referenceKey, "approvalExpiresUtc", "baselineDigest", "proposedDigest", "projectRevision")
                        || string.IsNullOrEmpty(target) || target.Length > 1024 || (section != "declaration" && section != "implementation")
                        || !EditorUuid(preview) || (rollback ? !EditorUuid(journal) || journal != EditorText(pending.Arguments, referenceKey, 36) : journal != null || preview != EditorText(pending.Arguments, referenceKey, 36))
                        || !SourceDigest(baseline) || !SourceDigest(proposed) || baseline != EditorText(pending.Arguments, "baselineDigest", 64)
                        || proposed != EditorText(pending.Arguments, "proposedDigest", 64)) return false;
                    var revision = EditorText(pending.Arguments, "projectRevision", 1024);
                    var before = EditorProperty(providerApproval, "BeforeText") as string; var after = EditorProperty(providerApproval, "ProposedText") as string;
                    if (string.IsNullOrEmpty(revision) || before == null || after == null || before.Length > SourceTextLimit || after.Length > SourceTextLimit) return false;
                    var separator = target.IndexOf('|');
                    if (separator <= 0 || !Guid.TryParseExact(preview, "D", out var previewId)) return false;
                    var moniker = target.Substring(0, separator);
                    var parts = target.Split('|');
                    if ((parts.Length != 2 && parts.Length != 3) || parts.Any(string.IsNullOrWhiteSpace)) return false;
                    source = new SourcePreview { ObjectId = target, Section = section, Revision = revision, Moniker = moniker, PreviewId = previewId,
                        Source = parts.Length == 2 ? AutomationSource : LiveBufferSource,
                        Before = rollback ? after : before, After = rollback ? before : after,
                        Baseline = rollback ? proposed : baseline, Proposed = rollback ? baseline : proposed };
                    if (rollback && !Guid.TryParseExact(journal, "D", out journalId)) return false;
                }
                else if (kind == "source.create")
                {
                    var payload = EditorProperty(providerApproval, "ProposedText") as string;
                    if (preview != null || journal != null || baseline != EngineeringAction.EmptyDigest
                        || (EditorProperty(providerApproval, "BeforeText") as string) != "") return false;
                    pending.Creation = ValidateSourceCreation(pending.Arguments, target, section, payload, proposed);
                    if (pending.Creation == null) return false;
                }
                else return false;
                lock (EditorSeenGate)
                {
                    if (EditorSeenSession != pending.Binding.SessionId)
                    { EditorSeenOperations.Clear(); EditorSeenSession = pending.Binding.SessionId; }
                    if (EditorSeenOperations.Count >= 64 || !EditorSeenOperations.Add(id)) return false;
                }
                Func<Task<bool>> verifyView = () => VerifyEngineeringEditorViewAsync(pending.Generation, pending.Cancellation);
                bool confirmed;
                if (kind == "build.start")
                {
                    var text = HostText.Format("EngineeringConfirm", new object[] { pending.Binding.SolutionFile, target, configuration, platform });
                    confirmed = ConfirmEditorBuild(text, verifyView);
                }
                else if (kind == "source.create") confirmed = ConfirmSourceCreate(target, pending.Creation, verifyView);
                else confirmed = ConfirmSource(source, kind == "source.rollback", verifyView);
                if (!confirmed || pending.Cancellation.IsCancellationRequested || pending.Generation != _editorGeneration
                    || !EditorProfileIsCurrent() || _engineeringProjectSuspended || pending.Session != _engineering
                    || !pending.Binding.Same(pending.Session.Binding) || DateTime.UtcNow >= expiry || _engineeringBusy || _sourceBusy
                    || EngineeringBuildGuard.HasUnresolved || EngineeringSourceGuard.HasUnresolved
                    || !_editorRequests.TryGetValue(id, out var current) || !ReferenceEquals(current, pending)) return false;
                if (kind == "build.start")
                {
                    if (!EngineeringBuildGuard.TryBegin(id)) return false;
                    pending.Approved = true;
                    _engineeringBuildId = id; _engineeringBusy = true; _engineeringUncertain = false;
                    _engineeringBuildButton.IsEnabled = false; SetEngineeringStatus("EngineeringStarting");
                }
                else
                {
                    var creation = kind == "source.create" ? new EngineeringSourceCreation(id, pending.Binding.ProjectFile, target) : null;
                    if (!EngineeringSourceGuard.TryBegin(id, creation)) return false;
                    sourceGuardSession = pending.Binding.SessionId;
                    sourceGuardJournal = kind == "source.create" ? Guid.Empty : kind == "source.rollback" ? journalId : id;
                    sourceGuardKind = kind; sourceGuardPreview = source; _sourceBusy = true;
                    pending.Approved = true;
                    _sourceStatus.Text = SourceUnknownStatus(id);
                    UpdateSourceButtons();
                }
                return true;
            }
            catch { return false; }
        }

        private static Dictionary<string, object> ValidateSourceCreation(Dictionary<string, object> args,
            string target, string kind, string payload, string digest)
        {
            var parent = EditorText(args, "parentPath", 512);
            var name = EditorText(args, "name", 64);
            var returnType = EditorText(args, "returnType", 64);
            var access = EditorText(args, "access", 16);
            var declaration = EditorText(args, "declarationText", SourceTextLimit);
            var implementation = EditorText(args, "implementationText", SourceTextLimit);
            var hasGet = args.TryGetValue("createGet", out var get);
            var hasSet = args.TryGetValue("createSet", out var set);
            var memberWithAccess = kind == "method" || kind == "property";
            if (!EngineeringAction.IsCreateKind(kind) || kind != EditorText(args, "kind", 16)
                || !EditorTreePath(target) || !EditorTreePath(parent) || !EditorSourceIdentifier(name) || target != parent + "^" + name
                || args.Keys.Except(new[] { "parentPath", "name", "kind", "returnType", "access", "createGet", "createSet",
                    "declarationText", "implementationText", "approvalExpiresUtc" }, StringComparer.Ordinal).Any()
                || (args.ContainsKey("returnType") ? (!memberWithAccess && kind != "fun") || !EditorSourceIdentifier(returnType) : kind == "fun" || kind == "property")
                || (args.ContainsKey("access") && (!memberWithAccess || (access != "PUBLIC" && access != "PRIVATE" && access != "PROTECTED" && access != "INTERNAL")))
                || ((hasGet || hasSet) && kind != "property") || (hasGet && !(get is bool)) || (hasSet && !(set is bool))
                || (args.ContainsKey("declarationText") && (kind == "action" || declaration == null || declaration.IndexOf('\0') >= 0))
                || (args.ContainsKey("implementationText") && (kind == "dut_struct" || kind == "gvl" || kind == "property"
                    || implementation == null || implementation.IndexOf('\0') >= 0))
                || ((Equals(get, true) || Equals(set, true)) && !EditorTreePath(target + "^Get"))) return null;

            // Reconstruct the provider's ordered payload, including its defaults
            // and newline normalization, before presenting the approved content.
            var canonical = new JavaScriptSerializer { MaxJsonLength = EngineeringHostTransport.MaxPayload }.Serialize(new {
                parentPath = parent, name, kind, returnType, access = memberWithAccess ? access ?? "PUBLIC" : null,
                createGet = Equals(get, true), createSet = Equals(set, true),
                declarationText = NormalizeSourceText(declaration), implementationText = NormalizeSourceText(implementation)
            });
            if (payload != canonical || !SourceDigest(digest) || EngineeringAction.HashRequest(payload) != digest) return null;
            return EditorJson(payload, EngineeringHostTransport.MaxPayload);
        }

        private static bool EditorSourceIdentifier(string value)
        { return value != null && Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z", RegexOptions.CultureInvariant); }

        private static bool EditorTreePath(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length <= 512 && value.Count(c => c == '^') < 32
                && value.Split('^').All(part => part.Length > 0 && part.Trim() == part && part != "." && part != ".."
                    && part.IndexOfAny(new[] { '\\', '/', ':', '|' }) < 0 && !part.Any(char.IsControl));
        }

        private static bool ConfirmSourceCreate(string target, Dictionary<string, object> creation, Func<Task<bool>> verifyView)
        {
            var window = new Window { Title = HostText.Get("EngineeringSourceCreateTitle"), Width = 780, Height = 650,
                WindowStartupLocation = WindowStartupLocation.CenterScreen };
            var panel = new DockPanel { Margin = new Thickness(12) };
            window.Content = panel;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Bottom);
            panel.Children.Add(buttons);
            buttons.Children.Add(new Button { Content = HostText.Get("EngineeringSourceCancel"), Margin = new Thickness(4), MinWidth = 100, IsCancel = true });
            var approve = new Button { Content = HostText.Get("EngineeringSourceCreateButton"), Margin = new Thickness(4), MinWidth = 100 };
            buttons.Children.Add(approve);
            BindEngineeringConfirmation(window, approve, verifyView);

            var review = new StackPanel();
            panel.Children.Add(new ScrollViewer { Content = review, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            review.Children.Add(new TextBlock { Text = HostText.Format("EngineeringSourceCreateSummary", new object[] { target, creation["kind"] }),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            foreach (var key in new[] { "returnType", "access", "createGet", "createSet" })
            {
                if (creation[key] == null || ((key == "createGet" || key == "createSet") && !Equals(creation["kind"], "property"))) continue;
                var value = creation[key] is bool enabled ? (enabled ? "true" : "false") : (string)creation[key];
                review.Children.Add(new TextBlock { Text = key + " = " + value, TextWrapping = TextWrapping.Wrap });
            }
            review.Children.Add(new TextBlock { Text = HostText.Get("EngineeringSourceCreatePersistence"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
            foreach (var key in new[] { "declarationText", "implementationText" })
            {
                if (!(creation[key] is string code)) continue;
                review.Children.Add(new TextBlock { Text = HostText.Get(key == "declarationText" ? "EngineeringSourceCreateDeclaration" : "EngineeringSourceCreateImplementation") });
                review.Children.Add(new TextBox { Text = code, IsReadOnly = true, AcceptsReturn = true, MinHeight = 70, MaxHeight = 195,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"), TextWrapping = TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            }
            return window.ShowDialog() == true;
        }

        private static bool ConfirmEditorBuild(string text, Func<Task<bool>> verifyView)
        {
            var window = new Window { Title = HostText.Get("EngineeringBuild"), Width = 520,
                SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            var panel = new StackPanel { Margin = new Thickness(12) };
            window.Content = panel;
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            panel.Children.Add(buttons);
            buttons.Children.Add(new Button { Content = HostText.Get("EngineeringSourceCancel"), Margin = new Thickness(4), MinWidth = 100, IsCancel = true });
            var approve = new Button { Content = HostText.Get("EngineeringBuild"), Margin = new Thickness(4), MinWidth = 100 };
            buttons.Children.Add(approve);
            BindEngineeringConfirmation(window, approve, verifyView);
            return window.ShowDialog() == true;
        }

        private static void BindEngineeringConfirmation(Window window, Button approve, Func<Task<bool>> verifyView)
        {
            approve.Click += async (sender, args) =>
            {
                approve.IsEnabled = false;
                // Keep the dialog modal while awaiting the WebView instead of
                // blocking its UI thread. Cancel/close still dismisses it.
                var accepted = false;
                try { accepted = verifyView == null || await verifyView(); }
                catch { }
                if (window.IsVisible) window.DialogResult = accepted;
            };
        }

        // One child request passes through the exact same provider instance.
        // An interrupted await is not evidence that the provider stopped.
        private async Task<string> DispatchEditorEngineeringAsync(EngineeringProviderSession session, string request, CancellationToken cancellation, long generation)
        {
            var binding = session?.Binding;
            var parsed = EditorParseRequest(request, binding);
            if (parsed == null) throw new InvalidOperationException("Invalid engineering request.");
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellation);
            if (_disposed || generation != _editorGeneration || session != _engineering || !binding.Same(session.Binding))
                throw new InvalidOperationException("Engineering grant changed.");
            if (parsed.Operation != "session.ping" && !await VerifyEngineeringEditorViewAsync(generation, cancellation))
                throw new InvalidOperationException("Engineering grant is not active.");
            if (parsed.Operation == "debug.context")
                return await DescribeDebugGrantAsync(session, binding, parsed, generation, cancellation);
            if (_activeEngineeringProfile != "editor" && parsed.Operation.StartsWith("source.", StringComparison.Ordinal))
                throw new InvalidOperationException("The active profile does not allow source operations.");
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                var mutation = parsed.Operation == "build.start" || EngineeringAction.IsSourceMutation(parsed.Operation);
                var timeoutSeconds = mutation ? 125 : 20;
                attempt.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                parsed.Session = session; parsed.Binding = binding; parsed.Raw = request; parsed.Cancellation = attempt.Token;
                parsed.Generation = generation;
                if (mutation && !_editorRequests.TryAdd(parsed.Id, parsed)) throw new InvalidOperationException("Operation is already in flight.");
                try
                {
                    var response = await EngineeringDispatchBoundedAsync(session, request, attempt.Token, timeoutSeconds).ConfigureAwait(false);
                    var envelope = EditorParseResponse(response, binding, parsed.Id);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    attempt.Token.ThrowIfCancellationRequested();
                    if (_disposed || session != _engineering || !binding.Same(session.Binding)) throw new InvalidOperationException("Engineering context changed.");
                    if (parsed.Approved) HandleEditorMutationResponse(parsed, envelope, response);
                    else if (parsed.Operation == "source.rollback" && EditorText(envelope, "status", 32) == "conflict"
                        && EditorText(envelope, "error", 64) == "unsaved_editor_changes") ShowRollbackUnsavedWarning();
                    else if (parsed.Operation == "session.describe")
                    {
                        _engineeringPlcBuildVerified = EngineeringSupportsPlcOnlyBuild(response, binding, parsed.Id,
                            out _engineeringConfiguration, out _engineeringPlatform);
                        UpdateEngineeringBuildButton();
                        SetEngineeringCapabilityStatus(response, binding, parsed.Id);
                    }
                    else if (parsed.Operation == "operation.get" || parsed.Operation == "operation.cancel" || parsed.Operation == "diagnostics.read" || parsed.Operation == "output.read")
                    {
                        Guid build;
                        if (Guid.TryParseExact(EditorText(parsed.Arguments, "targetOperationId", 36), "D", out build) && build == EngineeringBuildGuard.OperationId && build == _engineeringBuildId)
                            RenderEngineeringResponse(session, build, parsed.Id, response);
                    }
                    else if (parsed.Operation == "source.journal") ReconcileEditorSourceJournal(parsed, envelope);
                    if (generation != _editorGeneration
                        || (parsed.Operation != "session.ping" && !await VerifyEngineeringEditorViewAsync(generation, cancellation)))
                        throw new InvalidOperationException("Engineering grant changed.");
                    return response;
                }
                catch
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (parsed.Approved) MarkEditorUnknown(parsed);
                    throw;
                }
                finally { if (mutation) _editorRequests.TryRemove(parsed.Id, out _); }
            }
        }
        private void HandleEditorMutationResponse(EditorRequest request, Dictionary<string, object> envelope, string response)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var status = EditorText(envelope, "status", 32);
            if (request.Operation == "build.start")
            {
                if (EngineeringBuildGuard.OperationId != request.Id || _engineeringBuildId != request.Id) return;
                var running = RenderEngineeringResponse(request.Session, request.Id, request.Id, response);
                if (running) { _engineeringCancelButton.Visibility = Visibility.Visible; _ = ObserveEditorBuildAsync(request.Session, request.Id); }
                else FinishEditorBuild();
                return;
            }
            if (EngineeringSourceGuard.OperationId != request.Id) return;
            var result = EditorObject(envelope, "result"); var provenance = EditorObject(envelope, "provenance");
            if (status == "succeeded")
            {
                if (request.Operation == "source.create")
                {
                    var kind = EditorText(request.Creation, "kind", 16);
                    var target = EditorText(request.Creation, "parentPath", 512) + "^" + EditorText(request.Creation, "name", 64);
                    if (!SourceProvenanceMatches(result, provenance, AutomationSource, "fileWrittenProjectPendingSave")
                        || EditorText(result, "treePath", 512) != target || !MatchesCreatedKind(kind, EditorText(result, "kind", 16)))
                        throw new InvalidOperationException("Unverified source creation.");
                    EngineeringSourceGuard.Reconcile(request.Id);
                    _sourceStatus.Text = HostText.Format("EngineeringSourceCreateDone", new object[] { target });
                    _sourceBusy = false; UpdateSourceButtons();
                    Notify(_sourceStatus.Text);
                    return;
                }
                if (sourceGuardPreview == null || !SourceProvenanceMatches(result, provenance, sourceGuardPreview.Source)
                    || EditorText(result, "journalOperationId", 36) != sourceGuardJournal.ToString("D")
                    || EditorText(result, "baselineDigest", 64) != EditorText(request.Arguments, "baselineDigest", 64)
                    || EditorText(result, "proposedDigest", 64) != EditorText(request.Arguments, "proposedDigest", 64)) throw new InvalidOperationException();
                EngineeringSourceGuard.Reconcile(request.Id);
                if (request.Operation == "source.apply") { _sourceApplied = sourceGuardPreview; _sourceJournal = request.Id; }
                else { _sourceApplied = null; _sourceJournal = Guid.Empty; }
                _sourceStatus.Text = SourceDoneStatus(request.Id, sourceGuardJournal, sourceGuardPreview.Source);
                _sourceBusy = false; UpdateSourceButtons();
                if (sourceGuardPreview.Source == AutomationSource) Notify(_sourceStatus.Text);
                return;
            }
            else if (status == "refused" || status == "conflict" || status == "unsupported")
            {
                EngineeringSourceGuard.Reconcile(request.Id);
                _sourceBusy = false; UpdateSourceButtons();
                if (request.Operation == "source.rollback" && status == "conflict"
                    && EditorText(envelope, "error", 64) == "unsaved_editor_changes") ShowRollbackUnsavedWarning();
                else _sourceStatus.Text = HostText.Get("EngineeringSourceRefused");
            }
            else throw new InvalidOperationException();
        }
        private static bool MatchesCreatedKind(string requested, string observed)
        {
            // Engineering describes created objects with the discovery
            // catalog's kinds, where POUs and DUTs share their respective labels.
            return requested == observed || (observed == "pou" && (requested == "fb" || requested == "prg" || requested == "fun"))
                || (observed == "dut" && requested == "dut_struct");
        }

        private void ReconcileEditorSourceJournal(EditorRequest request, Dictionary<string, object> envelope)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!EngineeringSourceGuard.HasUnresolved || request.Binding.SessionId != sourceGuardSession
                || EditorText(request.Arguments, "journalId", 36) != sourceGuardJournal.ToString("D") || sourceGuardPreview == null
                || EditorText(envelope, "status", 32) != "succeeded") return;
            var result = EditorObject(envelope, "result");
            if (!ValidSourceJournal(result, sourceGuardPreview, sourceGuardJournal)) return;
            object submitted;
            if (sourceGuardKind == "source.rollback" && (!result.TryGetValue("rollbackSubmitted", out submitted) || !Equals(submitted, true))) return;
            var outcome = EditorText(result, sourceGuardKind == "source.rollback" ? "rollbackOutcome" : "outcome", 32);
            if (outcome != "succeeded" && outcome != "conflict" && outcome != "unsupported") return;
            EngineeringSourceGuard.Reconcile(EngineeringSourceGuard.OperationId);
            _sourceBusy = false; _sourceApplied = null; _sourceJournal = Guid.Empty;
            _sourceStatus.Text = HostText.Format("EngineeringSourceReconciled", new object[] { outcome, sourceGuardJournal.ToString("D") }); UpdateSourceButtons();
        }
        private async Task ObserveEditorBuildAsync(EngineeringProviderSession session, Guid build)
        {
            try
            {
                var token = _engineeringLifetime.Token;
                for (int count = 0; count < 40 && EngineeringBuildGuard.OperationId == build; count++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), token);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (_disposed || session != _engineering || session.Binding == null || _engineeringBuildId != build) return;
                    if (_engineeringProjectSuspended) continue;
                    var id = Guid.NewGuid(); var request = session.CreateRequest("operation.get", id, new { targetOperationId = build.ToString("D") });
                    var response = await EngineeringDispatchBoundedAsync(session, request, token);
                    EditorParseResponse(response, session.Binding, id);
                    if (_disposed || session != _engineering || _engineeringBuildId != build || EngineeringBuildGuard.OperationId != build) return;
                    if (!RenderEngineeringResponse(session, build, id, response)) break;
                }
            }
            catch { }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!_disposed && session == _engineering && _engineeringBuildId == build)
                {
                    if (EngineeringBuildGuard.HasUnresolved) { _engineeringUncertain = true; SetEngineeringStatus("EngineeringUnknown"); }
                    FinishEditorBuild();
                }
            }
        }
        private void FinishEditorBuild()
        {
            ThreadHelper.ThrowIfNotOnUIThread(); _engineeringBusy = false; _engineeringCancelButton.Visibility = Visibility.Collapsed;
            UpdateSourceButtons();
        }
        private void MarkEditorUnknown(EditorRequest request)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (request.Operation == "build.start")
            {
                if (!_disposed && request.Session == _engineering && EngineeringBuildGuard.OperationId == request.Id)
                { _engineeringUncertain = true; SetEngineeringStatus("EngineeringUnknown"); FinishEditorBuild(); }
                return;
            }
            if (EngineeringSourceGuard.OperationId != request.Id) return;
            var creation = request.Operation == "source.create";
            if (creation) EngineeringSourceGuard.MarkSourceCreationUnknown(request.Id);
            if (_disposed) return;
            _sourceBusy = false;
            _sourceStatus.Text = SourceUnknownStatus(request.Id);
            UpdateSourceButtons();
            if (creation) RefreshBanner();
        }
        private static EditorRequest EditorParseRequest(string json, EngineeringBinding binding)
        {
            var envelope = EditorJson(json, EngineeringHostTransport.MaxPayload); object version, expectedValue, argsValue;
            if (!EditorKeys(envelope, "protocolVersion", "sessionId", "operationId", "operation", "expected", "arguments") || binding == null
                || !envelope.TryGetValue("protocolVersion", out version) || !Equals(version, 1)
                || EditorText(envelope, "sessionId", 36) != binding.SessionId.ToString("D")
                || !envelope.TryGetValue("expected", out expectedValue) || !(expectedValue is Dictionary<string, object> expected)
                || !EditorExpectedDictionary(expected, binding) || !envelope.TryGetValue("arguments", out argsValue) || !(argsValue is Dictionary<string, object> args)) return null;
            var operation = EditorText(envelope, "operation", 64); var operationId = EditorText(envelope, "operationId", 36);
            if (string.IsNullOrEmpty(operation) || !EditorUuid(operationId) || !Guid.TryParseExact(operationId, "D", out var id)) return null;
            return new EditorRequest { Id = id, Operation = operation, Arguments = args };
        }
        private static Dictionary<string, object> EditorParseResponse(string json, EngineeringBinding binding, Guid id)
        {
            var envelope = EditorJson(json, EngineeringHostTransport.MaxPayload); object version;
            if (envelope == null || binding == null || !envelope.TryGetValue("protocolVersion", out version) || !Equals(version, 1)
                || EditorText(envelope, "sessionId", 36) != binding.SessionId.ToString("D") || EditorText(envelope, "operationId", 36) != id.ToString("D")
                || string.IsNullOrEmpty(EditorText(envelope, "status", 32))) throw new InvalidOperationException("Uncorrelated provider response.");
            var provenance = EditorObject(envelope, "provenance"); var context = EditorObject(provenance, "context");
            if (!EditorObservedContext(context, binding)) throw new InvalidOperationException("Uncorrelated provider context.");
            return envelope;
        }
        private static bool EditorObservedContext(Dictionary<string, object> context, EngineeringBinding binding)
        {
            object pid; DateTime started;
            return context != null && EditorText(context, "sessionId", 36) == binding.SessionId.ToString("D")
                && context.TryGetValue("idePid", out pid) && Equals(pid, binding.IdePid)
                && DateTime.TryParse(EditorText(context, "ideStartedUtc", 64), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out started) && started == binding.IdeStartedUtc
                && string.Equals(EditorText(context, "solutionFile", 4096), binding.SolutionFile, StringComparison.OrdinalIgnoreCase)
                && string.Equals(EditorText(context, "projectFile", 4096), binding.ProjectFile, StringComparison.OrdinalIgnoreCase)
                && EditorText(context, "contextEpoch", 256) == binding.ContextEpoch;
        }
        private static bool EditorExpectedDictionary(Dictionary<string, object> value, EngineeringBinding binding)
        {
            object pid; DateTime started;
            return EditorKeys(value, "idePid", "ideStartedUtc", "solutionFile", "projectFile", "contextEpoch")
                && value.TryGetValue("idePid", out pid) && Equals(pid, binding.IdePid)
                && DateTime.TryParse(EditorText(value, "ideStartedUtc", 64), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out started) && started == binding.IdeStartedUtc
                && string.Equals(EditorText(value, "solutionFile", 4096), binding.SolutionFile, StringComparison.OrdinalIgnoreCase)
                && string.Equals(EditorText(value, "projectFile", 4096), binding.ProjectFile, StringComparison.OrdinalIgnoreCase)
                && EditorText(value, "contextEpoch", 256) == binding.ContextEpoch;
        }
        private static bool EditorExpectedObject(object value, EngineeringBinding binding)
        {
            return value != null && (Guid)EditorProperty(value, "SessionId") == binding.SessionId && (int)EditorProperty(value, "IdePid") == binding.IdePid
                && (DateTime)EditorProperty(value, "IdeStartedUtc") == binding.IdeStartedUtc
                && string.Equals(EditorProperty(value, "SolutionFile") as string, binding.SolutionFile, StringComparison.OrdinalIgnoreCase)
                && string.Equals(EditorProperty(value, "ProjectFile") as string, binding.ProjectFile, StringComparison.OrdinalIgnoreCase)
                && (string)EditorProperty(value, "ContextEpoch") == binding.ContextEpoch;
        }
        private static object EditorProperty(object obj, string name) { return obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance).GetValue(obj, null); }
        private static Dictionary<string, object> EditorObject(Dictionary<string, object> obj, string key) { object value; return obj != null && obj.TryGetValue(key, out value) ? value as Dictionary<string, object> : null; }
        private static bool EditorKeys(Dictionary<string, object> obj, params string[] names) { return obj != null && obj.Count == names.Length && names.All(obj.ContainsKey); }
        private static string EditorText(Dictionary<string, object> obj, string key, int max) { object value; var text = obj != null && obj.TryGetValue(key, out value) ? value as string : null; return text != null && text.Length <= max ? text : null; }
        private static bool EditorUuid(string value) { Guid id; return Guid.TryParseExact(value, "D", out id) && id != Guid.Empty && value == id.ToString("D"); }
        private static Dictionary<string, object> EditorJson(string json, int limit)
        {
            if (string.IsNullOrEmpty(json) || json.Length > limit || new UTF8Encoding(false, true).GetByteCount(json) > limit || !EditorStrictJson(json)) return null;
            return new JavaScriptSerializer { MaxJsonLength = limit, RecursionLimit = 32 }.DeserializeObject(json) as Dictionary<string, object>;
        }
        private static bool EditorStrictJson(string json)
        {
            int index = 0;
            return EditorJsonValue(json, ref index, 0) && EditorJsonSpace(json, ref index) && index == json.Length;
        }
        private static bool EditorJsonSpace(string s, ref int i)
        { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++; return true; }
        private static bool EditorJsonString(string s, ref int i)
        {
            if (i >= s.Length || s[i++] != '"') return false;
            while (i < s.Length)
            {
                char c = s[i++]; if (c == '"') return true; if (c < 0x20) return false;
                if (c != '\\') continue;
                if (i >= s.Length) return false;
                c = s[i++];
                if (c == 'u') { for (int j = 0; j < 4; j++) if (i >= s.Length || !Uri.IsHexDigit(s[i++])) return false; }
                else if (c != '"' && c != '\\' && c != '/' && c != 'b' && c != 'f' && c != 'n' && c != 'r' && c != 't') return false;
            }
            return false;
        }
        private static bool EditorJsonValue(string s, ref int i, int depth)
        {
            if (depth > 12 || !EditorJsonSpace(s, ref i) || i >= s.Length) return false;
            if (s[i] == '"') return EditorJsonString(s, ref i);
            if (s[i] == '{' || s[i] == '[')
            {
                bool obj = s[i++] == '{'; char end = obj ? '}' : ']'; EditorJsonSpace(s, ref i);
                var names = obj ? new HashSet<string>(StringComparer.Ordinal) : null;
                if (i < s.Length && s[i] == end) { i++; return true; }
                while (i < s.Length)
                {
                    if (obj)
                    {
                        int start = i;
                        if (!EditorJsonString(s, ref i)) return false;
                        var name = new JavaScriptSerializer().Deserialize<string>(s.Substring(start, i - start));
                        if (name == null || !names.Add(name) || !EditorJsonSpace(s, ref i) || i >= s.Length || s[i++] != ':') return false;
                    }
                    if (!EditorJsonValue(s, ref i, depth + 1) || !EditorJsonSpace(s, ref i) || i >= s.Length) return false;
                    if (s[i] == end) { i++; return true; }
                    if (s[i++] != ',') return false;
                    EditorJsonSpace(s, ref i);
                }
                return false;
            }
            foreach (string literal in new[] { "true", "false", "null" })
                if (i + literal.Length <= s.Length && string.CompareOrdinal(s, i, literal, 0, literal.Length) == 0) { i += literal.Length; return true; }
            if (s[i] == '-') i++;
            if (i >= s.Length || s[i] < '0' || s[i] > '9') return false;
            if (s[i] == '0') i++; else while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (i < s.Length && s[i] == '.') { i++; int start = i; while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++; if (start == i) return false; }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E')) { i++; if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++; int start = i; while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++; if (start == i) return false; }
            return true;
        }
    }
}
