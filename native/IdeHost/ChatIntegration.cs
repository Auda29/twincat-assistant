using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;

namespace TwinCatAssistant.Native
{
    // Chat automation behind the native context bar. The operator only decides
    // which POU state leaves the IDE (attach button or the auto-attach option)
    // and when a message is sent; sign-in, project chat selection and provider
    // enablement follow from the IDE context and OpenChamber's own settings.
    public sealed partial class AssistantControl
    {
        private readonly string _chatIdeInstance = Guid.NewGuid().ToString("N");
        private ChatBindingStore _chatBindings;
        private ActiveEditorSnapshot _chatActive;
        private NativeProjectIdentity _chatProject, _preparedProject;
        private Func<NativeProjectIdentity> _chatProjectReader;
        private string _projectWorkspace, _preparedMcpSettings, _mcpFailureKey;
        private string _mcpFailureMessage;
        private bool _projectSetupPending;
        private bool ProjectSetupFailed { get { return _chatProject != null && _mcpFailureKey?.StartsWith(ProjectKey(_chatProject) + "\0", StringComparison.Ordinal) == true; } }
        private string[] _projectWorkspaces;
        private string _knowledgeCorpus = "not_probed";
        private bool _mcpEnabled, _explicitGeneralRequested;
        private Func<ActiveEditorSnapshot> _chatSnapshotReader;
        private Func<string> _chatWorkspace;
        private Func<NativeChatConfiguration> _chatConfiguration = NativeChatConfiguration.Load;
        private Func<NativeChatConfiguration> _configurationLoader;
        private Task<NativeChatConfiguration> _configurationTask;
        private NativeChatConfiguration _connectedConfiguration;
        private string _chatBrowserProfile = null;
        private ChatSelection _chatSelection;
        private ChatContextBinding _chatBinding;
        private bool _chatActionPending, _chatAdapterReady, _chatPolling, _autoConnectAttempted, _automating;
        private readonly TextBlock _modelStatus = new TextBlock { Text = HostText.Get("H043"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        private readonly TextBlock _mcpStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        private DispatcherTimer _chatReconnect, _automationKick, _noticeTimer;
        private int _chatReconnectAttempts, _signInAttempts;
        private long _chatDocumentGeneration;
        private string _autoProjectKey;
        private string _automaticAttachmentFailure;
        private readonly AutoAttachmentAttempts _autoAttachment = new AutoAttachmentAttempts();
        private bool _autoAttaching;
        private readonly HashSet<string> _autoBindAttempted = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _consentAttempted = new HashSet<string>(StringComparer.Ordinal);
        private string _noticeText, _noticeTone, _noticeActionLabel;
        private Action _noticeAction;

        private void InitializeChatIntegration()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _chatWorkspace = () => System.IO.Path.Combine(ConnectedChatConfiguration().StateRoot, "workspace");
            _chatBindings = new ChatBindingStore(() => ConnectedChatConfiguration().StateRoot);
            Loaded += OnChatLoaded;
            _chatReconnect = new DispatcherTimer(DispatcherPriority.Background);
            _chatReconnect.Tick += ReconnectChat;
            _automationKick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
            _automationKick.Tick += OnAutomationKick;
            _noticeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(8) };
            _noticeTimer.Tick += (sender, args) => { _noticeTimer.Stop(); if (_noticeTone != "error") DismissNotice(); };
        }

        // Load and verify once per connection, away from the IDE thread. Polls,
        // rendering and binding lookups use the same state-root snapshot as the
        // connected service. Explicit reconnect revalidates all configuration.
        // Every launched Node operation still verifies its complete package.
        private async Task<NativeChatConfiguration> GetChatConfigurationAsync(bool refresh = false)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (refresh || _configurationTask == null || !ReferenceEquals(_configurationLoader, _chatConfiguration))
            {
                _configurationLoader = _chatConfiguration;
                _connectedConfiguration = null;
                _configurationTask = Task.Run(_configurationLoader);
            }
            var pending = _configurationTask;
            var result = await pending;
            if (ReferenceEquals(pending, _configurationTask)) _connectedConfiguration = result;
            return result;
        }
        private NativeChatConfiguration ConnectedChatConfiguration()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_connectedConfiguration == null)
                throw new InvalidOperationException("Chat configuration has not been loaded for this connection.");
            return _connectedConfiguration;
        }

        private void DisposeChatIntegration()
        {
            _chatReconnect?.Stop();
            if (_chatReconnect != null) _chatReconnect.Tick -= ReconnectChat;
            _automationKick?.Stop();
            if (_automationKick != null) _automationKick.Tick -= OnAutomationKick;
            _noticeTimer?.Stop();
            Loaded -= OnChatLoaded;
        }

        private void ScheduleChatReconnect()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || _chatReconnect.IsEnabled || _chatReconnectAttempts >= 3) return;
            _chatReconnect.Interval = TimeSpan.FromSeconds(new[] { 2, 5, 15 }[_chatReconnectAttempts++]);
            _chatReconnect.Start();
            RenderChatBinding();
        }

#pragma warning disable VSTHRD100 // Dispatcher timer and WPF event boundaries.
        private async void ReconnectChat(object sender, EventArgs args)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _chatReconnect.Stop();
                if (_disposed) return;
                await ConnectAsync();
                if (!_disposed && _chatConnection.Phase == "failed") ScheduleChatReconnect();
            }
            catch { if (!_disposed) ScheduleChatReconnect(); }
        }

        private async void OnChatLoaded(object sender, RoutedEventArgs args)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_autoConnectAttempted || _disposed) return;
            _autoConnectAttempted = true;
            try { await ConnectAsync(); } catch { if (!_disposed) RenderChatBinding(); }
        }

        private async void OnAutomationKick(object sender, EventArgs args)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _automationKick.Stop();
                if (!_disposed && _chatAdapterReady) await PollChatSelectionAsync();
            }
            catch { }
        }
#pragma warning restore VSTHRD100

        // Editor changes react within a fraction of a second instead of waiting
        // for the next health poll.
        private void KickAutomation()
        {
            if (_automationKick == null || _disposed || !_chatAdapterReady) return;
            _automationKick.Stop();
            _automationKick.Start();
        }

        private async Task InitializeChatAdapterAsync(long generation)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _chatAdapterReady = false; _chatSelection = null; _chatBinding = null;
            RenderChatBinding();
            try
            {
                string script;
                using (var stream = typeof(AssistantControl).Assembly.GetManifestResourceStream("TwinCatAssistant.EmbeddedChat.js"))
                {
                    if (stream == null || stream.Length > 100000) throw new InvalidOperationException();
                    using (var reader = new StreamReader(stream, Encoding.UTF8)) script = await reader.ReadToEndAsync();
                }
                await _browser.CoreWebView2.ExecuteScriptAsync(script
                    + "\nwindow.__twinCatInstall='loading';TwinCatEmbedded.installEmbeddedChat(window,"
                    + new JavaScriptSerializer().Serialize(_chatPreferences.Language)
                    + ").then(()=>window.__twinCatInstall='ready',()=>window.__twinCatInstall='failed');");
                if (_disposed || generation != _chatConnection.Generation) return;
                await PollChatSelectionAsync();
            }
            catch
            {
                if (!_disposed && generation == _chatConnection.Generation)
                {
                    _chatConnection.Fail(generation, HostText.Get("H044"));
                    ShowChatStatus();
                }
            }
        }

        private async Task<ChatSelection> ReadChatSelectionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || _browser.CoreWebView2 == null || !Allowed(_browser.CoreWebView2.Source)) return null;
            var documentGeneration = _chatDocumentGeneration;
            var browser = _browser.CoreWebView2;
            var serializer = new JavaScriptSerializer();
            // Operational readiness lets automation select/bind the project.
            // Presentation additionally waits for that selection's committed
            // history, not just an input field from the previous selection.
            // A confirmed session can be bound before React mounts its composer.
            // Keep that DOM/presentation gate out of operational readiness.
            var raw = await _browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify((()=>{const s=window.__twinCatChat?.state() ?? {version:1,ready:false};"
                + "const mounted=!!document.querySelector('[data-testid=\"chat-input\"],.oc-draft-center');"
                + "const loading=!!document.querySelector('#initial-loading,[data-chat-hydration-skeleton],[data-twincat-startup-overlay]');"
                + "return {...s,ready:s.ready === true,"
                + "presentationReady:s.presentationReady === true && mounted && !loading};})())");
            if (_disposed || documentGeneration != _chatDocumentGeneration || !ReferenceEquals(browser, _browser.CoreWebView2) || raw == null || raw.Length > 12000) return null;
            var json = serializer.Deserialize<string>(raw);
            return serializer.Deserialize<ChatSelection>(json);
        }

        private async Task PollChatSelectionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatPolling || _disposed || _browser.CoreWebView2 == null) return;
            _chatPolling = true;
            var generation = _chatConnection.Generation;
            var documentGeneration = _chatDocumentGeneration;
            var languageVersion = _languageObservationVersion;
            try
            {
                var selection = await ReadChatSelectionAsync();
                if (_disposed || generation != _chatConnection.Generation || documentGeneration != _chatDocumentGeneration || OlderChatSelection(selection)) return;
                _chatSelection = selection; _chatAdapterReady = selection?.ready == true;
                if (selection?.ready == true && languageVersion == _languageObservationVersion) ObserveChatLanguage(selection.language);
                if (selection?.theme != null) ApplyTheme(selection.theme);
                try { _chatBinding = selection?.IsSelected == true ? _chatBindings.Read(_origin.AbsoluteUri, selection.sessionId) : null; }
                catch { _chatBinding = null; }
                await PermitCurrentChatAsync();
                if (!_disposed && generation == _chatConnection.Generation) { RetryKnowledge(); RenderChatBinding(); }
            }
            catch
            {
                if (!_disposed && generation == _chatConnection.Generation)
                { _chatAdapterReady = false; _chatSelection = null; _chatBinding = null; RenderChatBinding(); }
            }
            finally { _chatPolling = false; }
            if (!_disposed && generation == _chatConnection.Generation) await RunAutomationAsync();
        }

        private async Task PermitCurrentChatAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var selection = _chatSelection;
            var active = _chatActive?.Identity?.ProjectIdentity?.Equals(_chatProject) == true ? _chatActive : null;
            var metadata = CanPromptCurrentChat() && !_chatBinding.IsGeneral ? ChatContextMetadata(active, _chatProject) : null;
            var permission = CanPromptCurrentChat() ? new { sessionId = selection.sessionId, selection.epoch, selection.documentId,
                contextKey = _chatBinding.IsGeneral ? "general" : ProjectKey(_chatProject), context = metadata } : null;
            await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat?.permit(" + new JavaScriptSerializer().Serialize(permission) + ")");
        }
        private object ChatContextMetadata(ActiveEditorSnapshot active, NativeProjectIdentity project)
        {
            return new { project = ProjectName(project.ProjectFile), @object = active?.ObjectName,
                treePath = active?.Identity?.TreePath, sourceKind = active?.SourceKind,
                objectKind = active?.ObjectKind, owner = active?.OwnerName, member = active?.MemberPath,
                reference = active == null ? null : ChatContextPolicy.ReferenceId(active) };
        }

        // One pass of automatic steps. Attachment retries are bounded and
        // delayed; confirmed staging is not replayed in the same context.
        private async Task RunAutomationAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_automating || _disposed || _holdingView || _chatActionPending || !_chatAdapterReady || _chatSelection == null || _origin == null) return;
            _automating = true;
            try
            {
                // Corpus discovery is independent of selecting the project chat.
                // It can include package checks and must not consume the chat's
                // presentation deadline before project preparation even starts.
                RunAction(() => RefreshKnowledgeAsync());
                if (_disposed || _chatSelection == null || !_chatAdapterReady) return;
                if (_chatSelection.authenticated != true) { await SignInAgainAsync(); return; }
                _signInAttempts = 0;
                var identity = _chatProject;
                var selected = _chatSelection.IsSelected;
                if (identity != null)
                {
                    if (!await PrepareProjectWorkspaceAsync(identity)) return;
                    var key = identity.SolutionFile + "\0" + identity.ProjectFile + "\0" + _chatDocumentGeneration;
                    var generalChat = _chatBinding?.IsGeneral == true && CanPromptCurrentChat();
                    if (key != _autoProjectKey)
                    {
                        // Opening or switching to a PLC project restores its chosen
                        // conversation. A deliberately used general chat stays open.
                        _autoProjectKey = key;
                        if (!generalChat || !_explicitGeneralRequested) { await OpenProjectChatAsync(); return; }
                    }
                    else if (selected && !HasCurrentChatBinding() && !generalChat
                        && (_chatBinding == null || _chatBinding.IsGeneral || _chatBinding.SameProjectContext(identity))
                        && _autoBindAttempted.Add(_chatSelection.sessionId + "\0" + key))
                    {
                        if (_mcpEnabled && _projectWorkspaces?.Any(directory => SameDirectory(directory, _chatSelection.directory)) == true
                            && !await PrepareProjectWorkspaceAsync(identity, _chatSelection.directory)) return;
                        // A conversation opened from the history: adopt unbound and
                        // same-project chats; general chats stay general.
                        if (_chatBinding?.IsGeneral == true) await BindGeneralAsync();
                        else await BindCurrentChatAsync();
                        return;
                    }
                }
                else if (selected && !CanPromptCurrentChat() && (_chatBinding == null || _chatBinding.IsGeneral)
                    && _autoBindAttempted.Add(_chatSelection.sessionId + "\0general"))
                {
                    await BindGeneralAsync();
                    return;
                }
                var provider = ChatContextPolicy.ProviderToEnable(_chatSelection);
                if (provider != null && CanPromptCurrentChat() && _consentAttempted.Add(provider)) { await AllowProviderAsync(provider); return; }
                if (!HasCurrentChatBinding() || _chatActive == null) _autoAttachment.Reset();
                if (_chatPreferences.AutoAttach && HasCurrentChatBinding() && _chatActive != null && _chatSelection.presentationReady && !_chatSelection.manualAttachmentPending)
                {
                    var revision = ChatContextPolicy.Revision(_chatActive);
                    var attempt = AutoAttachmentKey(revision);
                    if (ActiveChip()?.revision != revision && _autoAttachment.CanAttempt(attempt, DateTime.UtcNow))
                    {
                        _autoAttachment.Begin(attempt, DateTime.UtcNow);
                        if (await AttachActivePouAsync(true)) _autoAttachment.Confirm(attempt);
                        else if (_autoAttachment.Exhausted(attempt))
                            Notify(_automaticAttachmentFailure ?? HostText.Get("H045"), "warning", HostText.Get("H046"),
                                () => { _autoAttachment.Reset(); DismissNotice(); KickAutomation(); });
                    }
                }
            }
            catch { }
            finally { _automating = false; }
        }

        private async Task SignInAgainAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_signInAttempts >= 3 || _origin == null) return;
            _signInAttempts++;
            if (await SignInBrowserAsync(_origin.Port) && !_disposed) _browser.CoreWebView2.Reload();
        }

        private bool HasCurrentChatBinding()
        {
            return !_disposed && _chatAdapterReady && _chatSelection?.IsSelected == true
                && _chatBinding?.MatchesProject(_chatProject, _chatIdeInstance, _origin?.AbsoluteUri, _chatSelection.sessionId) == true
                && _preparedProject?.Equals(_chatProject) == true
                && SameDirectory(_chatSelection.directory, _projectWorkspace);
        }
        private bool CanPromptCurrentChat()
        {
            return HasCurrentChatBinding() || (!_disposed && _chatAdapterReady && _chatSelection?.IsSelected == true
                && _chatBinding?.MatchesGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id,
                    _origin?.AbsoluteUri, _chatSelection.sessionId) == true
                && SameDirectory(_chatSelection.directory, _chatWorkspace()));
        }

        // The composer chip of the active POU in the selected chat, if staged.
        private ChatAttachment ActiveChip()
        {
            if (_chatActive?.Identity == null || _chatSelection?.attachments == null) return null;
            var id = ChatContextPolicy.ReferenceId(_chatActive);
            return _chatSelection.attachments.Where(chip => (chip?.reference ?? chip?.id) == id).LastOrDefault();
        }

        private string ProjectName(string projectFile) { return string.IsNullOrEmpty(projectFile) ? "" : System.IO.Path.GetFileNameWithoutExtension(projectFile); }
        private string AutoAttachmentKey(string revision) { return revision == null || _chatSelection?.IsSelected != true ? null : _chatSelection.sessionId + "\0" + _chatSelection.documentId + "\0" + revision; }

        private void RenderChatBinding()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (_attachPouButton != null && !_disposed)
                {
                    RenderLoading();
                    LayoutSidebar();
                }

                if (_attachPouButton == null || _disposed || _holdingView)
                    return;
                RenderKnowledge();
                // Automatic attachment runs quietly; only operator actions disable the bar.
                var ready = (!_chatActionPending || _autoAttaching) && _chatAdapterReady && _chatSelection?.authenticated == true;
                var identity = _chatProject;
                var foreign = _chatSelection?.IsSelected == true && _chatBinding != null && !_chatBinding.IsGeneral && identity != null && !_chatBinding.SameProjectContext(identity);
                _historyButton.IsEnabled = ready;
                _newChatButton.IsEnabled = ready;
                _newChatButton.ToolTip = identity == null ? HostText.Get("H047") : HostText.Format("H048", new object[] { ProjectName(identity.ProjectFile) });
                // Attach button: the one explicit disclosure action.
                var revision = ChatContextPolicy.Revision(_chatActive);
                var chip = HasCurrentChatBinding() ? ActiveChip() : null;
                // Keep one auto-chip, but do not claim it is staged until the input
                // queue or the original composer confirms its exact POU revision.
                var autoPending = _chatPreferences.AutoAttach && revision != null && chip?.revision != revision && HasCurrentChatBinding() && (_autoAttaching || _autoAttachment.CanAttempt(AutoAttachmentKey(revision), DateTime.UtcNow));
                var attachedCurrent = revision != null && chip?.revision == revision;
                var staging = autoPending || (attachedCurrent && _chatSelection.attachmentState == "staging");
                var reference = _chatActive?.Identity == null ? null : ChatContextPolicy.ReferenceId(_chatActive);
                var submittedCurrent = !attachedCurrent && reference != null && revision != null && HasCurrentChatBinding() && _chatSelection.submittedAttachments?.Any(item => item?.reference == reference && item.revision == revision) == true;
                var stale = !attachedCurrent && chip != null;
                _attachPouButton.IsEnabled = ready && _chatActive?.Identity != null && !foreign && !attachedCurrent && !staging && _chatSelection?.IsSelected == true && _chatSelection.presentationReady;
                _attachLabel.Text = staging ? HostText.Get("H001") : attachedCurrent ? HostText.Get("H002") : submittedCurrent ? HostText.Get("H003") : stale ? HostText.Get("H004") : HostText.Get("H000");
                _attachIcon.Data = AssistantTheme.Icon(attachedCurrent ? "check" : stale ? "refresh" : "attach");
                _attachPouButton.ToolTip = _chatActive?.Identity == null ? HostText.Format("H049", new object[] { _attachLabel.Text }) : attachedCurrent ? HostText.Format("H050", new object[] { _chatActive.ObjectName }) : HostText.Format("H051", new object[] { _attachLabel.Text, _chatActive.ObjectName });
                // Status chip.
                string status, tone;
                // The chip describes the POU context; connection problems use the banner.
                if (identity == null || _chatBinding?.IsGeneral == true)
                {
                    status = HostText.Get("H052");
                    tone = "Neutral";
                }
                else if (foreign)
                {
                    status = HostText.Format("H054", new object[] { ProjectName(_chatBinding.ProjectFile) });
                    tone = "Error";
                }
                else if (_chatActive == null)
                {
                    status = HostText.Get("H056");
                    tone = "Neutral";
                }
                else if (stale)
                {
                    status = HostText.Get("H057");
                    tone = "Warning";
                }
                else if (_chatActive.Dirty?.State == "known-dirty")
                {
                    status = HostText.Get("H059");
                    tone = "Warning";
                }
                else if (_chatActive.Dirty?.State == "known-clean")
                {
                    status = HostText.Get("H060");
                    tone = "Success";
                }
                else
                {
                    status = HostText.Get("H062");
                    tone = "Neutral";
                }

                if (_statusText.Text != status)
                    _statusText.Text = status;
                _statusChip.Visibility = identity == null && _chatBinding?.IsGeneral != true ? Visibility.Collapsed : Visibility.Visible;
                _statusChip.SetResourceReference(Border.BackgroundProperty, "Tc." + tone + "Surface");
                _statusDot.SetResourceReference(Shape.FillProperty, "Tc." + tone);
                _statusText.SetResourceReference(TextBlock.ForegroundProperty, tone == "Neutral" ? "Tc.Muted" : "Tc." + tone);
                _statusChip.ToolTip = _chatActive?.SourceNotice == null ? status : HostText.Format("SourceState", status, HostText.DisplayStatus(_chatActive.SourceNotice));
                // Model provider status lives in Diagnose.
                var allowed = _chatSelection?.allowedProviders ?? new string[0];
                var connected = _chatSelection?.providers ?? new ChatProviderChoice[0];
                var servers = _chatSelection?.mcp ?? new ChatMcpStatus[0];
                _mcpStatus.Text = (_chatSelection?.mcpObservation?.stale == true ? HostText.Get("H064") : HostText.Get("H065")) + (servers.Length == 0 ? HostText.Get("H066") : string.Join("; ", servers.Select(server => (server.name == "knowledge" ? "Knowledge" : "TwinCAT") + ": " + (server.status == "connected" ? HostText.Get("McpConnected") : server.status == "pending" ? HostText.Get("McpConnecting") : HostText.Get("H069"))))) + (_mcpEnabled ? HostText.Format("H070", new object[] { (_knowledgeCorpus == "not_configured" ? HostText.Get("H071") : HostText.Get("H072")) }) : servers.Length == 0 ? HostText.Get("H073") : HostText.Get("H074"));
                if (!_chatActionPending)
                    _modelStatus.Text = connected.Length == 0 ? HostText.Get("H043") : HostText.Format("H075", new object[] { string.Join(", ", connected.Select(choice => choice.name + (allowed.Contains(choice.id) ? HostText.Get("H076") : ""))), (string.IsNullOrEmpty(_chatSelection?.model) ? HostText.Get("H077") : HostText.Format("H078", new object[] { _chatSelection.model })) });
                RenderBanner(ready, identity, foreign);
                RenderDiagnostics();
                UpdateSidebar();
            }
            finally
            {
                ApplyLanguageBindings();
            }
        }

        private void RenderBanner(bool ready, NativeProjectIdentity identity, bool foreign)
        {
            string text = null, tone = "Neutral", action = null;
            Action command = null;
            var dismissable = false;
            if (_setupMissing) { text = HostText.Get("H079"); tone = "Error"; action = HostText.Get("H080"); command = ShowDiagnostics; }
            else if (_chatConnection.Phase == "failed")
            {
                text = _chatReconnect?.IsEnabled == true ? HostText.Get("H081") : HostText.Get("H082");
                tone = "Error"; action = HostText.Get("H083"); command = Reconnect;
            }
            else if (ProjectSetupFailed)
            {
                text = _mcpFailureMessage; tone = "Error"; action = HostText.Get("H046");
                command = () => { _mcpFailureKey = null; DismissNotice(); KickAutomation(); };
            }
            else if (_noticeText != null) { text = _noticeText; tone = _noticeTone == "error" ? "Error" : _noticeTone == "warning" ? "Warning" : "Neutral"; action = _noticeActionLabel; command = _noticeAction; dismissable = true; }
            else if (foreign)
            {
                text = HostText.Format("H084", new object[] { ProjectName(_chatBinding.ProjectFile) });
                tone = "Error"; action = HostText.Format("H048", new object[] { ProjectName(identity.ProjectFile) }); command = () => RunAction(() => NewChatAsync(false));
            }
            else if (ready && identity == null && _chatSelection?.IsSelected == true && _chatBinding != null && !_chatBinding.IsGeneral)
            {
                text = HostText.Format("H085", new object[] { ProjectName(_chatBinding.ProjectFile) });
                action = HostText.Get("H052"); command = () => RunAction(() => NewChatAsync(true));
            }
            else if (ready && _chatSelection?.IsSelected == true && identity != null && !HasCurrentChatBinding() && _chatBinding != null
                && !_chatBinding.IsGeneral && _chatBinding.SameProjectContext(identity))
            {
                text = _mcpEnabled && !SameDirectory(_chatSelection.directory, _projectWorkspace)
                    ? HostText.Get("H086")
                    : HostText.Get("H087");
                tone = "Warning"; action = HostText.Get("H088"); command = () => RunAction(() => NewChatAsync(false));
            }
            else if (ready && (_chatSelection.providers?.Length ?? 0) == 0)
            {
                text = HostText.Get("H089");
                action = HostText.Get("H090"); command = () => RunAction(OpenProviderSettingsAsync);
            }
            else if (ready && _knowledgeBusy)
            {
                text = HostText.Get("H091");
                action = HostText.Get("H092"); command = ShowKnowledgeSetup;
            }
            else if (ready && _knowledgeState?.phase == "update")
            {
                text = HostText.Get("H093");
                action = HostText.Get("H004"); command = ShowKnowledgeSetup;
            }
            else if (ready && (_knowledgeState?.phase == "available" || _knowledgeState?.phase == "missing" || _knowledgeState?.phase == "error"))
            {
                text = _knowledgeState.phase == "available" ? HostText.Get("H094")
                    : _knowledgeState.phase == "missing" ? HostText.Get("H095")
                    : HostText.Get("H096");
                action = HostText.Get("H097"); command = ShowKnowledgeSetup;
            }
            _banner.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
            if (text == null) return;
            if (_bannerText.Text != text) _bannerText.Text = text;
            _banner.SetResourceReference(Border.BackgroundProperty, "Tc." + tone + "Surface");
            _bannerAction.Content = action;
            _bannerAction.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
            _bannerCommand = command;
            _bannerDismiss.Visibility = dismissable ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Notify(string text, string tone = "info", string actionLabel = null, Action action = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            _noticeText = text; _noticeTone = tone; _noticeActionLabel = actionLabel; _noticeAction = action;
            _noticeTimer.Stop();
            if (tone != "error") _noticeTimer.Start();
            RenderChatBinding();
        }
        private void DismissNotice()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _noticeText = null; _noticeAction = null; _noticeActionLabel = null;
            RenderChatBinding();
        }

#pragma warning disable VSTHRD100 // UI command boundary; exceptions never leave it.
        private async void RunAction(Func<Task> action) { try { await action(); } catch { } }
#pragma warning restore VSTHRD100
        private void Reconnect() { _chatReconnectAttempts = 0; _chatReconnect?.Stop(); RunAction(ConnectAsync); }
        private void ShowDiagnostics() { _diagnostics.Visibility = Visibility.Visible; _menuPopup.IsOpen = false; }

        private void ToggleMenu()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                DismissProjectChatOverlay();
                _previewPopup.IsOpen = false;
                if (_menuPopup.IsOpen || JustClosed(_menuClosedAt))
                {
                    _menuPopup.IsOpen = false;
                    return;
                }

                _menuItems.Children.Clear();
                var ready = _chatAdapterReady && _chatSelection?.authenticated == true && !_chatActionPending;
                // Chat: actions and the one disclosure preference.
                MenuRow("chat", HostText.Get("H047"), HostText.Get("H098"), ready, () => NewChatAsync(true));
                MenuRow("history", HostText.Get("H099"), HostText.Get("H100"), ready, () => ShowProjectChatsAsync(true));
                var exportReady = ready && _chatSelection?.IsSelected == true && _chatSelection.canExport && !_threadExporting;
                MenuRow("copy", HostText.Get("H101"), HostText.Get("H102"), exportReady, () => ExportThreadAsync(false));
                MenuRow("download", HostText.Get("H103"), HostText.Get("H104"), exportReady, () => ExportThreadAsync(true));
                MenuRow("attach", HostText.Get("H105"), HostText.Get("H106"), true, ToggleAutoAttachAsync, MenuSwitch(_chatPreferences.AutoAttach));
                // Model providers: a single choice among the connected ones.
                MenuSeparator();
                MenuHeading(HostText.Get("LanguageLabel"));
                MenuRow(null, HostText.Get("H110"), null, true, () => SelectLanguageAsync("de"), leading: MenuRadio(HostText.Language == "de"));
                MenuRow(null, HostText.Get("H111"), null, true, () => SelectLanguageAsync("en"), leading: MenuRadio(HostText.Language == "en"));
                MenuSeparator();
                MenuHeading(HostText.Get("H107"));
                var providers = _chatSelection?.providers ?? new ChatProviderChoice[0];
                if (providers.Length > 1)
                    foreach (var provider in providers)
                    {
                        var id = provider.id;
                        MenuRow(null, provider.name, null, ready && CanPromptCurrentChat(), () => AllowProviderAsync(id), leading: MenuRadio(_chatSelection.allowedProviders?.Contains(id) == true));
                    }

                MenuRow("sliders", HostText.Get("H108"), null, ready, OpenProviderSettingsAsync);
                // Knowledge: state on the right, setup behind the row.
                MenuSeparator();
                string knowledgeDetail = HostText.Get("H109"), knowledgeStatus = HostText.Get("H097"), knowledgeTone = "Warning";
                var language = _knowledgeState?.language == "de" ? HostText.Get("H110") : HostText.Get("H111");
                if (_knowledgeBusy)
                {
                    knowledgeDetail = HostText.Get("H112");
                    knowledgeStatus = HostText.Get("H113");
                    knowledgeTone = "Neutral";
                }
                else if (_knowledgeState?.ready == true)
                {
                    knowledgeDetail = HostText.Format("H114", new object[] { language, (_knowledgeState.phase == "update" ? HostText.Get("H115") : "") });
                    // The live MCP state wins over the last confirmed reload; while
                    // automatic reconnects remain, a failed state shows as connecting.
                    var live = McpState("knowledge");
                    if (McpFailed(live) && !(live == "failed" && KnowledgeRetrying()))
                    {
                        knowledgeStatus = HostText.Get("H116");
                        knowledgeTone = "Error";
                    }
                    else if (KnowledgeConnected())
                    {
                        knowledgeStatus = HostText.Get("H117");
                        knowledgeTone = "Success";
                    }
                    else if (live == "unknown")
                    {
                        knowledgeStatus = HostText.Get("H118");
                        knowledgeTone = "Neutral";
                    }
                    else
                    {
                        knowledgeStatus = HostText.Get("H119");
                        knowledgeTone = "Neutral";
                    }
                }

                MenuRow("book", HostText.Get("H120"), knowledgeDetail, true, () =>
                {
                    ShowKnowledgeSetup();
                    return Task.CompletedTask;
                }, MenuStatus(knowledgeStatus, knowledgeTone));
                // Connection maintenance.
                MenuSeparator();
                MenuRow("refresh", HostText.Get("H083"), null, !_connecting, () =>
                {
                    Reconnect();
                    return Task.CompletedTask;
                });
                MenuRow("pulse", HostText.Get("H121"), null, true, () =>
                {
                    _diagnostics.Visibility = _diagnostics.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                    return Task.CompletedTask;
                }, MenuSwitch(_diagnostics.Visibility == Visibility.Visible));
                _menuPopup.IsOpen = true;
            }
            finally
            {
                ApplyLanguageBindings();
            }
        }

        // Menu row: icon (or radio), label with an optional muted detail line,
        // and an optional trailing switch or status. Labels wrap, never clip.
        private void MenuRow(string icon, string label, string detail, bool enabled, Func<Task> action, UIElement trailing = null, UIElement leading = null)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (leading == null && icon != null)
            {
                var path = Icon(icon);
                path.SetResourceReference(Shape.StrokeProperty, "Tc.Muted");
                leading = path;
            }
            if (leading is FrameworkElement lead) { lead.HorizontalAlignment = HorizontalAlignment.Left; lead.VerticalAlignment = VerticalAlignment.Top; lead.Margin = new Thickness(0, 1, 0, 0); }
            if (leading != null) grid.Children.Add(leading);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock { Text = label, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Foreground");
            text.Children.Add(title);
            if (detail != null)
            {
                var sub = new TextBlock { Text = detail, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
                sub.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
                text.Children.Add(sub);
            }
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (trailing is FrameworkElement trail)
            {
                trail.Margin = new Thickness(10, 0, 0, 0);
                trail.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(trail, 2);
                grid.Children.Add(trail);
            }
            var row = AsyncButton(null, async () => { _menuPopup.IsOpen = false; await action(); });
            row.SetResourceReference(StyleProperty, "Tc.MenuRow");
            row.Content = grid;
            row.IsEnabled = enabled;
            System.Windows.Automation.AutomationProperties.SetName(row, label);
            _menuItems.Children.Add(row);
        }

        private void MenuSeparator()
        {
            var line = new Border { Height = 1, Margin = new Thickness(6, 5, 6, 5) };
            line.SetResourceReference(Border.BackgroundProperty, "Tc.Border");
            _menuItems.Children.Add(line);
        }

        private void MenuHeading(string heading)
        {
            var text = new TextBlock { Text = heading, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 2, 8, 3) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            _menuItems.Children.Add(text);
        }

        private static FrameworkElement MenuSwitch(bool on)
        {
            var thumb = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            thumb.SetResourceReference(Shape.FillProperty, on ? "Tc.PrimaryForeground" : "Tc.Muted");
            var track = new Border { Width = 28, Height = 16, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = thumb };
            track.SetResourceReference(Border.BackgroundProperty, on ? "Tc.Primary" : "Tc.NeutralSurface");
            track.SetResourceReference(Border.BorderBrushProperty, on ? "Tc.Primary" : "Tc.Muted");
            return track;
        }

        private static FrameworkElement MenuRadio(bool selected)
        {
            var ring = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1.5) };
            ring.SetResourceReference(Border.BorderBrushProperty, selected ? "Tc.Primary" : "Tc.Muted");
            if (selected)
            {
                var dot = new Ellipse { Width = 6, Height = 6 };
                dot.SetResourceReference(Shape.FillProperty, "Tc.Primary");
                ring.Child = dot;
            }
            return ring;
        }

        private static FrameworkElement MenuStatus(string label, string tone)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.FillProperty, "Tc." + tone);
            var text = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, tone == "Neutral" ? "Tc.Muted" : "Tc." + tone);
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, text } };
        }

        private async Task ToggleAutoAttachAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _chatPreferences.AutoAttach = !_chatPreferences.AutoAttach;
            try { _chatPreferences.Save(); } catch { Notify(HostText.Get("H122"), "warning"); }
            _autoAttachment.Reset();
            if (_chatPreferences.AutoAttach) Notify(HostText.Get("H123"));
            else
            {
                await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat?.clearFollowing()");
                Notify(HostText.Get("H124"));
            }
            await PollChatSelectionAsync();
        }

        private async Task OpenHistoryAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            await ShowProjectChatsAsync(false);
        }

        private async Task OpenProviderSettingsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatAdapterReady && !_disposed) await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat.openSettings()");
        }

        private ActiveEditorSnapshot FreshChatSnapshot()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = _chatSnapshotReader?.Invoke();
            if (result?.Identity == null) throw new InvalidOperationException(HostText.Get("H125"));
            return result;
        }

        private NativeProjectIdentity ReadProjectIdentity()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _chatProjectReader != null ? _chatProjectReader() : _chatSnapshotReader?.Invoke()?.Identity?.ProjectIdentity;
        }
        private NativeProjectIdentity FreshProjectIdentity()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var project = ReadProjectIdentity();
            if (project == null) throw new InvalidOperationException(HostText.Get("H126"));
            return project;
        }
        private static string ProjectKey(NativeProjectIdentity project)
        {
            return project == null ? null : ChatContextPolicy.Hash(project.IdeProcessId + "\0" + project.SolutionFile + "\0" + project.ProjectFile);
        }
        private static bool SameDirectory(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
                && string.Equals(a.Replace('\\', '/').TrimEnd('/'), b.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }
        private async Task<bool> PrepareProjectWorkspaceAsync(NativeProjectIdentity project, string workspace = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var config = await GetChatConfigurationAsync();
            _mcpEnabled = !string.IsNullOrEmpty(config.McpSettingsPath);
            if (_preparedProject?.Equals(project) == true && _preparedMcpSettings == config.McpSettingsPath
                && (workspace == null || SameDirectory(workspace, _projectWorkspace))) return true;
            var key = ProjectKey(project) + "\0" + config.McpSettingsPath + "\0" + workspace;
            if (_mcpFailureKey == key) return false;
            _preparedProject = null; _projectWorkspace = null; _projectWorkspaces = null;
            _mcpFailureKey = null; _mcpFailureMessage = null;
            var wasPending = _chatActionPending;
            var readinessPending = _chatReadyTimeout.IsEnabled;
            _chatReadyTimeout.Stop();
            _projectSetupPending = true;
            _chatActionPending = true; RenderChatBinding();
            try
            {
                var prepared = _mcpEnabled ? await NativeMcpProject.PrepareAsync(config, project, workspace) : null;
                var directory = prepared == null ? _chatWorkspace() : prepared.workspace;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed || !project.Equals(ReadProjectIdentity())) return false;
                _projectWorkspace = directory; _preparedProject = project; _preparedMcpSettings = config.McpSettingsPath;
                _projectWorkspaces = prepared?.workspaces ?? new[] { directory };
                _knowledgeCorpus = prepared?.corpus ?? "not_probed";
                return true;
            }
            catch (Exception error)
            {
                if (!_disposed)
                {
                    _mcpFailureKey = key;
                    var failure = (error as NativeMcpSetupException)?.Failure;
                    _mcpFailureMessage = failure == "project_locked"
                        ? HostText.Get("ProjectSetupLocked")
                        : failure == "timeout"
                        ? HostText.Get("ProjectSetupTimeout")
                        : HostText.Get("H127");
                    Notify(_mcpFailureMessage, "error", HostText.Get("H046"),
                        () => { _mcpFailureKey = null; DismissNotice(); KickAutomation(); });
                }
                return false;
            }
            finally
            {
                _projectSetupPending = false;
                _chatActionPending = wasPending;
                if (!_disposed)
                {
                    if (readinessPending && !_chatPresented && !ProjectSetupFailed) _chatReadyTimeout.Start();
                    RenderChatBinding();
                }
            }
        }

        private Task NewProjectChatAsync() { return NewChatAsync(false); }

        // Restore the chosen conversation, with newest active chat as fallback; create
        // a new one when none exists or it is held by another live IDE.
        private async Task OpenProjectChatAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var project = ReadProjectIdentity();
            if (project == null || _chatActionPending || !await PrepareProjectWorkspaceAsync(project)) return;
            HashSet<string> known;
            string remembered;
            try { known = _chatBindings.SessionsForProject(_origin.AbsoluteUri, project); remembered = _chatBindings.SelectedForProject(project); }
            catch
            {
                Notify(HostText.Get("H128"), "error");
                return;
            }
            {
                _chatActionPending = true; RenderChatBinding();
                var documentGeneration = _chatDocumentGeneration;
                try
                {
                    // Read the durable session list through the existing runtime,
                    // independently of the UI's still-empty bootstrap cache.
                    var entries = await ReadRecentSessionsAsync();
                    if (_disposed || documentGeneration != _chatDocumentGeneration || !project.Equals(ReadProjectIdentity())) return;
                    foreach (var latest in entries.Where(entry => !entry.archived && (known.Contains(entry.id) || _mcpEnabled))
                        .OrderByDescending(entry => entry.id == remembered).ThenByDescending(entry => entry.updated))
                    {
                        if (_mcpEnabled && !SameDirectory(latest.directory, _projectWorkspace)
                            && !await PrepareProjectWorkspaceAsync(project, latest.directory))
                            throw new InvalidOperationException(HostText.Get("H129"));
                        try { _chatBindings.Bind(ChatContextBinding.CreateProject(project, _chatIdeInstance, _origin.AbsoluteUri, latest.id)); }
                        catch (InvalidOperationException) { continue; }
                        if (!await SelectSessionAsync(latest.id)) throw new InvalidOperationException(HostText.Get("H130"));
                        if (_disposed || documentGeneration != _chatDocumentGeneration || !project.Equals(ReadProjectIdentity())) return;
                        try { await BindSelectionAsync(FreshProjectIdentity()); return; }
                        catch (InvalidOperationException) { /* Try another chat if this one has a live IDE owner. */ }
                    }
                }
                catch
                {
                    if (!_disposed) Notify(HostText.Get("H131"), "error", HostText.Get("H046"),
                        () => { _autoProjectKey = null; DismissNotice(); KickAutomation(); });
                    return;
                }
                finally { _chatActionPending = false; if (!_disposed) await PollChatSelectionStateAsync(); }
            }
            if (!_disposed && project.Equals(ReadProjectIdentity())) await NewChatAsync(false);
        }

        private async Task PollChatSelectionStateAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var selection = await ReadChatSelectionAsync();
            if (_disposed || selection == null || OlderChatSelection(selection)) return;
            _chatSelection = selection;
            try { _chatBinding = selection.IsSelected ? _chatBindings.Read(_origin.AbsoluteUri, selection.sessionId) : null; } catch { _chatBinding = null; }
            await PermitCurrentChatAsync();
            RenderChatBinding();
        }

        private bool OlderChatSelection(ChatSelection selection)
        {
            return selection != null && _chatSelection != null && selection.documentId == _chatSelection.documentId && selection.epoch < _chatSelection.epoch;
        }

        private async Task<ChatSessionEntry[]> ReadRecentSessionsAsync(bool includeArchived = false, string[] selectedDirectories = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var serializer = new JavaScriptSerializer { MaxJsonLength = 24000000 };
            var generation = _chatDocumentGeneration;
            var directory = _projectWorkspace ?? _chatWorkspace();
            var directories = selectedDirectories ?? _projectWorkspaces ?? new[] { directory };
            var requestId = Guid.NewGuid().ToString("N");
            var slot = "window.__twinCatHistories['" + requestId + "']";
            await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatHistories??={};" + slot + "={id:'" + requestId + "',phase:'pending'};window.__twinCatChat.storedSessions("
                + serializer.Serialize(directories) + "," + (includeArchived ? "true" : "false") + ").then(items=>{if(" + slot + ")" + slot
                + "={id:'" + requestId + "',phase:'ready',items};},()=>{if(" + slot + ")" + slot + "={id:'" + requestId + "',phase:'failed'};});");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            try
            {
                while (!_disposed && generation == _chatDocumentGeneration && (selectedDirectories != null || SameDirectory(directory, _projectWorkspace ?? _chatWorkspace())) && DateTime.UtcNow < deadline)
                {
                    var raw = await _browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify(" + slot + ")");
                    if (raw == null || raw.Length > 24000000) throw new InvalidOperationException(HostText.Get("H132"));
                    var result = serializer.Deserialize<ChatHistoryResult>(serializer.Deserialize<string>(raw));
                    if (result?.id != requestId || result.phase == "failed") throw new InvalidOperationException(HostText.Get("H133"));
                    if (result.phase == "ready" && result.items != null) return result.items;
                    await Task.Delay(150);
                }
                throw new InvalidOperationException(HostText.Get("H134"));
            }
            finally { if (!_disposed && generation == _chatDocumentGeneration) await _browser.CoreWebView2.ExecuteScriptAsync("delete " + slot); }
        }

        private async Task<bool> SelectSessionAsync(string sessionId, string directory = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!ChatSelection.ValidSession(sessionId)) return false;
            var serializer = new JavaScriptSerializer();
            await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat.selectSession(" + serializer.Serialize(sessionId) + ","
                + serializer.Serialize(directory ?? _projectWorkspace ?? _chatWorkspace()) + ")");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_disposed && DateTime.UtcNow < deadline)
            {
                var selection = await ReadChatSelectionAsync();
                if (selection?.IsSelected == true && selection.sessionId == sessionId) { _chatSelection = selection; return true; }
                await Task.Delay(150);
            }
            return false;
        }

        private async Task NewChatAsync(bool general)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending || !_chatAdapterReady) return;
            var identity = general ? null : ReadProjectIdentity();
            if (identity != null && !await PrepareProjectWorkspaceAsync(identity)) return;
            _chatActionPending = true; RenderChatBinding();
            try
            {
                var options = new { title = (string)null,
                    directory = identity == null ? _chatWorkspace() : (_projectWorkspace ?? _chatWorkspace()) };
                var serializer = new JavaScriptSerializer();
                // ExecuteScriptAsync does not await a JS promise. Poll a narrow,
                // metadata-only completion slot instead; never replay creation.
                await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatNewChat={phase:'pending'};window.__twinCatChat.newChat("
                    + serializer.Serialize(options) + ").then(()=>window.__twinCatNewChat={phase:'ready'},()=>window.__twinCatNewChat={phase:'failed'});");
                var deadline = DateTime.UtcNow.AddSeconds(30);
                bool created = false;
                while (!_disposed && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(150);
                    var phase = await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatNewChat?.phase");
                    if (phase == "\"ready\"") { created = true; break; }
                    if (phase == "\"failed\"") throw new InvalidOperationException(HostText.Get("H136"));
                }
                if (_disposed) return;
                if (!created) throw new InvalidOperationException(HostText.Get("H137"));
                if (identity == null)
                {
                    var selection = await ReadChatSelectionAsync();
                    if (selection?.IsSelected != true || !SameDirectory(selection.directory, _chatWorkspace())) throw new InvalidOperationException(HostText.Get("H138"));
                    var binding = ChatContextBinding.CreateGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id, _origin.AbsoluteUri, selection.sessionId);
                    _chatBindings.Bind(binding); _chatSelection = selection; _chatBinding = binding;
                }
                else
                {
                    var current = FreshProjectIdentity();
                    if (!identity.Equals(current)) throw new InvalidOperationException(HostText.Get("H139"));
                    await BindSelectionAsync(current);
                }
                _explicitGeneralRequested = identity == null;
                _noticeText = null;
            }
            catch (Exception error) { if (!_disposed) Notify(error is InvalidOperationException ? error.Message : HostText.Get("H140"), "error"); }
            finally { _chatActionPending = false; }
            if (!_disposed) await PollChatSelectionAsync();
        }

        private async Task BindSelectionAsync(NativeProjectIdentity project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var selected = await ReadChatSelectionAsync();
            if (selected?.IsSelected != true) throw new InvalidOperationException(HostText.Get("H141"));
            var fresh = FreshProjectIdentity();
            if (!fresh.Equals(project) || !SameDirectory(selected.directory, _projectWorkspace ?? _chatWorkspace())) throw new InvalidOperationException(HostText.Get("H142"));
            var binding = ChatContextBinding.CreateProject(fresh, _chatIdeInstance, _origin.AbsoluteUri, selected.sessionId);
            _chatBindings.Bind(binding);
            _chatBindings.RememberProjectSelection(fresh, _origin.AbsoluteUri, selected.sessionId);
            _chatSelection = selected; _chatBinding = binding;
        }

        private async Task BindCurrentChatAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending) return;
            _chatActionPending = true; RenderChatBinding();
            try
            {
                var project = ReadProjectIdentity();
                if (project != null) await BindSelectionAsync(project);
                else await BindGeneralCoreAsync();
            }
            catch (InvalidOperationException) { /* Banner explains foreign or busy chats. */ }
            catch { Notify(HostText.Get("H143"), "error"); }
            finally { _chatActionPending = false; }
            await PollChatSelectionAsync();
        }

        private async Task BindGeneralAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending) return;
            _chatActionPending = true; RenderChatBinding();
            try { await BindGeneralCoreAsync(); }
            catch { /* A chat bound to a project is never made general. */ }
            finally { _chatActionPending = false; }
            await PollChatSelectionAsync();
        }

        private async Task BindGeneralCoreAsync()
        {
            var selection = await ReadChatSelectionAsync();
            if (selection?.IsSelected != true) throw new InvalidOperationException(HostText.Get("H141"));
            if (!SameDirectory(selection.directory, _chatWorkspace())) throw new InvalidOperationException(HostText.Get("H144"));
            var binding = ChatContextBinding.CreateGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id, _origin.AbsoluteUri, selection.sessionId);
            _chatBindings.Bind(binding); _chatSelection = selection; _chatBinding = binding;
        }

        // Attach click: a general or unbound conversation becomes this
        // project's chat first, then the current snapshot is staged.
        private async Task AttachClickAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending || !_chatAdapterReady || _chatActive?.Identity == null) return;
            if (!HasCurrentChatBinding())
            {
                if (_chatSelection?.IsSelected != true || (_mcpEnabled && !SameDirectory(_chatSelection.directory, _projectWorkspace))) { await NewChatAsync(false); }
                else if (_chatBinding == null || _chatBinding.IsGeneral || _chatBinding.SameProject(_chatActive.Identity))
                {
                    _chatActionPending = true; RenderChatBinding();
                    try { await BindSelectionAsync(FreshProjectIdentity()); }
                    catch (Exception error) { Notify(error is InvalidOperationException ? error.Message : HostText.Get("H143"), "error"); return; }
                    finally { _chatActionPending = false; }
                    await PollChatSelectionAsync();
                }
            }
            if (HasCurrentChatBinding()) await AttachActivePouAsync();
        }

        private async Task<bool> AttachActivePouAsync(bool auto = false)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending || !HasCurrentChatBinding()) return false;
            _chatActionPending = true; _autoAttaching = auto;
            var attached = false;
            string requestedRevision = null, requestedSession = null, requestedDocument = null;
            if (!auto) RenderChatBinding();
            try
            {
                var snapshot = FreshChatSnapshot();
                var revision = ChatContextPolicy.Revision(snapshot);
                var documentGeneration = _chatDocumentGeneration;
                var selection = await ReadChatSelectionAsync();
                requestedRevision = revision; requestedSession = selection?.sessionId; requestedDocument = selection?.documentId;
                if (selection?.presentationReady != true) throw new InvalidOperationException(HostText.Get("H145"));
                if (auto && selection?.manualAttachmentPending == true)
                {
                    throw new InvalidOperationException(HostText.Get("H146"));
                }
                if (selection?.IsSelected != true || !SameDirectory(selection.directory, _projectWorkspace)
                    || !_chatBinding.Matches(snapshot.Identity, _chatIdeInstance, _origin.AbsoluteUri, selection.sessionId))
                    throw new InvalidOperationException(HostText.Get("H147"));
                // The native click (or the operator's auto-attach setting) is the
                // disclosure action. Revalidate after the asynchronous browser read.
                if (ChatContextPolicy.Revision(FreshChatSnapshot()) != revision)
                    throw new InvalidOperationException(HostText.Get("H148"));
                if (_disposed || documentGeneration != _chatDocumentGeneration)
                    throw new InvalidOperationException(HostText.Get("H149"));
                var attachment = ChatContextPolicy.CreateAttachment(snapshot);
                var serializer = new JavaScriptSerializer();
                var request = new { sessionId = selection.sessionId, selection.epoch, selection.documentId, contextKey = ProjectKey(snapshot.Identity.ProjectIdentity), auto,
                    item = new { providerId = "twincat-assistant", id = auto ? ChatContextPolicy.ActiveReferenceId : ChatContextPolicy.ReferenceId(snapshot),
                        title = snapshot.ObjectName + " / " + System.IO.Path.GetFileNameWithoutExtension(snapshot.Identity.ProjectFile)
                            + (attachment.IsSavedReference ? HostText.Get("H150") : ""),
                        url = _origin.AbsoluteUri, text = attachment.Text,
                        data = new { revision, reference = ChatContextPolicy.ReferenceId(snapshot), mode = auto ? "following" : "manual",
                            objectKind = snapshot.ObjectKind, owner = snapshot.OwnerName, member = snapshot.MemberPath,
                            contentKind = attachment.IsSavedReference ? "saved-object-reference" : "snapshot",
                            sourceKind = attachment.IsSavedReference ? "saved-project-reference" : snapshot.SourceKind, dirty = snapshot.Dirty?.State } } };
                // Keep project permission and the active-object metadata intact.
                // A source revision is attachment data, not a new history scope.
                var permission = new { request.sessionId, request.epoch, request.documentId, request.contextKey, context = ChatContextMetadata(snapshot, snapshot.Identity.ProjectIdentity) };
                var outcome = await _browser.CoreWebView2.ExecuteScriptAsync("(()=>{try{if(!window.__twinCatChat.permit(" + serializer.Serialize(permission)
                    + "))return 'binding_changed';window.__twinCatChat.attach(" + serializer.Serialize(request)
                    + ");return 'ready';}catch(error){return ({'Chat binding changed':'binding_changed','Another attachment is still pending':'pending_other','A manual attachment is still pending':'pending_manual','Invalid POU attachment':'invalid_attachment'})[error?.message]??'unavailable';}})()");
                var outcomeCode = outcome != null && outcome.Length < 80 ? serializer.Deserialize<string>(outcome) : null;
                if (outcomeCode != "ready") throw new InvalidOperationException(AttachmentFailureMessage(outcomeCode));
                attached = true;
                _automaticAttachmentFailure = null;
                _noticeText = null;
            }
            // A quick POU switch can outdate an automatic attempt. The caller
            // owns its retry budget and reports exhaustion; clicks fail visibly.
            catch (Exception error) {
                _automaticAttachmentFailure = error is InvalidOperationException ? error.Message : HostText.Get("H151");
                if (!_disposed && !auto) Notify(_automaticAttachmentFailure, "error");
            }
            finally { _chatActionPending = false; }
            if (!_disposed) await PollChatSelectionAsync();
            _autoAttaching = false;
            if (!_disposed && auto) RenderChatBinding();
            var confirmed = attached && (!auto || (_chatSelection?.sessionId == requestedSession && _chatSelection.documentId == requestedDocument
                && _chatSelection.attachments?.Any(item => item?.id == ChatContextPolicy.ActiveReferenceId && item.revision == requestedRevision) == true));
            if (attached && !confirmed) _automaticAttachmentFailure = HostText.Get("H152");
            return confirmed;
        }
        private static string AttachmentFailureMessage(string code)
        {
            switch (code)
            {
                case "binding_changed": return HostText.Get("H153");
                case "pending_other": return HostText.Get("H154");
                case "pending_manual": return HostText.Get("H155");
                case "invalid_attachment": return HostText.Get("H156");
                default: return HostText.Get("H157");
            }
        }

        // Enables exactly one connected provider for the Assistant's read-only context.
        // Runs automatically once a provider is connected in OpenChamber; the
        // menu offers it for switching between several connected providers.
        private async Task AllowProviderAsync(string provider)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending || !CanPromptCurrentChat()) return;
            _chatActionPending = true; RenderChatBinding();
            try
            {
                var selection = await ReadChatSelectionAsync();
                if (selection?.IsSelected != true || string.IsNullOrEmpty(provider) || selection.providers?.Any(choice => choice.id == provider) != true
                    || !SameDirectory(selection.directory, _chatBinding.IsGeneral ? _chatWorkspace() : _projectWorkspace)
                    || (!(_chatBinding.IsGeneral && _chatBinding.MatchesGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id, _origin.AbsoluteUri, selection.sessionId))
                        && !_chatBinding.MatchesProject(FreshProjectIdentity(), _chatIdeInstance, _origin.AbsoluteUri, selection.sessionId)))
                    throw new InvalidOperationException(HostText.Get("H158"));
                var model = selection.model?.StartsWith(provider + "/", StringComparison.Ordinal) == true ? selection.model : null;
                _modelStatus.Text = HostText.Format("H159", new object[] { provider });
                await NativeModelConsent.ConfigureAsync(await GetChatConfigurationAsync(), provider, model);
                var serializer = new JavaScriptSerializer();
                var expected = new { selection.sessionId, selection.epoch, selection.documentId };
                await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatPolicy='pending';window.__twinCatChat.refreshPolicy(" + serializer.Serialize(expected)
                    + "," + serializer.Serialize(provider) + "," + serializer.Serialize(model) + ").then(()=>window.__twinCatPolicy='ready',()=>window.__twinCatPolicy='failed');");
                var deadline = DateTime.UtcNow.AddSeconds(20);
                bool confirmed = false;
                while (!_disposed && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(150);
                    var phase = await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatPolicy");
                    if (phase == "\"ready\"") { confirmed = true; break; }
                    if (phase == "\"failed\"") break;
                }
                if (_disposed) return;
                if (!confirmed) throw new InvalidOperationException("Model policy readback failed.");
                _modelStatus.Text = HostText.Format("H161", new object[] { provider, (model == null ? HostText.Get("H162") : HostText.Format("H163", new object[] { model })) });
            }
            catch
            {
                if (!_disposed)
                {
                    _modelStatus.Text = HostText.Get("H164");
                    var retry = provider;
                    Notify(HostText.Format("H165", new object[] { provider }), "error", HostText.Get("H046"),
                        () => { _consentAttempted.Remove(retry); DismissNotice(); RunAction(() => AllowProviderAsync(retry)); });
                }
            }
            finally { _chatActionPending = false; }
            if (!_disposed) await PollChatSelectionStateAsync();
        }
    }
}
