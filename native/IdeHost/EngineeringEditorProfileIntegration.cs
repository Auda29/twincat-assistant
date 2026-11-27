using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private EngineeringHostTransport _editorTransport;
        private bool _engineeringEditorEnabled, _engineeringEditorChanging;
        private string _editorChat, _editorWorkspace;
        private string _activeEngineeringProfile;
        private DebugGrant _debugGrant;
        private Guid _editorBinding;
        private long _editorGeneration, _editorSelectionEpoch, _editorDocumentGeneration;
        private string _editorDocument;
        private NativeChatSession _editorOwner;
        private Task _editorRevocation = Task.CompletedTask;
        private bool _editorToggleRequested;
        private SettledChatProfileRequest _settledProfileRequest;
        internal event Action EngineeringEditorModeChanged;
        internal bool EngineeringEditorProfileEnabled { get { return _engineeringEditorEnabled; } }
        internal string EngineeringProfileCaption => _activeEngineeringProfile == "debug" ? "DebugCaption"
            : _activeEngineeringProfile == "editor" ? "EditorCaption" : "ReadOnlyCaption";

        private bool EditorProfileIsCurrent()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return !_disposed && _engineeringEditorEnabled && !_engineeringProjectSuspended
                && _editorTransport != null && _engineering?.Binding != null
                && _engineering.Binding.SessionId == _editorBinding && _chatSession?.IsRunning == true
                && CanPromptCurrentChat() && _chatBinding?.IsGeneral == false
                && _chatBinding.SameProjectContext(_engineeringProject)
                && EditorSelectionMatches(_chatSelection) && _projectWorkspace == _editorWorkspace;
        }

        private bool EditorSelectionMatches(ChatSelection selection)
        {
            return selection?.IsSelected == true && selection.sessionId == _editorChat
                && selection.documentId == _editorDocument && selection.epoch == _editorSelectionEpoch
                && _chatDocumentGeneration == _editorDocumentGeneration;
        }

        private async Task<bool> VerifyEngineeringEditorViewAsync(long generation, CancellationToken cancellation)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellation);
            if (generation != _editorGeneration || !EditorProfileIsCurrent()) return false;
            var pending = ReadChatSelectionAsync();
            var timeout = Task.Delay(TimeSpan.FromSeconds(3), cancellation);
            var selection = await Task.WhenAny(pending, timeout) == pending ? await pending : null;
            if (cancellation.IsCancellationRequested || generation != _editorGeneration || !EditorProfileIsCurrent()) return false;
            if (EditorSelectionMatches(selection) && selection.activeEngineeringProfile == "twincat-" + _activeEngineeringProfile) return true;
            RevokeEngineeringEditor();
            return false;
        }

        private async Task ConsumeChatProfileRequestAsync(ChatSelection selection, long documentGeneration)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var request = selection?.profileRequest;
            if (selection?.IsSelected != true || request == null || _editorToggleRequested || _engineeringEditorChanging) return;
            _editorToggleRequested = true;
            try
            {
                var fresh = await ReadChatSelectionAsync(includeProfileRequest: true);
                if (_disposed || documentGeneration != _chatDocumentGeneration || !request.Matches(selection, fresh)) return;
                if (_settledProfileRequest != null && await _settledProfileRequest.TryRetryAcknowledgementAsync(
                    fresh, documentGeneration, settled => FinishChatProfileRequestAsync(settled))) return;
                _settledProfileRequest = null;
                var handled = false;
                try
                {
                    if (_engineeringEditorEnabled) RevokeEngineeringEditor();
                    await _editorRevocation;
                    if (request.profile == "twincat-readonly") { handled = true; return; }
                    if (documentGeneration != _chatDocumentGeneration
                        || !request.Matches(selection, await ReadChatSelectionAsync(includeProfileRequest: true))) return;
                    handled = true;
                    var profile = request.profile == "twincat-editor" ? "editor"
                        : request.profile == "twincat-debug" ? "debug" : null;
                    if (profile != null) await EnableEngineeringEditorCoreAsync(profile, selection);
                }
                finally
                {
                    // The success path publishes the grant and acknowledges in
                    // one script. Retry a failed acknowledgement without repeating consent.
                    if (handled && documentGeneration == _chatDocumentGeneration)
                    {
                        _settledProfileRequest = new SettledChatProfileRequest(selection, documentGeneration);
                        await FinishChatProfileRequestAsync(selection);
                    }
                }
            }
            catch { } // A navigation or WebView failure leaves an unread request retryable until expiry.
            finally { _editorToggleRequested = false; }
        }

        private async Task EnableEngineeringEditorCoreAsync(string profile, ChatSelection expected)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var session = _engineering;
            var runtime = _chatSession;
            if (_disposed) return;
            var hasProjectChat = HasCurrentChatBinding()
                && (_engineeringProject == null || _chatBinding.SameProjectContext(_engineeringProject));
            if (!hasProjectChat || _engineeringProject == null || session?.Binding == null || runtime?.IsRunning != true
                || _engineeringProjectSuspended || string.IsNullOrEmpty(_projectWorkspace))
            {
                var key = profile != "debug" ? "EngineeringEditorUnavailable"
                    : hasProjectChat ? "EngineeringDebugVerificationUnavailable" : "EngineeringDebugProjectRequired";
                SetEngineeringStatus(key);
                Notify(HostText.Get(key), "warning");
                return;
            }
            var chat = _chatSelection.sessionId;
            var workspace = _projectWorkspace;
            var binding = session.Binding;
            var selectedEpoch = _chatSelection.epoch;
            var selectedDocument = _chatSelection.documentId;
            var selectedDocumentGeneration = _chatDocumentGeneration;
            DebugGrant debug = null;
            if (profile == "debug")
            {
                var online = await ObserveDebugLoginAsync(session, _engineeringLifetime.Token);
                if (_disposed || session != _engineering || _chatSelection?.sessionId != chat
                    || _chatSelection.epoch != selectedEpoch || _chatSelection.documentId != selectedDocument) return;
                if (!expected.profileRequest.Matches(expected, await ReadChatSelectionAsync(includeProfileRequest: true))
                    || _disposed || session != _engineering || _engineeringLifetime == null) return;
                if (online.Login == null)
                {
                    Notify(HostText.Get(online.Notice), "warning");
                    return;
                }
                if (_chatPreferences.AutoDebug)
                    debug = CreateAutomaticDebugGrant(online.Login);
                if (debug == null)
                {
                    debug = ConfirmDebugGrant(online.Login);
                    if (debug == null) return;
                    if (_disposed || session != _engineering || _engineeringLifetime == null) return;
                    var confirmed = await ObserveDebugLoginAsync(session, _engineeringLifetime.Token);
                    if (!debug.Matches(confirmed.Login))
                    {
                        if (!_disposed && session == _engineering)
                            Notify(HostText.Get(confirmed.Notice ?? "EngineeringDebugTargetChanged"), "warning");
                        return;
                    }
                }
            }
            else if (MessageBox.Show(HostText.Get("EngineeringEditorConsent"), HostText.Get("EngineeringEditorEnable"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            var afterConsent = await ReadChatSelectionAsync(includeProfileRequest: true);
            if (_disposed || session != _engineering || runtime != _chatSession || _chatSelection?.sessionId != chat
                || _chatDocumentGeneration != selectedDocumentGeneration || !expected.profileRequest.Matches(expected, afterConsent)
                || _projectWorkspace != workspace || _engineeringProjectSuspended) return;
            _engineeringEditorChanging = true;
            var generation = ++_editorGeneration;
            _editorChat = chat; _editorWorkspace = workspace; _editorBinding = binding.SessionId; _editorOwner = runtime;
            _editorSelectionEpoch = selectedEpoch; _editorDocument = selectedDocument; _editorDocumentGeneration = selectedDocumentGeneration;
            var submitted = false;
            EngineeringHostTransport transport = null;
            try
            {
                var configuration = await GetChatConfigurationAsync();
                if (_disposed || generation != _editorGeneration || session != _engineering || runtime != _chatSession || _chatSelection?.sessionId != chat
                    || _projectWorkspace != workspace || _engineeringProjectSuspended) return;
                transport = new EngineeringHostTransport(binding.SessionId, (request, ct) => DispatchEditorEngineeringAsync(session, request, ct, generation));
                _editorTransport = transport;
                var id = Guid.NewGuid().ToString("D");
                var command = new { kind = "editor.bind", requestId = id, profile,
                    bootstrap = new { kind = "twincat-assistant-engineering-bootstrap", protocolVersion = 1,
                        pipeName = @"\\.\pipe\" + transport.PipeName, sessionId = binding.SessionId.ToString("D"), capability = transport.Capability,
                        expected = new { idePid = binding.IdePid, ideStartedUtc = binding.IdeStartedUtc.ToString("o"),
                            solutionFile = binding.SolutionFile, projectFile = binding.ProjectFile, contextEpoch = binding.ContextEpoch } },
                    workspace, sessionId = chat, entrypoint = Path.Combine(configuration.RepositoryRoot, "runtime", "mcp", "engineering", "packages", "mcp", "dist", "index.js") };
                submitted = true;
                if (!await runtime.EditorControlAsync(command, id, "editor-ready", CancellationToken.None)) throw new InvalidOperationException();
                var freshSelection = await ReadChatSelectionAsync(includeProfileRequest: true);
                if (_disposed || generation != _editorGeneration || !EditorSelectionMatches(freshSelection)
                    || !expected.profileRequest.Matches(expected, freshSelection)
                    || session != _engineering || runtime != _chatSession || _chatSelection?.sessionId != chat
                    || _projectWorkspace != workspace || _engineeringProjectSuspended) throw new InvalidOperationException();
                _editorTransport = transport; transport = null;
                _editorChat = chat; _editorWorkspace = workspace; _editorBinding = binding.SessionId;
                _activeEngineeringProfile = profile;
                _debugGrant = debug;
                _engineeringEditorEnabled = true;
                if (!EditorProfileIsCurrent() || !await FinishChatProfileRequestAsync(expected, "twincat-" + profile)
                    || generation != _editorGeneration
                    || !EditorProfileIsCurrent()) throw new InvalidOperationException();
                EngineeringEditorModeChanged?.Invoke();
                SetEngineeringStatus(profile == "debug"
                    ? (debug?.AllowAllSymbols == true ? "EngineeringDebugStatusAuto" : "EngineeringDebugEnabled")
                    : "EngineeringEditorEnabled");
                if (profile == "debug" && debug?.AllowAllSymbols == true)
                    Notify(HostText.Format("EngineeringDebugEnabledAuto", debug.Login.AmsNetId, debug.Port));
            }
            catch
            {
                RevokeEngineeringEditor();
                if (submitted) await _editorRevocation;
                if (!_disposed)
                {
                    SetEngineeringStatus(profile == "debug" ? "EngineeringDebugUnavailable" : "EngineeringEditorUnavailable");
                    if (profile == "debug" && session == _engineering && _chatSelection?.sessionId == chat)
                        Notify(HostText.Get("EngineeringDebugUnavailable"), "warning");
                }
            }
            finally
            {
                transport?.Dispose();
                if (!_engineeringEditorEnabled && _editorOwner == runtime) RevokeEngineeringEditor();
                _engineeringEditorChanging = false;
            }
        }

        private void RevokeEngineeringEditor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ++_editorGeneration;
            var wasActive = _engineeringEditorEnabled || _editorTransport != null || _editorOwner != null;
            var runtime = _editorOwner; _editorOwner = null;
            _engineeringEditorEnabled = false;
            _activeEngineeringProfile = null;
            _debugGrant = null;
            _editorTransport?.Dispose(); _editorTransport = null;
            var chat = _editorChat;
            var document = _editorDocument;
            _editorChat = _editorWorkspace = null; _editorBinding = Guid.Empty;
            if (wasActive)
            {
                EngineeringEditorModeChanged?.Invoke();
                _ = PublishEngineeringEditorRevocationAsync(chat, document);
                if (runtime?.IsRunning == true)
                    _editorRevocation = RevokeEditorRuntimeAsync(runtime);
            }
        }
        private void CheckEngineeringEditorContext()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!_engineeringEditorEnabled && !_engineeringEditorChanging) return;
            if (!EditorSelectionMatches(_chatSelection) || _projectWorkspace != _editorWorkspace
                || _engineering?.Binding == null || _engineering.Binding.SessionId != _editorBinding)
            { RevokeEngineeringEditor(); return; }
            if (_engineeringProjectSuspended) return;
            if (_engineeringEditorChanging && !_engineeringEditorEnabled) return;
            if (!EditorProfileIsCurrent()) RevokeEngineeringEditor();
        }
        private static async Task RevokeEditorRuntimeAsync(NativeChatSession runtime)
        {
            if (runtime?.IsRunning != true) return;
            var id = Guid.NewGuid().ToString("D");
            if (!await runtime.EditorControlAsync(new { kind = "editor.revoke", requestId = id }, id, "editor-revoked", CancellationToken.None))
                await runtime.StopAsync(); // Only the runtime this pane owns; its job reaps the broker.
        }
        private async Task<bool> FinishChatProfileRequestAsync(ChatSelection selection, string profile = null)
        {
            try
            {
                if (_disposed || _browser.CoreWebView2 == null || !Allowed(_browser.CoreWebView2.Source)) return false;
                var data = EngineeringProviderSession.Json().Serialize(new {
                    selection.sessionId, selection.documentId, selection.epoch, selection.profileRequest.id, profile
                });
                return await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat?.finishProfileRequest(" + data + ") === true") == "true";
            }
            catch { return false; }
        }

        private async Task PublishEngineeringEditorRevocationAsync(string session, string document)
        {
            try
            {
                if (_disposed || _browser.CoreWebView2 == null || !Allowed(_browser.CoreWebView2.Source)) return;
                var json = EngineeringProviderSession.Json();
                var data = json.Serialize(new { sessionId = session, enabled = false });
                await _browser.CoreWebView2.ExecuteScriptAsync("if (window.__twinCatChat?.documentId === "
                    + json.Serialize(document) + ") window.__twinCatSource?.setEditorProfile(" + data + ")");
            }
            catch { }
        }
    }
}
