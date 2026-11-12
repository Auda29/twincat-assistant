using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private EngineeringHostTransport _editorTransport;
        private bool _engineeringEditorEnabled, _engineeringEditorChanging;
        private string _editorChat, _editorWorkspace;
        private Guid _editorBinding;
        private long _editorGeneration;
        private NativeChatSession _editorOwner;
        private Task _editorRevocation = Task.CompletedTask;
        private bool _editorToggleRequested;
        private Button _editorModeButton;
        internal event Action EngineeringEditorModeChanged;
        internal bool EngineeringEditorProfileEnabled { get { return _engineeringEditorEnabled; } }

        private bool EditorProfileIsCurrent()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return !_disposed && _engineeringEditorEnabled && !_engineeringProjectSuspended
                && _editorTransport != null && _engineering?.Binding != null
                && _engineering.Binding.SessionId == _editorBinding && _chatSession?.IsRunning == true
                && CanPromptCurrentChat() && _chatBinding?.IsGeneral == false
                && _chatBinding.SameProjectContext(_engineeringProject)
                && _chatSelection?.sessionId == _editorChat && _projectWorkspace == _editorWorkspace;
        }

        private void BuildEngineeringEditorMode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _editorModeButton = Secondary(AsyncButton(HostText.Get("EngineeringEditorEnable"), ToggleEngineeringEditorAsync));
            _toolbar.Children.Add(_editorModeButton);
        }

        private async Task ToggleEngineeringEditorAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_editorToggleRequested) return;
            _editorToggleRequested = true;
            try { await ToggleEngineeringEditorCoreAsync(); }
            finally { _editorToggleRequested = false; }
        }
        private async Task ToggleEngineeringEditorCoreAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_engineeringEditorChanging) return;
            if (_engineeringEditorEnabled) { RevokeEngineeringEditor(); return; }
            await _editorRevocation;
            var session = _engineering;
            var runtime = _chatSession;
            if (_disposed || session?.Binding == null || runtime?.IsRunning != true || !CanPromptCurrentChat()
                || _chatBinding?.IsGeneral != false || !_chatBinding.SameProjectContext(_engineeringProject)
                || _engineeringProjectSuspended || string.IsNullOrEmpty(_projectWorkspace))
            { SetEngineeringStatus("EngineeringEditorUnavailable"); return; }
            var chat = _chatSelection.sessionId;
            var workspace = _projectWorkspace;
            var binding = session.Binding;
            if (MessageBox.Show(HostText.Get("EngineeringEditorConsent"), HostText.Get("EngineeringEditorEnable"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            if (_disposed || session != _engineering || runtime != _chatSession || _chatSelection?.sessionId != chat
                || _projectWorkspace != workspace || _engineeringProjectSuspended) return;
            _engineeringEditorChanging = true; _editorModeButton.IsEnabled = false;
            var generation = ++_editorGeneration;
            var selectedEpoch = _chatSelection.epoch;
            var selectedDocument = _chatSelection.documentId;
            _editorChat = chat; _editorWorkspace = workspace; _editorBinding = binding.SessionId; _editorOwner = runtime;
            var submitted = false;
            EngineeringHostTransport transport = null;
            try
            {
                var configuration = await GetChatConfigurationAsync();
                if (_disposed || generation != _editorGeneration || session != _engineering || runtime != _chatSession || _chatSelection?.sessionId != chat
                    || _projectWorkspace != workspace || _engineeringProjectSuspended) return;
                transport = new EngineeringHostTransport(binding.SessionId, (request, ct) => DispatchEditorEngineeringAsync(session, request, ct));
                _editorTransport = transport;
                var id = Guid.NewGuid().ToString("D");
                var command = new { kind = "editor.bind", requestId = id,
                    bootstrap = new { kind = "twincat-assistant-engineering-bootstrap", protocolVersion = 1,
                        pipeName = @"\\.\pipe\" + transport.PipeName, sessionId = binding.SessionId.ToString("D"), capability = transport.Capability,
                        expected = new { idePid = binding.IdePid, ideStartedUtc = binding.IdeStartedUtc.ToString("o"),
                            solutionFile = binding.SolutionFile, projectFile = binding.ProjectFile, contextEpoch = binding.ContextEpoch } },
                    workspace, sessionId = chat, entrypoint = Path.Combine(configuration.RepositoryRoot, "runtime", "mcp", "engineering", "packages", "mcp", "dist", "index.js") };
                submitted = true;
                if (!await runtime.EditorControlAsync(command, id, "editor-ready", CancellationToken.None)) throw new InvalidOperationException();
                var freshSelection = await ReadChatSelectionAsync();
                if (_disposed || generation != _editorGeneration || freshSelection?.sessionId != chat
                    || freshSelection.epoch != selectedEpoch || freshSelection.documentId != selectedDocument
                    || session != _engineering || runtime != _chatSession || _chatSelection?.sessionId != chat
                    || _projectWorkspace != workspace || _engineeringProjectSuspended) throw new InvalidOperationException();
                _editorTransport = transport; transport = null;
                _editorChat = chat; _editorWorkspace = workspace; _editorBinding = binding.SessionId;
                _engineeringEditorEnabled = true;
                if (!await PublishEngineeringEditorModeAsync(chat, true)) throw new InvalidOperationException();
                _editorModeButton.Content = HostText.Get("EngineeringEditorDisable");
                EngineeringEditorModeChanged?.Invoke();
                SetEngineeringStatus("EngineeringEditorEnabled");
            }
            catch
            {
                RevokeEngineeringEditor();
                if (submitted) await _editorRevocation;
                if (!_disposed) SetEngineeringStatus("EngineeringEditorUnavailable");
            }
            finally
            {
                transport?.Dispose();
                if (!_engineeringEditorEnabled && _editorOwner == runtime) RevokeEngineeringEditor();
                _engineeringEditorChanging = false; _editorModeButton.IsEnabled = true;
            }
        }

        private void RevokeEngineeringEditor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ++_editorGeneration;
            var wasActive = _engineeringEditorEnabled || _editorTransport != null || _editorOwner != null;
            var runtime = _editorOwner; _editorOwner = null;
            _engineeringEditorEnabled = false;
            _editorTransport?.Dispose(); _editorTransport = null;
            var chat = _editorChat;
            _editorChat = _editorWorkspace = null; _editorBinding = Guid.Empty;
            if (_editorModeButton != null) _editorModeButton.Content = HostText.Get("EngineeringEditorEnable");
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
            if (_chatSelection?.sessionId != _editorChat || _projectWorkspace != _editorWorkspace
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
                var data = EngineeringProviderSession.Json().Serialize(new { sessionId = session, enabled });
                return await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatSource?.setEditorProfile(" + data + ") === true") == "true";
            }
            catch { return false; }
        }
    }
}
