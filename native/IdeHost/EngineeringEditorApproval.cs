using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
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
            internal Dictionary<string, object> Arguments;
            internal bool Approved;
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
                if (id == Guid.Empty || !_editorRequests.TryGetValue(id, out pending) || pending.Approved
                    || pending.Cancellation.IsCancellationRequested || pending.Session != _engineering || pending.Binding == null || !pending.Binding.Same(pending.Session.Binding)) return false;
                var raw = EditorProperty(providerApproval, "RawRequestJson") as string;
                var kind = EditorProperty(providerApproval, "Operation") as string;
                if (!EngineeringProviderSession.DevelopmentSourceAccess && kind != "build.start") return false;
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
                    if (string.IsNullOrEmpty(revision) || before == null || after == null || before.Length > 16384 || after.Length > 16384) return false;
                    var separator = target.IndexOf('|');
                    if (separator <= 0 || !Guid.TryParseExact(preview, "D", out var previewId)) return false;
                    var moniker = target.Substring(0, separator);
                    // Provider has already validated the exact source target and live buffer.
                    source = new SourcePreview { ObjectId = target, Section = section, Revision = revision, Moniker = moniker, PreviewId = previewId,
                        Before = rollback ? after : before, After = rollback ? before : after,
                        Baseline = rollback ? proposed : baseline, Proposed = rollback ? baseline : proposed };
                    if (rollback && !Guid.TryParseExact(journal, "D", out journalId)) return false;
                }
                else return false;
                lock (EditorSeenGate)
                {
                    if (EditorSeenSession != pending.Binding.SessionId)
                    { EditorSeenOperations.Clear(); EditorSeenSession = pending.Binding.SessionId; }
                    if (EditorSeenOperations.Count >= 64 || !EditorSeenOperations.Add(id)) return false;
                }
                bool confirmed;
                if (kind == "build.start")
                {
                    var text = HostText.Format("EngineeringConfirm", new object[] { pending.Binding.SolutionFile, target, configuration, platform });
                    confirmed = MessageBox.Show(text, HostText.Get("EngineeringBuild"), MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK;
                }
                else confirmed = ConfirmSource(source, kind == "source.rollback");
                if (!confirmed || pending.Cancellation.IsCancellationRequested || !EditorProfileIsCurrent() || _engineeringProjectSuspended || pending.Session != _engineering
                    || !pending.Binding.Same(pending.Session.Binding) || DateTime.UtcNow >= expiry || _engineeringBusy || _sourceBusy
                    || EngineeringBuildGuard.HasUnresolved || EngineeringSourceGuard.HasUnresolved
                    || !_editorRequests.TryGetValue(id, out var current) || !ReferenceEquals(current, pending)) return false;
                if (kind == "build.start")
                {
                    if (!EngineeringBuildGuard.TryBegin(id)) return false;
                    _engineeringBuildId = id; _engineeringBusy = true; _engineeringUncertain = false;
                    _engineeringBuildButton.IsEnabled = false; SetEngineeringStatus("EngineeringStarting");
                }
                else
                {
                    if (!EngineeringSourceGuard.TryBegin(id)) return false;
                    sourceGuardSession = pending.Binding.SessionId; sourceGuardJournal = kind == "source.rollback" ? journalId : id;
                    sourceGuardKind = kind; sourceGuardPreview = source; _sourceBusy = true;
                    _sourceStatus.Text = HostText.Format("EngineeringSourceUnknown", new object[] { id.ToString("D"), sourceGuardJournal.ToString("D") });
                    UpdateSourceButtons();
                }
                pending.Approved = true;
                return true;
            }
            catch { return false; }
        }

        // One child request passes through the exact same provider instance.
        // An interrupted await is not evidence that the provider stopped.
        private async Task<string> DispatchEditorEngineeringAsync(EngineeringProviderSession session, string request, CancellationToken cancellation)
        {
            var binding = session?.Binding;
            var parsed = EditorParseRequest(request, binding);
            if (parsed == null) throw new InvalidOperationException("Invalid engineering request.");
            if (!EngineeringProviderSession.DevelopmentSourceAccess && parsed.Operation.StartsWith("source.", StringComparison.Ordinal))
                throw new InvalidOperationException("Source operations are unavailable in this release.");
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
            attempt.CancelAfter(TimeSpan.FromSeconds(20));
            parsed.Session = session; parsed.Binding = binding; parsed.Raw = request; parsed.Cancellation = attempt.Token;
            var mutation = parsed.Operation == "build.start" || parsed.Operation == "source.apply" || parsed.Operation == "source.rollback";
            if (mutation && !_editorRequests.TryAdd(parsed.Id, parsed)) throw new InvalidOperationException("Operation is already in flight.");
            try
            {
                var response = await EngineeringDispatchBoundedAsync(session, request, attempt.Token).ConfigureAwait(false);
                var envelope = EditorParseResponse(response, binding, parsed.Id);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                attempt.Token.ThrowIfCancellationRequested();
                if (_disposed || session != _engineering || !binding.Same(session.Binding)) throw new InvalidOperationException("Engineering context changed.");
                if (parsed.Approved) HandleEditorMutationResponse(parsed, envelope, response);
                else if (parsed.Operation == "session.describe")
                {
                    _engineeringPlcBuildVerified = EngineeringSupportsPlcOnlyBuild(response, binding, parsed.Id,
                        out _engineeringConfiguration, out _engineeringPlatform);
                    _engineeringBuildButton.IsEnabled = _engineeringPlcBuildVerified && !_engineeringBusy && !_engineeringUncertain
                        && !_engineeringProjectSuspended && !EngineeringBuildGuard.HasUnresolved && !EngineeringSourceGuard.HasUnresolved;
                }
                else if (parsed.Operation == "operation.get" || parsed.Operation == "operation.cancel" || parsed.Operation == "diagnostics.read" || parsed.Operation == "output.read")
                {
                    Guid build;
                    if (Guid.TryParseExact(EditorText(parsed.Arguments, "targetOperationId", 36), "D", out build) && build == EngineeringBuildGuard.OperationId && build == _engineeringBuildId)
                        RenderEngineeringResponse(session, build, parsed.Id, response);
                }
                else if (parsed.Operation == "source.journal") ReconcileEditorSourceJournal(parsed, envelope);
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
                if (EngineeringSourceGuard.OperationId != request.Id || sourceGuardPreview == null
                    || EditorText(provenance, "source", 32) != "liveBuffer" || EditorText(result, "source", 32) != "liveBuffer"
                    || EditorText(result, "journalOperationId", 36) != sourceGuardJournal.ToString("D")
                    || EditorText(result, "baselineDigest", 64) != EditorText(request.Arguments, "baselineDigest", 64)
                    || EditorText(result, "proposedDigest", 64) != EditorText(request.Arguments, "proposedDigest", 64)) throw new InvalidOperationException();
                EngineeringSourceGuard.Reconcile(request.Id);
                if (request.Operation == "source.apply") { _sourceApplied = sourceGuardPreview; _sourceJournal = request.Id; }
                else { _sourceApplied = null; _sourceJournal = Guid.Empty; }
                _sourceStatus.Text = HostText.Format("EngineeringSourceDone", new object[] { request.Id.ToString("D"), sourceGuardJournal.ToString("D") });
            }
            else if (status == "refused" || status == "conflict" || status == "unsupported")
            { EngineeringSourceGuard.Reconcile(request.Id); _sourceStatus.Text = HostText.Get("EngineeringSourceRefused"); }
            else throw new InvalidOperationException();
            _sourceBusy = false; UpdateSourceButtons();
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
            _engineeringBuildButton.IsEnabled = _engineeringPlcBuildVerified && !_engineeringProjectSuspended && !_engineeringUncertain
                && !EngineeringBuildGuard.HasUnresolved && !EngineeringSourceGuard.HasUnresolved; UpdateSourceButtons();
        }
        private void MarkEditorUnknown(EditorRequest request)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (request.Session != _engineering || _disposed) return;
            if (request.Operation == "build.start" && EngineeringBuildGuard.OperationId == request.Id)
            { _engineeringUncertain = true; SetEngineeringStatus("EngineeringUnknown"); FinishEditorBuild(); }
            else if (EngineeringSourceGuard.OperationId == request.Id)
            { _sourceBusy = false; _sourceStatus.Text = HostText.Format("EngineeringSourceUnknown", new object[] { request.Id.ToString("D"), sourceGuardJournal.ToString("D") }); UpdateSourceButtons(); }
        }
        private static EditorRequest EditorParseRequest(string json, EngineeringBinding binding)
        {
            var envelope = EditorJson(json, 256 * 1024); object version, expectedValue, argsValue;
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
            var envelope = EditorJson(json, 512 * 1024); object version;
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
