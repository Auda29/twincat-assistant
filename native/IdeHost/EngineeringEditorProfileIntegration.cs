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
            if (selection?.IsSelected != true || !ReferenceEquals(selection, _chatSelection)) return;
            if (selection.profileRequest == "twincat-readonly")
            {
                if (_engineeringEditorEnabled || _engineeringEditorChanging) RevokeEngineeringEditor();
                return;
            }
            var profile = selection.profileRequest == "twincat-editor" ? "editor"
                : selection.profileRequest == "twincat-debug" ? "debug" : null;
            if (profile == null || profile == _activeEngineeringProfile) return;
            var fresh = await ReadChatSelectionAsync();
            if (_disposed || documentGeneration != _chatDocumentGeneration || !ReferenceEquals(selection, _chatSelection)
                || fresh?.sessionId != selection.sessionId || fresh.epoch != selection.epoch || fresh.documentId != selection.documentId) return;
            await EnableEngineeringEditorAsync(profile);
        }

        private async Task EnableEngineeringEditorAsync(string profile)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_editorToggleRequested) return;
            _editorToggleRequested = true;
            try { await EnableEngineeringEditorCoreAsync(profile); }
            finally { _editorToggleRequested = false; }
        }
        private async Task EnableEngineeringEditorCoreAsync(string profile)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_engineeringEditorChanging || profile == _activeEngineeringProfile) return;
            if (_engineeringEditorEnabled) RevokeEngineeringEditor();
            await _editorRevocation;
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
                if (online.Login == null)
                {
                    Notify(HostText.Get(online.Notice), "warning");
                    return;
                }
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
            else if (MessageBox.Show(HostText.Get("EngineeringEditorConsent"), HostText.Get("EngineeringEditorEnable"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            var afterConsent = await ReadChatSelectionAsync();
            if (_disposed || session != _engineering || runtime != _chatSession || _chatSelection?.sessionId != chat
                || _chatDocumentGeneration != selectedDocumentGeneration || afterConsent?.IsSelected != true
                || afterConsent.sessionId != chat || afterConsent.epoch != selectedEpoch || afterConsent.documentId != selectedDocument
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
                var freshSelection = await ReadChatSelectionAsync();
                if (_disposed || generation != _editorGeneration || !EditorSelectionMatches(freshSelection)
                    || session != _engineering || runtime != _chatSession || _chatSelection?.sessionId != chat
                    || _projectWorkspace != workspace || _engineeringProjectSuspended) throw new InvalidOperationException();
                _editorTransport = transport; transport = null;
                _editorChat = chat; _editorWorkspace = workspace; _editorBinding = binding.SessionId;
                _activeEngineeringProfile = profile;
                _debugGrant = debug;
                _engineeringEditorEnabled = true;
                if (!await PublishEngineeringEditorModeAsync(chat, true) || generation != _editorGeneration
                    || !EditorProfileIsCurrent()) throw new InvalidOperationException();
                EngineeringEditorModeChanged?.Invoke();
                SetEngineeringStatus(profile == "debug" ? "EngineeringDebugEnabled" : "EngineeringEditorEnabled");
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
            _editorChat = _editorWorkspace = null; _editorBinding = Guid.Empty;
            if (wasActive)
            {
                EngineeringEditorModeChanged?.Invoke();
                _ = PublishEngineeringEditorModeAsync(chat, false);
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
        private async Task<bool> PublishEngineeringEditorModeAsync(string session, bool enabled)
        {
            try
            {
                if (_disposed || _browser.CoreWebView2 == null) return false;
                var profile = _activeEngineeringProfile == "debug" ? "twincat-debug" : "twincat-editor";
                var data = EngineeringProviderSession.Json().Serialize(new { sessionId = session, enabled, profile });
                var expected = EngineeringProviderSession.Json().Serialize(new { sessionId = session, documentId = _editorDocument, epoch = _editorSelectionEpoch });
                var script = enabled
                    ? "(()=>{const s=window.__twinCatChat?.state();const e=" + expected
                      + ";return s?.sessionId===e.sessionId&&s.documentId===e.documentId&&s.epoch===e.epoch&&window.__twinCatSource?.setEditorProfile(" + data + ")===true;})()"
                    : "window.__twinCatSource?.setEditorProfile(" + data + ")===true";
                return await _browser.CoreWebView2.ExecuteScriptAsync(script) == "true";
            }
            catch { return false; }
        }
    }
}
