using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private readonly string _chatIdeInstance = Guid.NewGuid().ToString("N");
        private readonly ChatBindingStore _chatBindings = new ChatBindingStore();
        private ActiveEditorSnapshot _chatActive;
        private Func<ActiveEditorSnapshot> _chatSnapshotReader;
        private Func<string> _chatWorkspace = () => Path.Combine(NativeChatConfiguration.Load().StateRoot, "workspace");
        private Func<NativeChatConfiguration> _chatConfiguration = NativeChatConfiguration.Load;
        private string _chatBrowserProfile = null;
        private ChatSelection _chatSelection;
        private ChatContextBinding _chatBinding;
        private bool _chatActionPending, _chatAdapterReady, _chatPolling, _autoConnectAttempted;
        private readonly TextBlock _bindingStatus = new TextBlock { Text = "Chat wird vorbereitet.", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        private Button _newChatButton, _bindChatButton, _attachPouButton;
        private readonly TextBlock _modelStatus = new TextBlock { Text = "Modellzugang benötigt eine ausdrückliche Freigabe. Zugangsdaten werden nur in OpenChambers Anbieter-Einstellungen eingegeben.", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        private Button _allowModelButton;
        private readonly ComboBox _consentProvider = new ComboBox { MinWidth = 160, MaxWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Left, DisplayMemberPath = "name", SelectedValuePath = "id" };
        private string _consentProviderKey;
        private readonly TextBlock _selectedModel = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        private DispatcherTimer _chatReconnect;
        private int _chatReconnectAttempts;
        private long _chatDocumentGeneration;

        private void AddModelConsentControls(Panel details)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            details.Children.Add(_modelStatus);
            details.Children.Add(_selectedModel);
            details.Children.Add(new TextBlock { Text = "Angemeldeten Anbieter freigeben (danach Modell im Chat wählen):", TextWrapping = TextWrapping.Wrap });
            details.Children.Add(_consentProvider);
            _consentProvider.SelectionChanged += (sender, args) => {
                _allowModelButton.IsEnabled = !_chatActionPending && CanPromptCurrentChat() && _consentProvider.SelectedItem is ChatProviderChoice;
            };
            details.Children.Add(AsyncButton("Neuer Chat ohne Projektkontext", () => NewChatAsync(true)));
            _allowModelButton = AsyncButton("Anbieter für freigegebene Inhalte zulassen", AllowSelectedModelAsync);
            _allowModelButton.ToolTip = "Erlaubt dem hier ausgewählten angemeldeten Anbieter die Verarbeitung ausdrücklich angehängter Inhalte und deiner Nachrichten in diesem lokalen Profil. Die Freigabe benötigt noch keine Modellauswahl. PLC-Schreib- und Shell-Werkzeuge bleiben gesperrt. Zugangsdaten werden hier nicht angefordert.";
            details.Children.Add(_allowModelButton);
            details.Children.Add(AsyncButton("Anbieter in OpenChamber einrichten", async () => {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_chatAdapterReady && !_disposed) await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat.openSettings()");
            }));
        }

        private void AddChatIntegrationControls(Panel panel)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            _newChatButton = AsyncButton("Neuer Projektchat", NewProjectChatAsync);
            _bindChatButton = AsyncButton("Chat zuordnen", BindCurrentChatAsync);
            _attachPouButton = AsyncButton("POU an Chat anhängen", AttachActivePouAsync);
            _attachPouButton.ToolTip = "Gibt den aktuellen POU-Snapshot für den Composer dieser Unterhaltung frei. Erst Absenden übermittelt ihn an das gewählte Modell. Kein automatisches Speichern oder Ändern des PLC-Projekts.";
            actions.Children.Add(_newChatButton); actions.Children.Add(_bindChatButton); actions.Children.Add(_attachPouButton);
            actions.Children.Add(AsyncButton("Verlauf", async () => {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_chatAdapterReady && !_disposed) await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat.toggleChats()");
            }));
            panel.Children.Add(actions); panel.Children.Add(_bindingStatus);
            Loaded += OnChatLoaded;
            _chatReconnect = new DispatcherTimer(DispatcherPriority.Background);
            _chatReconnect.Tick += ReconnectChat;
            RenderChatBinding();
        }

        private void ScheduleChatReconnect()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || _chatReconnect.IsEnabled || _chatReconnectAttempts >= 3) return;
            _chatReconnect.Interval = TimeSpan.FromSeconds(new[] { 2, 5, 15 }[_chatReconnectAttempts++]);
            _chatReconnect.Start();
            _bindingStatus.Text = "Chat-Verbindung unterbrochen. Erneute Verbindung wird versucht; Quelltext wird nicht erneut angehängt.";
        }

#pragma warning disable VSTHRD100 // Dispatcher timer event boundary.
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
#pragma warning restore VSTHRD100

#pragma warning disable VSTHRD100 // WPF event boundary.
        private async void OnChatLoaded(object sender, RoutedEventArgs args)
        {
            if (_autoConnectAttempted || _disposed) return;
            _autoConnectAttempted = true;
            try { await ConnectAsync(); } catch { if (!_disposed) _bindingStatus.Text = "Chat nicht erreichbar. Erneut verbinden über Details."; }
        }
#pragma warning restore VSTHRD100

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
            catch { if (!_disposed && generation == _chatConnection.Generation) _bindingStatus.Text = "Chat-Integration nicht verfügbar. Version und Verbindung in Details prüfen."; }
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
                _chatBinding = selection?.IsSelected == true ? _chatBindings.Read(_origin.AbsoluteUri, selection.sessionId) : null;
                var permitted = CanPromptCurrentChat();
                var permission = permitted ? new { sessionId = selection.sessionId, selection.epoch, selection.documentId, contextKey = _chatBinding.IsGeneral ? "general" : ChatContextPolicy.Revision(_chatActive) } : null;
                await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat?.permit(" + new JavaScriptSerializer().Serialize(permission) + ")");
                if (!_disposed && generation == _chatConnection.Generation) RenderChatBinding();
            }
            catch
            {
                if (!_disposed && generation == _chatConnection.Generation)
                { _chatAdapterReady = false; _chatSelection = null; _chatBinding = null; RenderChatBinding(); }
            }
            finally { _chatPolling = false; }
        }

        private bool HasCurrentChatBinding()
        {
            return !_disposed && _chatAdapterReady && _chatSelection?.IsSelected == true
                && _chatBinding?.Matches(_chatActive?.Identity, _chatIdeInstance, _origin?.AbsoluteUri, _chatSelection.sessionId) == true;
        }
        private bool CanPromptCurrentChat()
        {
            return HasCurrentChatBinding() || (!_disposed && _chatAdapterReady && _chatSelection?.IsSelected == true
                && _chatBinding?.MatchesGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id,
                    _origin?.AbsoluteUri, _chatSelection.sessionId) == true);
        }

        private void RenderChatBinding()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_newChatButton == null) return;
            var available = !_disposed && !_chatActionPending && _chatAdapterReady && _chatSelection?.authenticated == true;
            _newChatButton.Content = _chatActive?.Identity == null ? "Neuer Chat" : "Neuer Projektchat";
            _newChatButton.IsEnabled = available;
            _bindChatButton.Content = _chatActive?.Identity == null ? "Chat verwenden" : "Chat zuordnen";
            _bindChatButton.IsEnabled = available && _chatSelection?.IsSelected == true && !HasCurrentChatBinding()
                && (_chatActive?.Identity != null || !CanPromptCurrentChat());
            _attachPouButton.IsEnabled = available && HasCurrentChatBinding();
            var choices = _chatSelection?.providers ?? new ChatProviderChoice[0];
            var choiceKey = string.Join("\0", choices.Select(choice => choice.id + ":" + choice.name));
            if (_consentProviderKey != choiceKey)
            {
                var priorProvider = _consentProvider.SelectedValue as string;
                _consentProviderKey = choiceKey;
                _consentProvider.ItemsSource = choices;
                _consentProvider.SelectedValue = priorProvider;
                if (_consentProvider.SelectedIndex < 0 && choices.Length > 0) _consentProvider.SelectedIndex = 0;
            }
            _consentProvider.IsEnabled = available && !_chatActionPending;
            if (_allowModelButton != null) _allowModelButton.IsEnabled = available && CanPromptCurrentChat() && _consentProvider.SelectedItem is ChatProviderChoice;
            _selectedModel.Text = string.IsNullOrEmpty(_chatSelection?.model) ? "Noch kein Modell im Chat ausgewählt." : "Ausgewählt: " + _chatSelection.model;
            if (_chatActionPending) return;
            if (_chatConnection.Phase == "failed") _bindingStatus.Text = "Chat nicht verbunden. Verbindung unter Details prüfen.";
            else if (!_chatAdapterReady) _bindingStatus.Text = "Chat wird vorbereitet. Nach dem Start bitte anmelden.";
            else if (_chatSelection?.authenticated != true) _bindingStatus.Text = "Bitte im lokalen Chat anmelden. Quelltext bleibt lokal.";
            else if (CanPromptCurrentChat() && _chatBinding.IsGeneral) _bindingStatus.Text = "Allgemeiner Chat. Kein nativer Projekt- oder POU-Kontext freigegeben.";
            else if (_chatActive == null) _bindingStatus.Text = "Kein aktiver ST-Baustein. Ein allgemeiner Chat ist ohne POU-Kontext möglich.";
            else if (_chatSelection?.IsSelected != true) _bindingStatus.Text = "Neuen Projektchat starten oder eine Unterhaltung aus dem Verlauf öffnen.";
            else if (!HasCurrentChatBinding()) _bindingStatus.Text = _chatBinding == null
                ? "Diese Unterhaltung ist keinem TwinCAT-Projekt zugeordnet."
                : "Diese Unterhaltung gehört zu einem anderen Projekt oder einer anderen IDE. Keine Kontextübernahme.";
            else _bindingStatus.Text = "Chat für " + Path.GetFileNameWithoutExtension(_chatActive.Identity.ProjectFile)
                + " zugeordnet. " + (_chatSelection.attachedObject == null ? "Quelltext bleibt lokal." : "Letzte Kontextübergabe: " + _chatSelection.attachedObject
                    + (_chatSelection.attachedRevision == ChatContextPolicy.Revision(_chatActive) ? "." : ". Aktiver Stand abweichend; kein neuer Quelltext übertragen."));
        }

        private ActiveEditorSnapshot FreshChatSnapshot()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = _chatSnapshotReader?.Invoke();
            if (result?.Identity == null) throw new InvalidOperationException("Der aktive ST-Baustein ist nicht eindeutig verfügbar.");
            return result;
        }

        private Task NewProjectChatAsync() { return NewChatAsync(false); }

        private async Task NewChatAsync(bool general)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending || !_chatAdapterReady) return;
            _chatActionPending = true; RenderChatBinding();
            try
            {
                var snapshot = general ? null : _chatSnapshotReader?.Invoke();
                var identity = snapshot?.Identity;
                var options = new { title = identity == null ? "Allgemeiner TwinCAT-Chat" : Path.GetFileNameWithoutExtension(identity.ProjectFile) + " / " + snapshot.ObjectName,
                    directory = _chatWorkspace() };
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
                    if (selection?.IsSelected != true) throw new InvalidOperationException("Der neue Chat ist nicht verfügbar.");
                    var binding = ChatContextBinding.CreateGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id, _origin.AbsoluteUri, selection.sessionId);
                    _chatBindings.Bind(binding); _chatSelection = selection; _chatBinding = binding;
                }
                else
                {
                    var current = FreshChatSnapshot();
                    if (!identity.Equals(current.Identity) || current.ContextGeneration != snapshot.ContextGeneration) throw new InvalidOperationException("Editor gewechselt. Der neue Chat wurde nicht zugeordnet.");
                    await BindSelectionAsync(current);
                }
            }
            catch (Exception error) { if (!_disposed) _bindingStatus.Text = error is InvalidOperationException ? error.Message : "Projektchat konnte nicht angelegt werden. Bitte Verbindung prüfen."; }
            finally { _chatActionPending = false; if (!_disposed) { _newChatButton.IsEnabled = _chatAdapterReady; _bindChatButton.IsEnabled = _chatSelection?.IsSelected == true; } }
        }

        private async Task BindSelectionAsync(ActiveEditorSnapshot snapshot)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var selected = await ReadChatSelectionAsync();
            if (selected?.IsSelected != true) throw new InvalidOperationException("Zuerst eine Unterhaltung öffnen.");
            var fresh = FreshChatSnapshot();
            if (!fresh.Identity.Equals(snapshot.Identity) || fresh.ContextGeneration != snapshot.ContextGeneration) throw new InvalidOperationException("Editor gewechselt. Chat nicht zugeordnet.");
            var binding = ChatContextBinding.Create(fresh.Identity, _chatIdeInstance, _origin.AbsoluteUri, selected.sessionId);
            _chatBindings.Bind(binding);
            _chatSelection = selected; _chatBinding = binding;
            _bindingStatus.Text = "Projektchat zugeordnet. Quelltext bleibt lokal, bis du ihn anhängst.";
        }

        private async Task BindCurrentChatAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending) return;
            _chatActionPending = true; RenderChatBinding();
            try
            {
                var snapshot = _chatSnapshotReader?.Invoke();
                if (snapshot?.Identity != null) await BindSelectionAsync(snapshot);
                else
                {
                    var selection = await ReadChatSelectionAsync();
                    if (selection?.IsSelected != true) throw new InvalidOperationException("Zuerst eine Unterhaltung öffnen.");
                    var binding = ChatContextBinding.CreateGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id, _origin.AbsoluteUri, selection.sessionId);
                    _chatBindings.Bind(binding); _chatSelection = selection; _chatBinding = binding;
                }
            }
            catch (Exception error) { if (!_disposed) _bindingStatus.Text = error is InvalidOperationException ? error.Message : "Chat-Zuordnung nicht verfügbar."; }
            finally { _chatActionPending = false; }
            await PollChatSelectionAsync();
        }

        private async Task AttachActivePouAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending || !HasCurrentChatBinding()) return;
            _chatActionPending = true; RenderChatBinding();
            try
            {
                var snapshot = FreshChatSnapshot();
                var revision = ChatContextPolicy.Revision(snapshot);
                var documentGeneration = _chatDocumentGeneration;
                var selection = await ReadChatSelectionAsync();
                if (selection?.IsSelected != true || !_chatBinding.Matches(snapshot.Identity, _chatIdeInstance, _origin.AbsoluteUri, selection.sessionId))
                    throw new InvalidOperationException("Chat-Zuordnung geändert. Kein Quelltext angehängt.");
                // The native click is the disclosure action. Revalidate after the
                // asynchronous browser read before serializing any source.
                if (ChatContextPolicy.Revision(FreshChatSnapshot()) != revision)
                    throw new InvalidOperationException("Der Baustein wurde geändert. Bitte den aktuellen Stand erneut anhängen.");
                if (_disposed || documentGeneration != _chatDocumentGeneration)
                    throw new InvalidOperationException("Die Chat-Seite wurde neu geladen. Bitte den Kontext erneut prüfen.");
                var serializer = new JavaScriptSerializer();
                var request = new { sessionId = selection.sessionId, selection.epoch, selection.documentId, contextKey = revision,
                    item = new { providerId = "twincat-assistant", id = ChatContextPolicy.ReferenceId(snapshot),
                        title = snapshot.ObjectName + " / " + Path.GetFileNameWithoutExtension(snapshot.Identity.ProjectFile),
                        url = _origin.AbsoluteUri, text = ChatContextPolicy.AttachmentText(snapshot),
                        data = new { revision, sourceKind = snapshot.SourceKind, dirty = snapshot.Dirty?.State } } };
                var permission = new { request.sessionId, request.epoch, request.documentId, request.contextKey };
                var outcome = await _browser.CoreWebView2.ExecuteScriptAsync("(()=>{try{if(!window.__twinCatChat.permit(" + serializer.Serialize(permission)
                    + "))return false;window.__twinCatChat.attach(" + serializer.Serialize(request) + ");return true;}catch{return false;}})()");
                if (outcome != "true") throw new InvalidOperationException("Unterhaltung gewechselt oder Anhang noch nicht verarbeitet. Kein neuer Anhang bestätigt.");
                _bindingStatus.Text = snapshot.ObjectName + " als Snapshot im Composer bereit. Erst Absenden übermittelt ihn an das gewählte Modell.";
            }
            catch (Exception error) { if (!_disposed) _bindingStatus.Text = error is InvalidOperationException ? error.Message : "Kontext konnte nicht angehängt werden. Keine automatische Wiederholung."; }
            finally { _chatActionPending = false; }
        }

        private async Task AllowSelectedModelAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_chatActionPending || !CanPromptCurrentChat()) return;
            _chatActionPending = true; RenderChatBinding();
            try
            {
                var provider = (_consentProvider.SelectedItem as ChatProviderChoice)?.id;
                var selection = await ReadChatSelectionAsync();
                if (selection?.IsSelected != true || string.IsNullOrEmpty(provider) || selection.providers?.Any(choice => choice.id == provider) != true
                    || (!(_chatBinding.IsGeneral && _chatBinding.MatchesGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id, _origin.AbsoluteUri, selection.sessionId))
                        && !_chatBinding.Matches(FreshChatSnapshot().Identity, _chatIdeInstance, _origin.AbsoluteUri, selection.sessionId)))
                    throw new InvalidOperationException("Projektchat zuordnen und einen angemeldeten Anbieter auswählen, bevor du ihn freigibst.");
                var model = selection.model?.StartsWith(provider + "/", StringComparison.Ordinal) == true ? selection.model : null;
                _modelStatus.Text = "Anbieterfreigabe wird geprüft ...";
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
                _modelStatus.Text = "Anbieter " + provider + " ist für ausdrücklich freigegebene Inhalte in diesem lokalen Profil zugelassen. "
                    + (model == null ? "Jetzt das Modell im Chat auswählen." : "Gewähltes Standardmodell: " + model + ".")
                    + " PLC-Schreib- und Shell-Werkzeuge bleiben gesperrt.";
            }
            catch { if (!_disposed) _modelStatus.Text = "Anbieterfreigabe konnte nicht bestätigt werden. Profil, Verbindung und Anbieterauswahl prüfen; keine automatische Wiederholung."; }
            finally { _chatActionPending = false; }
        }
    }
}
