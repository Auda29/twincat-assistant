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
        private ChatBindingStore _chatBindings = new ChatBindingStore();
        private ActiveEditorSnapshot _chatActive;
        private NativeProjectIdentity _chatProject, _preparedProject;
        private Func<NativeProjectIdentity> _chatProjectReader;
        private string _projectWorkspace, _preparedMcpSettings, _mcpFailureKey;
        private string _knowledgeCorpus = "not_probed";
        private bool _mcpEnabled, _explicitGeneralRequested;
        private Func<ActiveEditorSnapshot> _chatSnapshotReader;
        private Func<string> _chatWorkspace = () => System.IO.Path.Combine(NativeChatConfiguration.Load().StateRoot, "workspace");
        private Func<NativeChatConfiguration> _chatConfiguration = NativeChatConfiguration.Load;
        private string _chatBrowserProfile = null;
        private ChatSelection _chatSelection;
        private ChatContextBinding _chatBinding;
        private bool _chatActionPending, _chatAdapterReady, _chatPolling, _autoConnectAttempted, _automating;
        private readonly TextBlock _modelStatus = new TextBlock { Text = "Modellanbieter: noch keiner verbunden. Zugangsdaten werden nur in OpenChambers Anbieter-Einstellungen eingegeben.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        private readonly TextBlock _mcpStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        private DispatcherTimer _chatReconnect, _automationKick, _noticeTimer;
        private int _chatReconnectAttempts, _signInAttempts;
        private long _chatDocumentGeneration;
        private string _autoProjectKey, _autoAttachedKey;
        private bool _autoAttaching;
        private readonly HashSet<string> _autoBindAttempted = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _consentAttempted = new HashSet<string>(StringComparer.Ordinal);
        private string _noticeText, _noticeTone, _noticeActionLabel;
        private Action _noticeAction;

        private void InitializeChatIntegration()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Loaded += OnChatLoaded;
            _chatReconnect = new DispatcherTimer(DispatcherPriority.Background);
            _chatReconnect.Tick += ReconnectChat;
            _automationKick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
            _automationKick.Tick += OnAutomationKick;
            _noticeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(8) };
            _noticeTimer.Tick += (sender, args) => { _noticeTimer.Stop(); if (_noticeTone != "error") DismissNotice(); };
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
                    + "\nwindow.__twinCatInstall='loading';TwinCatEmbedded.installEmbeddedChat().then(()=>window.__twinCatInstall='ready',()=>window.__twinCatInstall='failed');");
                if (_disposed || generation != _chatConnection.Generation) return;
                await PollChatSelectionAsync();
            }
            catch { if (!_disposed && generation == _chatConnection.Generation) Notify("Chat-Integration nicht verfügbar. Version und Verbindung unter Diagnose prüfen.", "error"); }
        }

        private async Task<ChatSelection> ReadChatSelectionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || _browser.CoreWebView2 == null || !Allowed(_browser.CoreWebView2.Source)) return null;
            var serializer = new JavaScriptSerializer();
            var raw = await _browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.__twinCatChat?.state() ?? {version:1,ready:false})");
            if (raw == null || raw.Length > 12000) return null;
            var json = serializer.Deserialize<string>(raw);
            return serializer.Deserialize<ChatSelection>(json);
        }

        private async Task PollChatSelectionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatPolling || _disposed || _browser.CoreWebView2 == null) return;
            _chatPolling = true;
            var generation = _chatConnection.Generation;
            try
            {
                var selection = await ReadChatSelectionAsync();
                if (_disposed || generation != _chatConnection.Generation) return;
                _chatSelection = selection; _chatAdapterReady = selection?.ready == true;
                if (selection?.theme != null) ApplyTheme(selection.theme);
                try { _chatBinding = selection?.IsSelected == true ? _chatBindings.Read(_origin.AbsoluteUri, selection.sessionId) : null; }
                catch { _chatBinding = null; }
                await PermitCurrentChatAsync();
                if (!_disposed && generation == _chatConnection.Generation) RenderChatBinding();
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
            var permission = CanPromptCurrentChat() ? new { sessionId = selection.sessionId, selection.epoch, selection.documentId,
                contextKey = _chatBinding.IsGeneral ? "general" : ProjectKey(_chatProject) } : null;
            await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat?.permit(" + new JavaScriptSerializer().Serialize(permission) + ")");
        }

        // One pass of automatic steps. Each step is attempted at most once per
        // context so a failure is reported instead of retried in a loop.
        private async Task RunAutomationAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_automating || _disposed || _holdingView || _chatActionPending || !_chatAdapterReady || _chatSelection == null || _origin == null) return;
            _automating = true;
            try
            {
                await RefreshKnowledgeAsync();
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
                        // Opening or switching to a PLC project selects its newest
                        // conversation. A deliberately used general chat stays open.
                        _autoProjectKey = key;
                        if (!HasCurrentChatBinding() && (!generalChat || (_mcpEnabled && !_explicitGeneralRequested))) { await OpenProjectChatAsync(); return; }
                    }
                    else if (selected && !HasCurrentChatBinding() && !generalChat
                        && (_chatBinding == null || _chatBinding.IsGeneral || _chatBinding.SameProjectContext(identity))
                        && _autoBindAttempted.Add(_chatSelection.sessionId + "\0" + key))
                    {
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
                if (_chatPreferences.AutoAttach && HasCurrentChatBinding() && _chatActive != null)
                {
                    var revision = ChatContextPolicy.Revision(_chatActive);
                    var attempt = _chatSelection.sessionId + "\0" + revision;
                    if (ActiveChip()?.revision != revision && attempt != _autoAttachedKey)
                    {
                        // A chip the operator removed stays removed until the
                        // POU changes or another POU becomes active.
                        _autoAttachedKey = attempt;
                        await AttachActivePouAsync(true);
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

        private void RenderChatBinding()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_attachPouButton == null || _disposed || _holdingView) return;
            RenderKnowledge();
            // Automatic attachment runs quietly; only operator actions disable the bar.
            var ready = (!_chatActionPending || _autoAttaching) && _chatAdapterReady && _chatSelection?.authenticated == true;
            var identity = _chatProject;
            var foreign = _chatSelection?.IsSelected == true && _chatBinding != null && !_chatBinding.IsGeneral
                && identity != null && !_chatBinding.SameProjectContext(identity);
            _historyButton.IsEnabled = ready;
            _newChatButton.IsEnabled = ready;
            _newChatButton.ToolTip = identity == null ? "Neuer allgemeiner Chat" : "Neuer Chat für " + ProjectName(identity.ProjectFile);
            // Attach button: the one explicit disclosure action.
            var revision = ChatContextPolicy.Revision(_chatActive);
            var chip = HasCurrentChatBinding() ? ActiveChip() : null;
            // With automatic attachment the new POU shows as attached right away
            // while it is staged; a failed or removed chip falls back to Anhängen.
            var autoPending = _chatPreferences.AutoAttach && revision != null && chip?.revision != revision && HasCurrentChatBinding()
                && (_autoAttaching || _autoAttachedKey != _chatSelection.sessionId + "\0" + revision);
            var attachedCurrent = revision != null && (chip?.revision == revision || autoPending);
            var stale = !attachedCurrent && chip != null;
            _attachPouButton.IsEnabled = ready && _chatActive?.Identity != null && !foreign && !attachedCurrent && _chatSelection?.IsSelected == true;
            _attachLabel.Text = attachedCurrent ? "Angehängt" : stale ? "Aktualisieren" : "Anhängen";
            _attachIcon.Data = AssistantTheme.Icon(attachedCurrent ? "check" : stale ? "refresh" : "attach");
            _attachPouButton.ToolTip = _chatActive?.Identity == null ? _attachLabel.Text + ": Öffne einen ST-Baustein, um ihn anzuhängen."
                : attachedCurrent ? "Angehängt: Dieser Stand von " + _chatActive.ObjectName + " ist bereits im Chat."
                : _attachLabel.Text + ": Gibt den aktuellen Stand von " + _chatActive.ObjectName + " für die nächste Nachricht frei. Erst Absenden überträgt ihn an das Modell. Das PLC-Projekt wird nicht verändert.";
            // Status chip.
            string status, tone;
            // The chip describes the POU context; connection problems use the banner.
            if (identity == null || _chatBinding?.IsGeneral == true) { status = "Allgemeiner Chat"; tone = "Neutral"; }
            else if (foreign) { status = "Chat aus " + ProjectName(_chatBinding.ProjectFile); tone = "Error"; }
            else if (_chatActive == null) { status = "Projekt erkannt"; tone = "Neutral"; }
            else if (stale) { status = "Seit Anhang geändert"; tone = "Warning"; }
            else if (_chatActive.Dirty?.State == "known-dirty") { status = "Ungespeichert"; tone = "Warning"; }
            else if (_chatActive.Dirty?.State == "known-clean") { status = "Gespeichert"; tone = "Success"; }
            else { status = "Stand unbekannt"; tone = "Neutral"; }
            if (_statusText.Text != status) _statusText.Text = status;
            _statusChip.Visibility = identity == null && _chatBinding?.IsGeneral != true ? Visibility.Collapsed : Visibility.Visible;
            _statusChip.SetResourceReference(Border.BackgroundProperty, "Tc." + tone + "Surface");
            _statusDot.SetResourceReference(Shape.FillProperty, "Tc." + tone);
            _statusText.SetResourceReference(TextBlock.ForegroundProperty, tone == "Neutral" ? "Tc.Muted" : "Tc." + tone);
            _statusChip.ToolTip = _chatActive?.SourceNotice == null ? status : status + Environment.NewLine + _chatActive.SourceNotice;
            // Model provider status lives in Diagnose.
            var allowed = _chatSelection?.allowedProviders ?? new string[0];
            var connected = _chatSelection?.providers ?? new ChatProviderChoice[0];
            var servers = _chatSelection?.mcp ?? new ChatMcpStatus[0];
            _mcpStatus.Text = "MCP: " + (servers.Length == 0 ? "noch nicht eingerichtet oder noch nicht verbunden."
                : string.Join("; ", servers.Select(server => (server.name == "knowledge" ? "Knowledge" : "TwinCAT") + ": "
                    + (server.status == "connected" ? "verbunden" : server.status == "pending" ? "verbindet" : "nicht verfügbar"))))
                + (_mcpEnabled ? " Projektwerkzeuge lesen gespeicherte Dateien. "
                    + (_knowledgeCorpus == "not_configured" ? "Knowledge-Korpus ist noch nicht eingerichtet." : "Knowledge-Korpus separat prüfen.")
                    : servers.Length == 0 ? " Lokale MCP-Konfiguration fehlt." : " Knowledge-Korpus separat prüfen.");
            if (!_chatActionPending)
                _modelStatus.Text = connected.Length == 0 ? "Modellanbieter: noch keiner verbunden. Zugangsdaten werden nur in OpenChambers Anbieter-Einstellungen eingegeben."
                    : "Modellanbieter: " + string.Join(", ", connected.Select(choice => choice.name + (allowed.Contains(choice.id) ? " (freigegeben)" : "")))
                        + (string.IsNullOrEmpty(_chatSelection?.model) ? ". Modell im Chat wählen." : ". Modell: " + _chatSelection.model + ".")
                        + " PLC-Schreib- und Shell-Werkzeuge bleiben gesperrt.";
            RenderBanner(ready, identity, foreign);
            RenderDiagnostics();
        }

        private void RenderBanner(bool ready, NativeProjectIdentity identity, bool foreign)
        {
            string text = null, tone = "Neutral", action = null;
            Action command = null;
            var dismissable = false;
            if (_setupMissing) { text = "Der lokale Chat ist noch nicht eingerichtet. Configure-NativeChat.ps1 ausführen."; tone = "Error"; action = "Diagnose"; command = ShowDiagnostics; }
            else if (_chatConnection.Phase == "failed")
            {
                text = _chatReconnect?.IsEnabled == true ? "Verbindung zum lokalen Chat unterbrochen. Neuer Versuch läuft …" : "Verbindung zum lokalen Chat unterbrochen.";
                tone = "Error"; action = "Neu verbinden"; command = Reconnect;
            }
            else if (_noticeText != null) { text = _noticeText; tone = _noticeTone == "error" ? "Error" : _noticeTone == "warning" ? "Warning" : "Neutral"; action = _noticeActionLabel; command = _noticeAction; dismissable = true; }
            else if (foreign)
            {
                text = "Diese Unterhaltung gehört zu " + ProjectName(_chatBinding.ProjectFile) + " und ist hier nur lesbar.";
                tone = "Error"; action = "Neuer Chat für " + ProjectName(identity.ProjectFile); command = () => RunAction(() => NewChatAsync(false));
            }
            else if (ready && identity == null && _chatSelection?.IsSelected == true && _chatBinding != null && !_chatBinding.IsGeneral)
            {
                text = "Dieser Projektchat gehört zu " + ProjectName(_chatBinding.ProjectFile) + ". Öffne einen Baustein daraus, um weiterzuschreiben.";
                action = "Allgemeiner Chat"; command = () => RunAction(() => NewChatAsync(true));
            }
            else if (ready && _chatSelection?.IsSelected == true && identity != null && !HasCurrentChatBinding() && _chatBinding != null
                && !_chatBinding.IsGeneral && _chatBinding.SameProjectContext(identity))
            {
                text = _mcpEnabled && !SameDirectory(_chatSelection.directory, _projectWorkspace)
                    ? "Diese Unterhaltung verwendet noch die frühere Projektanbindung. Starte einen neuen Projektchat."
                    : "Diese Unterhaltung ist in einer anderen TwinCAT-Instanz geöffnet.";
                tone = "Warning"; action = "Neuer Projektchat"; command = () => RunAction(() => NewChatAsync(false));
            }
            else if (ready && (_chatSelection.providers?.Length ?? 0) == 0)
            {
                text = "Noch kein Modellanbieter verbunden. Zugangsdaten gibst du nur in OpenChamber ein.";
                action = "Anbieter verbinden"; command = () => RunAction(OpenProviderSettingsAsync);
            }
            else if (ready && _knowledgeBusy)
            {
                text = "Die Beckhoff-Hilfe wird für die Wissenssuche vorbereitet. Du kannst weiterchatten.";
                action = "Fortschritt"; command = ShowKnowledgeSetup;
            }
            else if (ready && _knowledgeState?.phase == "update")
            {
                text = "Eine neuere Beckhoff-Hilfe ist installiert. Der bisherige Suchindex bleibt verfügbar.";
                action = "Aktualisieren"; command = ShowKnowledgeSetup;
            }
            else if (ready && (_knowledgeState?.phase == "available" || _knowledgeState?.phase == "missing" || _knowledgeState?.phase == "error"))
            {
                text = _knowledgeState.phase == "available" ? "Installierte Beckhoff-Hilfe für die Wissenssuche verwenden."
                    : _knowledgeState.phase == "missing" ? "Für die Wissenssuche wurde noch keine installierte Beckhoff-Hilfe gefunden."
                    : "Die Wissenssuche konnte noch nicht eingerichtet werden.";
                action = "Einrichten"; command = ShowKnowledgeSetup;
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
            _previewPopup.IsOpen = false;
            if (_menuPopup.IsOpen || JustClosed(_menuClosedAt)) { _menuPopup.IsOpen = false; return; }
            _menuItems.Children.Clear();
            var ready = _chatAdapterReady && _chatSelection?.authenticated == true && !_chatActionPending;
            // Chat: actions and the one disclosure preference.
            MenuRow("chat", "Neuer allgemeiner Chat", "Ohne Projekt- und Bausteinkontext", ready, () => NewChatAsync(true));
            MenuRow("attach", "Baustein automatisch anhängen", "Bei jeder Änderung neu vormerken", true, ToggleAutoAttachAsync,
                MenuSwitch(_chatPreferences.AutoAttach));
            // Model providers: a single choice among the connected ones.
            MenuSeparator();
            MenuHeading("Modellanbieter");
            var providers = _chatSelection?.providers ?? new ChatProviderChoice[0];
            if (providers.Length > 1)
                foreach (var provider in providers)
                {
                    var id = provider.id;
                    MenuRow(null, provider.name, null, ready && CanPromptCurrentChat(), () => AllowProviderAsync(id),
                        leading: MenuRadio(_chatSelection.allowedProviders?.Contains(id) == true));
                }
            MenuRow("sliders", "Anbieter und Modelle verwalten …", null, ready, OpenProviderSettingsAsync);
            // Knowledge: state on the right, setup behind the row.
            MenuSeparator();
            string knowledgeDetail = "Noch nicht eingerichtet", knowledgeStatus = "Einrichten", knowledgeTone = "Warning";
            var language = _knowledgeState?.language == "de" ? "Deutsch" : "Englisch";
            if (_knowledgeBusy) { knowledgeDetail = "Beckhoff-Hilfe wird vorbereitet"; knowledgeStatus = "Läuft"; knowledgeTone = "Neutral"; }
            else if (_knowledgeState?.ready == true)
            {
                knowledgeDetail = "Beckhoff-Hilfe · " + language + (_knowledgeState.phase == "update" ? " · Update verfügbar" : "");
                // The live MCP state wins over the last confirmed reload.
                if (McpFailed(McpState("knowledge"))) { knowledgeStatus = "Getrennt"; knowledgeTone = "Error"; }
                else if (KnowledgeConnected()) { knowledgeStatus = "Bereit"; knowledgeTone = "Success"; }
                else { knowledgeStatus = "Verbindet"; knowledgeTone = "Neutral"; }
            }
            MenuRow("book", "Wissenssuche", knowledgeDetail, true, () => { ShowKnowledgeSetup(); return Task.CompletedTask; },
                MenuStatus(knowledgeStatus, knowledgeTone));
            // Connection maintenance.
            MenuSeparator();
            MenuRow("refresh", "Neu verbinden", null, !_connecting, () => { Reconnect(); return Task.CompletedTask; });
            MenuRow("pulse", "Diagnose anzeigen", null, true, () => {
                _diagnostics.Visibility = _diagnostics.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                return Task.CompletedTask;
            }, MenuSwitch(_diagnostics.Visibility == Visibility.Visible));
            _menuPopup.IsOpen = true;
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
            try { _chatPreferences.Save(); } catch { Notify("Einstellung konnte nicht gespeichert werden; sie gilt bis zum Schließen der IDE.", "warning"); }
            _autoAttachedKey = null;
            if (_chatPreferences.AutoAttach) Notify("Der aktive Baustein wird jetzt automatisch angehängt, sobald er sich ändert. Erst Absenden überträgt ihn.");
            else Notify("Automatisches Anhängen ist aus. Bausteine nur noch über Anhängen freigeben.");
            await PollChatSelectionAsync();
        }

        private async Task OpenHistoryAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatAdapterReady && !_disposed) await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat.toggleChats()");
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
            if (result?.Identity == null) throw new InvalidOperationException("Der aktive ST-Baustein ist nicht eindeutig verfügbar.");
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
            if (project == null) throw new InvalidOperationException("Das PLC-Projekt ist nicht eindeutig verfügbar.");
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
        private async Task<bool> PrepareProjectWorkspaceAsync(NativeProjectIdentity project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var config = _chatConfiguration();
            _mcpEnabled = !string.IsNullOrEmpty(config.McpSettingsPath);
            if (_preparedProject?.Equals(project) == true && _preparedMcpSettings == config.McpSettingsPath) return true;
            var key = ProjectKey(project) + "\0" + config.McpSettingsPath;
            if (_mcpFailureKey == key) return false;
            _preparedProject = null; _projectWorkspace = null;
            _chatActionPending = true; RenderChatBinding();
            try
            {
                var prepared = _mcpEnabled ? await NativeMcpProject.PrepareAsync(config, project) : null;
                var directory = prepared == null ? _chatWorkspace() : prepared.workspace;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed || !project.Equals(ReadProjectIdentity())) return false;
                _projectWorkspace = directory; _preparedProject = project; _preparedMcpSettings = config.McpSettingsPath;
                _knowledgeCorpus = prepared?.corpus ?? "not_probed";
                return true;
            }
            catch
            {
                if (!_disposed)
                {
                    _mcpFailureKey = key;
                    Notify("Die Projektwerkzeuge konnten nicht eingerichtet werden. Diagnose und lokale MCP-Konfiguration prüfen.", "error", "Erneut versuchen",
                        () => { _mcpFailureKey = null; DismissNotice(); KickAutomation(); });
                }
                return false;
            }
            finally { _chatActionPending = false; if (!_disposed) RenderChatBinding(); }
        }

        private Task NewProjectChatAsync() { return NewChatAsync(false); }

        // Reopen the newest conversation this project already has; only create
        // a new one when none exists or it is held by another live IDE.
        private async Task OpenProjectChatAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var project = ReadProjectIdentity();
            if (project == null || _chatActionPending || !await PrepareProjectWorkspaceAsync(project)) return;
            HashSet<string> known;
            try { known = _chatBindings.SessionsForProject(_origin.AbsoluteUri, project); }
            catch { known = new HashSet<string>(); }
            if (known.Count > 0)
            {
                _chatActionPending = true; RenderChatBinding();
                try
                {
                    // Upstream may still be loading its session list right after startup.
                    for (var attempt = 0; attempt < 4 && !_disposed; attempt++)
                    {
                        var latest = (await ReadRecentSessionsAsync()).FirstOrDefault(entry => known.Contains(entry.id));
                        if (latest != null)
                        {
                            if (await SelectSessionAsync(latest.id))
                            {
                                try { await BindSelectionAsync(FreshProjectIdentity()); return; }
                                catch (InvalidOperationException) { break; }
                            }
                            break;
                        }
                        await Task.Delay(500);
                    }
                }
                finally { _chatActionPending = false; if (!_disposed) await PollChatSelectionStateAsync(); }
            }
            if (!_disposed) await NewChatAsync(false);
        }

        private async Task PollChatSelectionStateAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var selection = await ReadChatSelectionAsync();
            if (_disposed || selection == null) return;
            _chatSelection = selection;
            try { _chatBinding = selection.IsSelected ? _chatBindings.Read(_origin.AbsoluteUri, selection.sessionId) : null; } catch { _chatBinding = null; }
            await PermitCurrentChatAsync();
            RenderChatBinding();
        }

        private async Task<ChatSessionEntry[]> ReadRecentSessionsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var serializer = new JavaScriptSerializer();
            var raw = await _browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.__twinCatChat.recentSessions("
                + serializer.Serialize(_projectWorkspace ?? _chatWorkspace()) + "))");
            if (raw == null || raw.Length > 40000) return new ChatSessionEntry[0];
            return serializer.Deserialize<ChatSessionEntry[]>(serializer.Deserialize<string>(raw)) ?? new ChatSessionEntry[0];
        }

        private async Task<bool> SelectSessionAsync(string sessionId)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!ChatSelection.ValidSession(sessionId)) return false;
            await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat.selectSession(" + new JavaScriptSerializer().Serialize(sessionId) + ")");
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
                var options = new { title = identity == null ? "Allgemeiner TwinCAT-Chat" : System.IO.Path.GetFileNameWithoutExtension(identity.ProjectFile),
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
                    if (phase == "\"failed\"") throw new InvalidOperationException("Projektchat konnte nicht angelegt werden.");
                }
                if (_disposed) return;
                if (!created) throw new InvalidOperationException("Chat-Erstellung nicht bestätigt. Keine automatische Wiederholung; Verlauf prüfen.");
                if (identity == null)
                {
                    var selection = await ReadChatSelectionAsync();
                    if (selection?.IsSelected != true || !SameDirectory(selection.directory, _chatWorkspace())) throw new InvalidOperationException("Der neue allgemeine Chat ist nicht verfügbar.");
                    var binding = ChatContextBinding.CreateGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id, _origin.AbsoluteUri, selection.sessionId);
                    _chatBindings.Bind(binding); _chatSelection = selection; _chatBinding = binding;
                }
                else
                {
                    var current = FreshProjectIdentity();
                    if (!identity.Equals(current)) throw new InvalidOperationException("Projekt gewechselt. Der neue Chat wurde nicht zugeordnet.");
                    await BindSelectionAsync(current);
                }
                _explicitGeneralRequested = identity == null;
                _noticeText = null;
            }
            catch (Exception error) { if (!_disposed) Notify(error is InvalidOperationException ? error.Message : "Chat konnte nicht angelegt werden. Verbindung prüfen.", "error"); }
            finally { _chatActionPending = false; }
            if (!_disposed) await PollChatSelectionAsync();
        }

        private async Task BindSelectionAsync(NativeProjectIdentity project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var selected = await ReadChatSelectionAsync();
            if (selected?.IsSelected != true) throw new InvalidOperationException("Zuerst eine Unterhaltung öffnen.");
            var fresh = FreshProjectIdentity();
            if (!fresh.Equals(project) || !SameDirectory(selected.directory, _projectWorkspace ?? _chatWorkspace())) throw new InvalidOperationException("Projekt oder Arbeitsbereich gewechselt. Chat nicht zugeordnet.");
            var binding = ChatContextBinding.CreateProject(fresh, _chatIdeInstance, _origin.AbsoluteUri, selected.sessionId);
            _chatBindings.Bind(binding);
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
            catch { Notify("Chat-Zuordnung nicht verfügbar.", "error"); }
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
            if (selection?.IsSelected != true) throw new InvalidOperationException("Zuerst eine Unterhaltung öffnen.");
            if (!SameDirectory(selection.directory, _chatWorkspace())) throw new InvalidOperationException("Ein Projekt-Arbeitsbereich kann nicht als allgemeiner Chat freigegeben werden.");
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
                    catch (Exception error) { Notify(error is InvalidOperationException ? error.Message : "Chat-Zuordnung nicht verfügbar.", "error"); return; }
                    finally { _chatActionPending = false; }
                    await PollChatSelectionAsync();
                }
            }
            if (HasCurrentChatBinding()) await AttachActivePouAsync();
        }

        private async Task AttachActivePouAsync(bool auto = false)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending || !HasCurrentChatBinding()) return;
            _chatActionPending = true; _autoAttaching = auto;
            if (!auto) RenderChatBinding();
            try
            {
                var snapshot = FreshChatSnapshot();
                var revision = ChatContextPolicy.Revision(snapshot);
                var documentGeneration = _chatDocumentGeneration;
                var selection = await ReadChatSelectionAsync();
                if (selection?.IsSelected != true || !SameDirectory(selection.directory, _projectWorkspace)
                    || !_chatBinding.Matches(snapshot.Identity, _chatIdeInstance, _origin.AbsoluteUri, selection.sessionId))
                    throw new InvalidOperationException("Chat-Zuordnung geändert. Kein Quelltext angehängt.");
                // The native click (or the operator's auto-attach setting) is the
                // disclosure action. Revalidate after the asynchronous browser read.
                if (ChatContextPolicy.Revision(FreshChatSnapshot()) != revision)
                    throw new InvalidOperationException("Der Baustein wurde geändert. Bitte den aktuellen Stand erneut anhängen.");
                if (_disposed || documentGeneration != _chatDocumentGeneration)
                    throw new InvalidOperationException("Die Chat-Seite wurde neu geladen. Bitte den Kontext erneut prüfen.");
                var serializer = new JavaScriptSerializer();
                var request = new { sessionId = selection.sessionId, selection.epoch, selection.documentId, contextKey = revision, auto,
                    item = new { providerId = "twincat-assistant", id = auto ? ChatContextPolicy.ActiveReferenceId : ChatContextPolicy.ReferenceId(snapshot),
                        title = snapshot.ObjectName + " / " + System.IO.Path.GetFileNameWithoutExtension(snapshot.Identity.ProjectFile),
                        url = _origin.AbsoluteUri, text = ChatContextPolicy.AttachmentText(snapshot),
                        data = new { revision, reference = ChatContextPolicy.ReferenceId(snapshot), sourceKind = snapshot.SourceKind, dirty = snapshot.Dirty?.State } } };
                var permission = new { request.sessionId, request.epoch, request.documentId, request.contextKey };
                var outcome = await _browser.CoreWebView2.ExecuteScriptAsync("(()=>{try{if(!window.__twinCatChat.permit(" + serializer.Serialize(permission)
                    + "))return false;window.__twinCatChat.attach(" + serializer.Serialize(request) + ");return true;}catch{return false;}})()");
                if (outcome != "true") throw new InvalidOperationException("Unterhaltung gewechselt oder Anhang noch nicht verarbeitet. Kein neuer Anhang bestätigt.");
                _noticeText = null;
            }
            // A quick POU switch can outdate an automatic attempt; the next POU
            // starts its own. Only operator clicks report failures.
            catch (Exception error) { if (!_disposed && !auto) Notify(error is InvalidOperationException ? error.Message : "Kontext konnte nicht angehängt werden. Keine automatische Wiederholung.", "error"); }
            finally { _chatActionPending = false; }
            if (!_disposed) await PollChatSelectionAsync();
            _autoAttaching = false;
            if (!_disposed && auto) RenderChatBinding();
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
                    throw new InvalidOperationException("Chat-Auswahl geändert.");
                var model = selection.model?.StartsWith(provider + "/", StringComparison.Ordinal) == true ? selection.model : null;
                _modelStatus.Text = "Modellanbieter " + provider + " wird freigegeben …";
                await NativeModelConsent.ConfigureAsync(_chatConfiguration(), provider, model);
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
                _modelStatus.Text = "Modellanbieter " + provider + " ist für diesen Assistant zugelassen. "
                    + (model == null ? "Modell im Chat wählen." : "Standardmodell: " + model + ".") + " PLC-Schreib- und Shell-Werkzeuge bleiben gesperrt.";
            }
            catch
            {
                if (!_disposed)
                {
                    _modelStatus.Text = "Freigabe des Modellanbieters konnte nicht bestätigt werden.";
                    var retry = provider;
                    Notify("Der Modellanbieter " + provider + " konnte nicht freigegeben werden.", "error", "Erneut versuchen",
                        () => { _consentAttempted.Remove(retry); DismissNotice(); RunAction(() => AllowProviderAsync(retry)); });
                }
            }
            finally { _chatActionPending = false; }
            if (!_disposed) await PollChatSelectionStateAsync();
        }
    }
}
