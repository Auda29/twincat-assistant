using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private EngineeringProviderSession _engineering;
        private EngineeringMcpChild _engineeringMcp;
        private EngineeringSectionEditorAccess _engineeringSections;
        private NativeProjectIdentity _engineeringProject;
        private Func<string, bool?> _engineeringDirty;
        private IVsRunningDocumentTable _engineeringRdt;
        private IVsMonitorSelection _engineeringSelection;
        private Func<Type, object> _engineeringGetService;
        private long _engineeringEpoch;
        private CancellationTokenSource _engineeringLifetime;
        private Task<Assembly> _engineeringAssembly;
        private string _engineeringPackage;
        private Task _engineeringInitialization;
        private bool _engineeringBusy, _engineeringUncertain, _engineeringPlcBuildVerified;
        private bool _engineeringProjectSuspended;
        private string _engineeringConfiguration, _engineeringPlatform;
        private static readonly EngineeringOperationGuard EngineeringBuildGuard = new EngineeringOperationGuard();
        private Guid _engineeringBuildId;
        private Button _engineeringBuildButton, _engineeringCancelButton, _engineeringRefreshButton;
        private readonly TextBlock _engineeringStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 3, 0, 3), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _engineeringOutput = new TextBox { IsReadOnly = true, AcceptsReturn = true, MaxHeight = 160, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly StackPanel _engineeringRows = new StackPanel();
        private readonly Expander _engineeringDetails = new Expander { Visibility = Visibility.Collapsed };
        // Parent transport can subscribe here; null revokes the old capability.
        internal event Action<EngineeringProviderSession> EngineeringSessionChanged;
        internal EngineeringProviderSession EngineeringSession { get { return _engineering; } }

        private void BuildEngineeringPanel()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _engineeringBuildButton = Secondary(AsyncButton(HostText.Get("EngineeringBuild"), StartEngineeringBuildAsync));
            _engineeringCancelButton = Secondary(AsyncButton(HostText.Get("EngineeringCancel"), CancelEngineeringBuildAsync));
            _engineeringRefreshButton = Secondary(AsyncButton(HostText.Get("EngineeringRefresh"), RefreshEngineeringCapabilitiesAsync));
            _engineeringBuildButton.IsEnabled = false;
            _engineeringCancelButton.Visibility = Visibility.Collapsed;
            var actions = new WrapPanel();
            actions.Children.Add(_engineeringBuildButton); actions.Children.Add(_engineeringCancelButton); actions.Children.Add(_engineeringRefreshButton); actions.Children.Add(_engineeringStatus);
            var details = new StackPanel();
            details.Children.Add(new TextBlock { Text = HostText.Get("EngineeringNavigationUnavailable"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
            details.Children.Add(new ScrollViewer { Content = _engineeringRows, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            details.Children.Add(_engineeringOutput);
            _engineeringDetails.Header = HostText.Get("EngineeringDetails");
            _engineeringDetails.Content = details;
            var panel = new StackPanel { Margin = new Thickness(8, 2, 8, 4) };
            panel.Children.Add(actions); panel.Children.Add(_engineeringDetails);
            _toolbar.Children.Add(panel);
            SetEngineeringStatus("EngineeringNoProject");
            BuildEngineeringSourcePanel();
            BuildEngineeringEditorMode();
        }

        private void BindEngineering(Func<string, bool?> dirty, IVsRunningDocumentTable rdt, IVsMonitorSelection selection, Func<Type, object> getService)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DisposeEngineering();
            _engineeringDirty = path => EngineeringDirtyState.GetDirty(rdt, _boundDte, getService, path, _engineering?.Binding?.ProjectFile);
            _engineeringRdt = rdt; _engineeringSelection = selection; _engineeringGetService = getService;
        }

        private void RenderEngineeringProject(NativeProjectIdentity project, bool transient)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            if (transient)
            {
                // The tracker briefly clears its publication on ordinary text,
                // save and frame events. Revoke approval, not build observation.
                _engineeringProjectSuspended = true;
                _engineering?.SuspendApprovals();
                _engineeringBuildButton.IsEnabled = false;
                EngineeringSourceContextChanged();
                return;
            }
            if (_engineeringProject != null && _engineeringProject.Equals(project))
            {
                var wasSuspended = _engineeringProjectSuspended;
                _engineeringProjectSuspended = false;
                _engineering?.ResumeApprovals();
                _engineeringBuildButton.IsEnabled = _engineeringPlcBuildVerified && !_engineeringBusy
                    && !_engineeringUncertain && !EngineeringBuildGuard.HasUnresolved;
                if (wasSuspended) EngineeringSourceContextChanged();
                return;
            }
            DisposeEngineering();
            if (project == null || _boundDte == null || _engineeringDirty == null) return;
            _engineeringProject = project;
            if (!EngineeringProviderSession.SupportedHost(HostIdentityReader.Current)) { SetEngineeringStatus("EngineeringUnsupportedHost"); return; }
            _engineeringLifetime = new CancellationTokenSource();
            _engineeringInitialization = InitializeEngineeringAsync(project, _engineeringEpoch, _engineeringLifetime.Token);
        }

        private async Task InitializeEngineeringAsync(NativeProjectIdentity project, long epoch, CancellationToken cancellation)
        {
            SetEngineeringStatus("EngineeringLoading");
            try
            {
                var configuration = await GetChatConfigurationAsync();
                if (_disposed || cancellation.IsCancellationRequested || epoch != _engineeringEpoch) return;
                var root = configuration.RepositoryRoot;
                if (_engineeringAssembly == null || !string.Equals(_engineeringPackage, root, StringComparison.OrdinalIgnoreCase))
                {
                    _engineeringPackage = root;
                    _engineeringAssembly = Task.Run(() => EngineeringProviderSession.LoadVerifiedAssembly(root));
                }
                var assembly = await _engineeringAssembly;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed || cancellation.IsCancellationRequested || epoch != _engineeringEpoch || !_engineeringProject.Equals(project)) return;
                using (var process = Process.GetCurrentProcess())
                {
                    if (process.Id != project.IdeProcessId) throw new InvalidOperationException();
                    var binding = new EngineeringBinding(Guid.NewGuid(), process.Id, process.StartTime.ToUniversalTime(),
                        Path.GetFullPath(project.SolutionFile), Path.GetFullPath(project.ProjectFile), epoch.ToString(CultureInfo.InvariantCulture));
                    var editor = new EngineeringEditorAccess(_engineeringRdt, _engineeringSelection, _boundDte, binding,
                        () => !_disposed && epoch == _engineeringEpoch
                            && _engineering != null && binding.Same(_engineering.Binding), _engineeringGetService);
                    var navigation = new EngineeringDiagnosticNavigation(_boundDte, binding,
                        () => !_disposed && !_engineeringProjectSuspended && epoch == _engineeringEpoch
                            && _engineering != null && binding.Same(_engineering.Binding), ConsumeEngineeringNavigationClick);
                    var sections = new EngineeringSectionEditorAccess(_engineeringRdt, _engineeringSelection, _boundDte, binding,
                        () => !_disposed && !_engineeringProjectSuspended && epoch == _engineeringEpoch && _engineering != null && binding.Same(_engineering.Binding), _engineeringGetService);
                    _engineeringSections = sections;
                    _engineering = new EngineeringProviderSession(assembly, _boundDte, _engineeringDirty, binding,
                        new DispatcherSynchronizationContext(Dispatcher), editor.GetTextBuffer, navigation.Navigate, sections.GetSectionEditor, ConfirmEditorEngineeringApproval);
                }
                EngineeringSessionChanged?.Invoke(_engineering);
                EngineeringSourceSessionChanged();
                var session = _engineering;
                var child = await EngineeringMcpChild.StartAsync(root, session, cancellation);
                if (_disposed || cancellation.IsCancellationRequested || epoch != _engineeringEpoch || session != _engineering)
                { child.Dispose(); return; }
                _engineeringMcp = child;
                var description = await DescribeEngineeringMcpAsync(child, session, cancellation);
                Guid describeId;
                if (!Guid.TryParseExact(EngineeringProviderSession.Text(EngineeringProviderSession.Json().DeserializeObject(description) as Dictionary<string, object>, "operationId"), "D", out describeId)) throw new InvalidOperationException();
                if (_disposed || cancellation.IsCancellationRequested || epoch != _engineeringEpoch || session != _engineering) return;
                _engineeringPlcBuildVerified = EngineeringSupportsPlcOnlyBuild(description, session.Binding, describeId,
                    out _engineeringConfiguration, out _engineeringPlatform);
                _engineeringBuildButton.IsEnabled = _engineeringPlcBuildVerified && !_engineeringProjectSuspended
                    && !_engineeringUncertain && !EngineeringBuildGuard.HasUnresolved;
                SetEngineeringStatus(EngineeringBuildGuard.HasUnresolved ? "EngineeringUnknown"
                    : _engineeringPlcBuildVerified ? "EngineeringReady" : "EngineeringPlcBuildUnavailable");
            }
            catch
            {
                if (!_disposed && epoch == _engineeringEpoch) { DisposeEngineering(); SetEngineeringStatus("EngineeringUnavailable"); }
            }
        }

        private void DisposeEngineering()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ++_engineeringEpoch;
            RevokeEngineeringEditor();
            ClearEngineeringNavigationClick();
            _engineeringLifetime?.Cancel();
            _engineeringLifetime?.Dispose();
            _engineeringLifetime = null;
            _engineeringMcp?.Dispose();
            _engineeringMcp = null;
            var previous = _engineering;
            _engineeringSections?.Dispose(); _engineeringSections = null;
            _engineering = null;
            EngineeringSourceSessionChanged();
            try { previous?.Dispose(); }
            finally
            {
                // Transport cleanup must not prevent revocation of the provider.
                try { EngineeringSessionChanged?.Invoke(null); } catch { }
            }
            _engineeringProject = null;
            _engineeringInitialization = null;
            var interrupted = _engineeringBusy || _engineeringUncertain || EngineeringBuildGuard.HasUnresolved;
            _engineeringBusy = false;
            _engineeringUncertain = interrupted;
            _engineeringProjectSuspended = false;
            _engineeringPlcBuildVerified = false;
            _engineeringConfiguration = _engineeringPlatform = null;
            _engineeringBuildId = EngineeringBuildGuard.OperationId;
            if (_engineeringBuildButton != null) _engineeringBuildButton.IsEnabled = false;
            if (_engineeringCancelButton != null) _engineeringCancelButton.Visibility = Visibility.Collapsed;
            _engineeringOutput.Clear(); _engineeringRows.Children.Clear(); _engineeringDetails.Visibility = Visibility.Collapsed;
            SetEngineeringStatus(interrupted ? "EngineeringContextChanged" : "EngineeringNoProject");
        }

        private async Task StartEngineeringBuildAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var session = _engineering;
            if (_disposed || session == null || !_engineeringPlcBuildVerified || _engineeringBusy || _engineeringUncertain
                || _engineeringProjectSuspended || EngineeringBuildGuard.HasUnresolved || EngineeringSourceGuard.HasUnresolved || _sourceBusy) return;
            var binding = session.Binding;
            if (binding == null) return;
            var configuration = _engineeringConfiguration;
            var platform = _engineeringPlatform;
            if (string.IsNullOrWhiteSpace(configuration) || string.IsNullOrWhiteSpace(platform)) return;
            var confirmation = HostText.Format("EngineeringConfirm", new object[] { binding.SolutionFile, binding.ProjectFile, configuration, platform });
            if (MessageBox.Show(confirmation, HostText.Get("EngineeringBuild"), MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            // A modal dialog can pump IDE events. Never use a context from before it.
            if (_disposed || session != _engineering || !binding.Same(session.Binding) || _engineeringProjectSuspended
                || configuration != _engineeringConfiguration || platform != _engineeringPlatform) return;
            var lifetime = _engineeringLifetime.Token;
            _engineeringBusy = true;
            UpdateSourceButtons();
            _engineeringBuildButton.IsEnabled = false;
            _engineeringOutput.Clear(); _engineeringRows.Children.Clear(); _engineeringDetails.Visibility = Visibility.Collapsed;
            _engineeringBuildId = Guid.NewGuid();
            var build = _engineeringBuildId;
            if (!EngineeringBuildGuard.TryBegin(build)) { _engineeringBusy = false; return; }
            try
            {
                var expires = DateTime.UtcNow.AddSeconds(90);
                var request = session.CreateRequest("build.start", build, new { approvalExpiresUtc = expires.ToString("o"), configuration, platform });
                session.ApproveBuild(build, request, expires);
                SetEngineeringStatus("EngineeringStarting");
                var response = await EngineeringDispatchBoundedAsync(session, request, lifetime);
                if (!EngineeringCurrent(session, build)) return;
                var running = RenderEngineeringResponse(session, build, build, response);
                _engineeringCancelButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
                // Bounded observation. A timeout never means the IDE stopped.
                for (var poll = 0; running && poll < 40; ++poll)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), lifetime);
                    if (!EngineeringCurrent(session, build)) return;
                    if (_engineeringProjectSuspended) continue;
                    var operation = Guid.NewGuid();
                    request = session.CreateRequest("operation.get", operation, new { targetOperationId = build.ToString("D") });
                    response = await EngineeringDispatchBoundedAsync(session, request, lifetime);
                    if (!EngineeringCurrent(session, build)) return;
                    running = RenderEngineeringResponse(session, build, operation, response);
                }
                if (running && EngineeringCurrent(session, build)) { _engineeringUncertain = true; SetEngineeringStatus("EngineeringUnknown"); }
            }
            catch (OperationCanceledException) { if (EngineeringCurrent(session, build)) { _engineeringUncertain = true; SetEngineeringStatus("EngineeringUnknown"); } }
            catch { if (EngineeringCurrent(session, build)) { _engineeringUncertain = true; SetEngineeringStatus("EngineeringUnknown"); } }
            finally
            {
                if (EngineeringCurrent(session, build))
                {
                    _engineeringBusy = false;
                    UpdateSourceButtons();
                    _engineeringBuildButton.IsEnabled = _engineeringPlcBuildVerified && !_engineeringUncertain
                        && !_engineeringProjectSuspended && !EngineeringBuildGuard.HasUnresolved;
                    _engineeringCancelButton.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async Task RefreshEngineeringCapabilitiesAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var session = _engineering;
            if (_disposed || session == null || _engineeringBusy || _engineeringProjectSuspended) return;
            _engineeringRefreshButton.IsEnabled = false;
            _engineeringPlcBuildVerified = false;
            _engineeringBuildButton.IsEnabled = false;
            try
            {
                var response = await DescribeEngineeringMcpAsync(_engineeringMcp, session, _engineeringLifetime.Token);
                Guid id;
                if (!Guid.TryParseExact(EngineeringProviderSession.Text(EngineeringProviderSession.Json().DeserializeObject(response) as Dictionary<string, object>, "operationId"), "D", out id)) throw new InvalidOperationException();
                if (_disposed || session != _engineering || _engineeringProjectSuspended) return;
                _engineeringPlcBuildVerified = EngineeringSupportsPlcOnlyBuild(response, session.Binding, id,
                    out _engineeringConfiguration, out _engineeringPlatform);
                _engineeringBuildButton.IsEnabled = _engineeringPlcBuildVerified && !_engineeringUncertain && !EngineeringBuildGuard.HasUnresolved;
                SetEngineeringStatus(EngineeringBuildGuard.HasUnresolved ? "EngineeringUnknown"
                    : _engineeringPlcBuildVerified ? "EngineeringReady" : "EngineeringPlcBuildUnavailable");
            }
            catch { if (session == _engineering) SetEngineeringStatus("EngineeringUnavailable"); }
            finally { _engineeringRefreshButton.IsEnabled = true; }
        }

        private static async Task<string> DescribeEngineeringMcpAsync(EngineeringMcpChild child, EngineeringProviderSession session, CancellationToken cancellation)
        {
            if (child == null || session.Binding == null) throw new InvalidOperationException();
            var response = await child.CallToolAsync("tc_session_describe", "{}", cancellation);
            var result = EngineeringProviderSession.Json().DeserializeObject(response) as Dictionary<string, object>;
            object error, structured;
            if (result == null || (result.TryGetValue("isError", out error) && !Equals(error, false))
                || !result.TryGetValue("structuredContent", out structured) || !(structured is Dictionary<string, object> envelope)
                || EngineeringProviderSession.Text(envelope, "sessionId") != session.Binding?.SessionId.ToString("D")) throw new InvalidOperationException();
            return EngineeringProviderSession.Json().Serialize(envelope);
        }

        private async Task CancelEngineeringBuildAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var session = _engineering; var build = _engineeringBuildId;
            if (!_engineeringBusy || session == null || build == Guid.Empty) return;
            _engineeringCancelButton.IsEnabled = false;
            try
            {
                var operation = Guid.NewGuid();
                var request = session.CreateRequest("operation.cancel", operation, new { targetOperationId = build.ToString("D") });
                var response = await EngineeringDispatchBoundedAsync(session, request, _engineeringLifetime.Token);
                if (EngineeringCurrent(session, build))
                {
                    RenderEngineeringResponse(session, build, operation, response);
                    var envelope = EngineeringProviderSession.Json().DeserializeObject(response) as Dictionary<string, object>;
                    object resultObject, requested;
                    var result = envelope != null && envelope.TryGetValue("result", out resultObject) ? resultObject as Dictionary<string, object> : null;
                    if (result != null && result.TryGetValue("cancellationRequested", out requested) && Equals(requested, true))
                        SetEngineeringStatus("EngineeringCancelRequested");
                }
            }
            catch { if (EngineeringCurrent(session, build)) SetEngineeringStatus("EngineeringCancelUnknown"); }
            finally { _engineeringCancelButton.IsEnabled = true; }
        }

        private bool EngineeringCurrent(EngineeringProviderSession session, Guid build)
        { return !_disposed && session == _engineering && session.Binding != null && build == _engineeringBuildId; }

        private static bool EngineeringSupportsPlcOnlyBuild(string response, EngineeringBinding binding, Guid request,
            out string configuration, out string platform)
        {
            configuration = platform = null;
            if (response == null || response.Length > 512 * 1024 || binding == null) return false;
            var envelope = EngineeringProviderSession.Json().DeserializeObject(response) as Dictionary<string, object>;
            object version, resultObject, capabilitiesObject, validated;
            if (envelope == null || !envelope.TryGetValue("protocolVersion", out version) || !Equals(version, 1)
                || EngineeringProviderSession.Text(envelope, "sessionId") != binding.SessionId.ToString("D")
                || EngineeringProviderSession.Text(envelope, "operationId") != request.ToString("D")
                || EngineeringProviderSession.Text(envelope, "status") != "succeeded") return false;
            var result = envelope.TryGetValue("result", out resultObject) ? resultObject as Dictionary<string, object> : null;
            if (result == null || !result.TryGetValue("contextValidated", out validated) || !Equals(validated, true)) return false;
            var capabilities = result.TryGetValue("capabilities", out capabilitiesObject) ? capabilitiesObject as Dictionary<string, object> : null;
            // Generic build:"conditional" cannot authorize a PLC-only button:
            // DTE may build its containing TwinCAT project and other PLCs.
            if (EngineeringProviderSession.Text(capabilities, "plcBuild") != "supported"
                || EngineeringProviderSession.Text(result, "buildScope") != "plcProject"
                || !string.Equals(EngineeringProviderSession.Text(result, "targetProjectFile"), binding.ProjectFile, StringComparison.OrdinalIgnoreCase)) return false;
            configuration = EngineeringProviderSession.Text(result, "configuration");
            platform = EngineeringProviderSession.Text(result, "platform");
            return !string.IsNullOrWhiteSpace(configuration) && configuration.Length <= 256
                && !string.IsNullOrWhiteSpace(platform) && platform.Length <= 256;
        }

        private static async Task<string> EngineeringDispatchBoundedAsync(EngineeringProviderSession session, string request, CancellationToken cancellation)
        {
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                var dispatch = session.DispatchAsync(request, attempt.Token);
                var deadline = Task.Delay(TimeSpan.FromSeconds(20), cancellation);
                if (await Task.WhenAny(dispatch, deadline) != dispatch)
                {
                    attempt.Cancel();
                    // Observe a late fault without retrying or accepting late evidence.
                    _ = dispatch.ContinueWith(t => { var ignored = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    throw new OperationCanceledException();
                }
                return await dispatch;
            }
        }

        private bool RenderEngineeringResponse(EngineeringProviderSession session, Guid build, Guid request, string response)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (response == null || response.Length > 512 * 1024) throw new InvalidOperationException();
            var envelope = EngineeringProviderSession.Json().DeserializeObject(response) as Dictionary<string, object>;
            var binding = session.Binding;
            object version;
            if (envelope == null || binding == null || EngineeringProviderSession.Text(envelope, "sessionId") != binding.SessionId.ToString("D")
                || EngineeringProviderSession.Text(envelope, "operationId") != request.ToString("D")
                || !envelope.TryGetValue("protocolVersion", out version) || !Equals(version, 1)) throw new InvalidOperationException();
            var status = EngineeringProviderSession.Text(envelope, "status");
            string key;
            switch (status)
            {
                case "accepted": case "running": key = "EngineeringRunning"; break;
                case "succeeded": key = "EngineeringSucceeded"; break;
                case "failed": key = "EngineeringFailed"; break;
                case "refused": key = "EngineeringRefused"; break;
                case "conflict": key = "EngineeringContextChanged"; break;
                case "unsupported": key = "EngineeringUnsupported"; break;
                case "cancelled": key = "EngineeringCancelled"; break;
                default: key = "EngineeringUnknown"; _engineeringUncertain = true; break;
            }
            SetEngineeringStatus(key);
            object resultObject;
            var result = envelope.TryGetValue("result", out resultObject) ? resultObject as Dictionary<string, object> : null;
            if (result != null)
            {
                if (EngineeringProviderSession.Text(result, "buildOperationId") != build.ToString("D")) throw new InvalidOperationException();
                object provenanceObject, contextObject, pidObject;
                var provenance = envelope.TryGetValue("provenance", out provenanceObject) ? provenanceObject as Dictionary<string, object> : null;
                var observed = provenance != null && provenance.TryGetValue("context", out contextObject) ? contextObject as Dictionary<string, object> : null;
                DateTime started;
                if (observed == null || EngineeringProviderSession.Text(provenance, "buildOperationId") != build.ToString("D")
                    || EngineeringProviderSession.Text(observed, "sessionId") != binding.SessionId.ToString("D")
                    || !observed.TryGetValue("idePid", out pidObject) || !Equals(pidObject, binding.IdePid)
                    || EngineeringProviderSession.Text(observed, "contextEpoch") != binding.ContextEpoch
                    || !string.Equals(EngineeringProviderSession.Text(observed, "solutionFile"), binding.SolutionFile, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(EngineeringProviderSession.Text(observed, "projectFile"), binding.ProjectFile, StringComparison.OrdinalIgnoreCase)
                    || !DateTime.TryParse(EngineeringProviderSession.Text(observed, "ideStartedUtc"), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out started) || started != binding.IdeStartedUtc)
                    throw new InvalidOperationException();
                var output = EngineeringProviderSession.Text(envelope, "output");
                _engineeringOutput.Text = output == null ? HostText.Get("EngineeringOutputUnavailable") : output.Substring(0, Math.Min(output.Length, 65536));
                _engineeringRows.Children.Clear();
                object diagnostics;
                if (envelope.TryGetValue("diagnostics", out diagnostics) && diagnostics is object[] rows)
                {
                    for (var index = 0; index < rows.Length && index < 100; ++index)
                    {
                        var row = rows[index] as Dictionary<string, object>;
                        var severity = EngineeringProviderSession.Text(row, "severity");
                        if (severity != "error" && severity != "warning") continue;
                        object line, column;
                        var location = EngineeringBounded(EngineeringProviderSession.Text(row, "file"), 2048);
                        if (row.TryGetValue("line", out line) && row.TryGetValue("column", out column) && line is int && column is int)
                            location += " (" + line + ", " + column + ")";
                        var label = HostText.Get(severity == "error" ? "EngineeringError" : "EngineeringWarning") + ": "
                            + EngineeringBounded(EngineeringProviderSession.Text(row, "code"), 64) + " "
                            + EngineeringBounded(EngineeringProviderSession.Text(row, "message"), 4096) + Environment.NewLine + location;
                        object navigationAvailable;
                        var diagnosticId = EngineeringProviderSession.Text(row, "diagnosticId");
                        if (row.TryGetValue("navigationAvailable", out navigationAvailable) && Equals(navigationAvailable, true)
                            && EngineeringProviderSession.Text(row, "navigationKind") == "nativeErrorItemExactMatchRequired" && SourceDigest(diagnosticId))
                        {
                            var navigate = Secondary(AsyncButton(label, () => NavigateEngineeringDiagnosticAsync(session, build, diagnosticId)));
                            navigate.HorizontalContentAlignment = HorizontalAlignment.Left;
                            _engineeringRows.Children.Add(navigate);
                        }
                        else _engineeringRows.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) });
                    }
                }
                object outputTruncated, diagnosticsTruncated;
                if ((result.TryGetValue("outputTruncated", out outputTruncated) && Equals(outputTruncated, true))
                    || (result.TryGetValue("diagnosticsTruncated", out diagnosticsTruncated) && Equals(diagnosticsTruncated, true)))
                    _engineeringRows.Children.Add(new TextBlock { Text = HostText.Get("EngineeringTruncated"), TextWrapping = TextWrapping.Wrap });
                _engineeringDetails.Visibility = Visibility.Visible;
                if (status == "failed") _engineeringDetails.IsExpanded = true;
                if (status == "succeeded" || status == "failed")
                { EngineeringBuildGuard.Reconcile(build); _engineeringUncertain = false; }
            }
            else if (status == "succeeded" || status == "running" || status == "accepted" || status == "failed") throw new InvalidOperationException();
            // The pinned provider returns these start statuses only before
            // calling BuildProject. A later observation conflict is different.
            if (request == build && (status == "refused" || status == "unsupported" || status == "conflict"))
            { EngineeringBuildGuard.Reconcile(build); _engineeringUncertain = false; }
            if (EngineeringBuildGuard.HasUnresolved && status != "accepted" && status != "running") _engineeringUncertain = true;
            ApplyLanguageBindings();
            // A cancellation acknowledgement does not stop the IDE build.
            // Keep observing its correlated snapshot until terminal evidence
            // or the existing bounded polling deadline.
            return status == "accepted" || status == "running" || (status == "unknown" && result != null);
        }
        private static string EngineeringBounded(string value, int length) { return value == null ? "" : value.Substring(0, Math.Min(value.Length, length)); }
        private void SetEngineeringStatus(string key) { ThreadHelper.ThrowIfNotOnUIThread(); _engineeringStatus.Text = HostText.Get(key); }
    }
}
