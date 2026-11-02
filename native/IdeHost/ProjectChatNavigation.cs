using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private ProjectChatPicker _projectChatPicker;
        private PickerScope _pickerScope;
        private long _pickerRequest;
        private sealed class PickerScope
        {
            public NativeProjectIdentity Project;
            public bool General;
            public long Document, Request;
            public string[] Directories;
            public string Name;
            public ChatSessionEntry[] Entries;
        }
        private void CloseProjectChats()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _pickerRequest++;
            _pickerScope = null;
            _projectChatPicker?.Close();
        }
        private void CloseProjectChatsOnChange(NativeProjectIdentity project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!SameProject(_chatProject, project)) { _autoProjectKey = null; _explicitGeneralRequested = false; }
            if (_pickerScope != null && !SameProject(_pickerScope.Project, project)) CloseProjectChats();
        }
        private static bool SameProject(NativeProjectIdentity a, NativeProjectIdentity b) { return a == null ? b == null : a.Equals(b); }
        private bool PickerCurrent(PickerScope scope)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return !_disposed && ReferenceEquals(scope, _pickerScope) && scope.Request == _pickerRequest
                && scope.Document == _chatDocumentGeneration && SameProject(scope.Project, ReadProjectIdentity());
        }
        private void CheckPicker(PickerScope scope)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!PickerCurrent(scope)) throw new OperationCanceledException();
            if (!_chatAdapterReady || _chatActionPending) throw new InvalidOperationException("Der Chat wird gerade vorbereitet. Bitte kurz warten.");
        }
        private async Task ShowProjectChatsAsync(bool general)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || !_chatAdapterReady || _chatSelection?.authenticated != true || _chatActionPending) return;
            if (_projectChatPicker?.IsOpen == true) { CloseProjectChats(); return; }
            var project = ReadProjectIdentity();
            if (!general && project == null) general = true;
            if (!general && !await PrepareProjectWorkspaceAsync(project)) return;
            _menuPopup.IsOpen = false; _previewPopup.IsOpen = false;
            var scope = new PickerScope { Project = project, General = general, Document = _chatDocumentGeneration,
                Request = ++_pickerRequest, Directories = general ? new[] { _chatWorkspace() } : (_projectWorkspaces ?? new[] { _projectWorkspace ?? _chatWorkspace() }),
                Name = general ? "Allgemeine Chats" : ProjectName(project.ProjectFile) };
            _pickerScope = scope;
            if (_projectChatPicker == null) _projectChatPicker = new ProjectChatPicker(_historyButton, _theme);
            _projectChatPicker.SelectAsync = id => SelectPickerChatAsync(scope, id);
            _projectChatPicker.NewAsync = async () => {
                CheckPicker(scope);
                var before = _chatSelection?.sessionId;
                await NewChatAsync(scope.General);
                if (!PickerCurrent(scope)) throw new OperationCanceledException();
                if (_chatSelection?.IsSelected != true || _chatSelection.sessionId == before || !CanPromptCurrentChat())
                    throw new InvalidOperationException("Der neue Chat wurde nicht bestätigt. Bitte zuerst den Verlauf neu laden.");
            };
            _projectChatPicker.RetryAsync = () => LoadPickerAsync(scope, true);
            _projectChatPicker.RenameAsync = _chatSelection.canManage ? (Func<string, string, Task>)((id, title) => ManagePickerChatAsync(scope, id, "rename", title)) : null;
            _projectChatPicker.GenerateTitleAsync = _chatSelection.canManage ? (Func<string, Task>)(id => ManagePickerChatAsync(scope, id, "generate-title")) : null;
            _projectChatPicker.ArchiveAsync = _chatSelection.canManage ? (Func<string, Task>)(id => ManagePickerChatAsync(scope, id, "archive")) : null;
            _projectChatPicker.RestoreAsync = _chatSelection.canManage ? (Func<string, Task>)(id => ManagePickerChatAsync(scope, id, "restore")) : null;
            await LoadPickerAsync(scope, true);
        }
        private async Task LoadPickerAsync(PickerScope scope, bool loading)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!PickerCurrent(scope)) return;
            if (loading) _projectChatPicker.ShowLoading(scope.Name);
            if (!_projectChatPicker.IsOpen) return;
            try
            {
                scope.Entries = await PickerEntriesAsync(scope);
                if (!PickerCurrent(scope)) return;
                _projectChatPicker.Show(scope.Name, _chatSelection?.sessionId, scope.Entries.Select(entry => new ProjectChatPickerItem {
                    Id = entry.id, Title = entry.title, Updated = (long)Math.Max(0, Math.Min(entry.updated, 253402300799999d)), Busy = entry.busy, Archived = entry.archived }).ToArray());
            }
            catch (OperationCanceledException) { }
            catch { if (PickerCurrent(scope)) _projectChatPicker.ShowError("Der Verlauf konnte nicht geladen werden. Deine Chats bleiben erhalten."); }
        }
        private async Task<ChatSessionEntry[]> PickerEntriesAsync(PickerScope scope)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            CheckPicker(scope);
            var entries = await ReadRecentSessionsAsync(true, scope.Directories);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            CheckPicker(scope);
            return entries.Where(entry => {
                if (!ChatSelection.ValidSession(entry.id) || !scope.Directories.Any(path => SameDirectory(path, entry.directory))) return false;
                var binding = _chatBindings.Read(_origin.AbsoluteUri, entry.id);
                return scope.General ? binding == null || binding.IsGeneral
                    : binding == null ? _mcpEnabled : binding.SameProjectContext(scope.Project);
            }).GroupBy(entry => entry.id).Select(group => group.First()).ToArray();
        }
        private async Task SelectPickerChatAsync(PickerScope scope, string id)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var entries = await PickerEntriesAsync(scope);
            var target = entries.SingleOrDefault(entry => entry.id == id && !entry.archived);
            if (target == null) throw new InvalidOperationException("Dieser Chat ist nicht mehr im ausgewählten Verlauf verfügbar.");
            CheckPicker(scope);
            // Bind first: another live IDE's conversation cannot be selected
            // and accidentally receive this window's context or prompt.
            var binding = scope.General ? ChatContextBinding.CreateGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id, _origin.AbsoluteUri, id)
                : ChatContextBinding.CreateProject(scope.Project, _chatIdeInstance, _origin.AbsoluteUri, id);
            BindPickerChat(binding);
            _chatActionPending = true; RenderChatBinding();
            try
            {
                if (!scope.General && _mcpEnabled && !await PrepareProjectWorkspaceAsync(scope.Project, target.directory)) throw new InvalidOperationException("Projektwerkzeuge noch nicht verfügbar.");
                if (!PickerCurrent(scope)) throw new OperationCanceledException();
                if (!await SelectSessionAsync(id, target.directory)) throw new InvalidOperationException("Dieser Chat konnte noch nicht geöffnet werden.");
                if (!PickerCurrent(scope)) throw new OperationCanceledException();
                _explicitGeneralRequested = scope.General;
                if (scope.General) await BindGeneralCoreAsync(); else await BindSelectionAsync(scope.Project);
            }
            finally { _chatActionPending = false; if (!_disposed) await PollChatSelectionStateAsync(); }
        }
        private async Task ManagePickerChatAsync(PickerScope scope, string id, string action, string title = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var target = (await PickerEntriesAsync(scope)).SingleOrDefault(entry => entry.id == id);
            if (target == null) throw new InvalidOperationException("Dieser Chat gehört nicht zum ausgewählten Verlauf.");
            if ((action == "archive" || action == "generate-title") && target.busy) throw new InvalidOperationException("In diesem Chat läuft noch eine Antwort.");
            if (action == "generate-title" && target.archived) throw new InvalidOperationException("Zuerst den archivierten Chat wiederherstellen.");
            // Retain the existing live-IDE ownership check for management too.
            BindPickerChat(scope.General ? ChatContextBinding.CreateGeneral(_chatIdeInstance, System.Diagnostics.Process.GetCurrentProcess().Id, _origin.AbsoluteUri, id)
                : ChatContextBinding.CreateProject(scope.Project, _chatIdeInstance, _origin.AbsoluteUri, id));
            CheckPicker(scope);
            var selection = await ReadChatSelectionAsync();
            if (!PickerCurrent(scope)) throw new OperationCanceledException();
            if (selection?.IsSelected != true) throw new InvalidOperationException("Zuerst einen Chat öffnen.");
            var context = _chatBinding?.IsGeneral == true ? "general" : ProjectKey(_chatProject);
            var serializer = new JavaScriptSerializer();
            var expected = new { sessionId = selection.sessionId, selection.epoch, selection.documentId, contextKey = context };
            var request = new { id, directory = target.directory, action, title };
            var slot = Guid.NewGuid().ToString("N");
            _chatActionPending = true; RenderChatBinding();
            try
            {
                await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatManage={id:'" + slot + "',phase:'pending'};window.__twinCatChat.manageSession("
                    + serializer.Serialize(expected) + "," + serializer.Serialize(request) + ").then(()=>{if(window.__twinCatManage?.id==='" + slot
                    + "')window.__twinCatManage.phase='ready'},error=>{if(window.__twinCatManage?.id==='" + slot
                    + "'){window.__twinCatManage.phase='failed';window.__twinCatManage.code=['empty_history','history_unavailable','chat_busy','archived','invalid_title','provider_failed'].includes(error?.code)?error.code:'unknown'}});");
                var deadline = DateTime.UtcNow.AddSeconds(action == "generate-title" ? 100 : 30);
                while (PickerCurrent(scope) && DateTime.UtcNow < deadline)
                {
                    var phase = await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatManage?.id==='" + slot + "'?window.__twinCatManage.phase:null");
                    if (phase == "\"ready\"") break;
                    if (phase == "\"failed\"")
                    {
                        var codeRaw = await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatManage?.id==='" + slot + "'?window.__twinCatManage.code:null");
                        var code = codeRaw != null && codeRaw.Length < 100 ? serializer.Deserialize<string>(codeRaw) : null;
                        throw new InvalidOperationException(action == "generate-title" ? TitleFailureMessage(code)
                            : "Die Änderung wurde nicht bestätigt. Bitte den Verlauf erneut laden.");
                    }
                    await Task.Delay(100);
                }
                if (!PickerCurrent(scope)) throw new OperationCanceledException();
                var confirmed = await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatManage?.id==='" + slot + "'&&window.__twinCatManage.phase==='ready'");
                if (confirmed != "true") throw new InvalidOperationException("Änderung nicht bestätigt. Bitte vor einem erneuten Versuch den Verlauf laden.");
            }
            finally { _chatActionPending = false; if (!_disposed) await PollChatSelectionStateAsync(); }
            if (action == "archive" && selection.sessionId == id && PickerCurrent(scope))
            {
                if (scope.General)
                {
                    var next = (await PickerEntriesAsync(scope)).FirstOrDefault(entry => !entry.archived && entry.id != id);
                    if (next == null) await NewChatAsync(true); else await SelectPickerChatAsync(scope, next.id);
                }
                else await OpenProjectChatAsync();
            }
            if (PickerCurrent(scope)) await LoadPickerAsync(scope, false);
        }
        private void BindPickerChat(ChatContextBinding binding)
        {
            try { _chatBindings.Bind(binding); }
            catch (InvalidOperationException) { throw new InvalidOperationException("Dieser Chat gehört zu einem anderen Projekt oder wird in einer anderen IDE verwendet."); }
        }
        private static string TitleFailureMessage(string code)
        {
            switch (code)
            {
                case "empty_history": return "Dieser Chat enthält noch keinen abgeschlossenen Austausch. Der bisherige Titel bleibt erhalten.";
                case "history_unavailable": return "Der gespeicherte Chatverlauf konnte nicht vollständig geladen werden. Bitte erneut versuchen.";
                case "chat_busy": return "In diesem Chat läuft noch eine Antwort oder sein Status ist noch nicht bestätigt. Bitte kurz warten.";
                case "archived": return "Bitte zuerst den archivierten Chat wiederherstellen.";
                case "invalid_title": return "Der Modellanbieter hat einen fehlerhaften Titel geliefert. Der bisherige Titel bleibt erhalten. Bitte erneut versuchen.";
                case "provider_failed": return "Der Modellanbieter konnte die Titelanfrage nicht abschließen. Bitte Anbieter und Modell prüfen. Der bisherige Titel bleibt erhalten.";
                default: return "Die Titelanfrage wurde nicht bestätigt. Bitte den Verlauf neu laden und erneut versuchen. Der bisherige Titel bleibt erhalten.";
            }
        }
    }
}
