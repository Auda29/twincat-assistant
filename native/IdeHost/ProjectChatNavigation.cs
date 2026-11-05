using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;

namespace TwinCatAssistant.Native
{
    // Host side of the chat bar. The bar is an ordinary WPF element in the
    // chat area (no OS popup): docked beside the conversation on wide tool
    // windows, an overlay on narrow ones. Every list and action goes through
    // the existing session selection, binding and management paths below;
    // the bar keeps no chats of its own.
    public sealed partial class AssistantControl
    {
        // Docked from this tool window width on; the conversation keeps at
        // least SidebarDockFrom - SidebarWidth (488 px) beside the bar.
        internal const double SidebarWidth = 272, SidebarDockFrom = 760, SidebarOverlayMax = 304, SidebarOverlayGap = 44;
        private ProjectChatPicker _projectChatPicker;
        private PickerScope _pickerScope;
        private long _pickerRequest;
        private int _pickerRefreshing;
        private DateTime _pickerRefreshedAt = DateTime.MinValue;
        private string _pickerUnknownSelection;
        private NativeProjectIdentity _sidebarProject;
        private bool _sidebarWasDocked, _generalSidebarPinned;
        private readonly Dictionary<string, bool> _sidebarPinPreferences = new Dictionary<string, bool>(StringComparer.Ordinal);
        // Restores attempted per project and chat document; preferences per project.
        private readonly HashSet<string> _sidebarRestored = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _sidebarPreferences = new Dictionary<string, bool>(StringComparer.Ordinal);
        private sealed class PickerScope
        {
            public NativeProjectIdentity Project;
            public bool General, Started, Focus;
            public long Document, Request;
            public string[] Directories;
            public string Name;
            public ChatSessionEntry[] Entries;
        }

        // ---- Layout ----

        private bool SidebarDocked()
        {
            var width = ActualWidth > 0 ? ActualWidth : double.IsNaN(Width) ? 0 : Width;
            return width >= SidebarDockFrom || SidebarPinned() && width >= SidebarWidth + 320;
        }

        private ProjectChatPicker EnsureProjectChatPicker()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_projectChatPicker == null) _projectChatPicker = new ProjectChatPicker(_historyButton, _theme);
            var picker = _projectChatPicker;
            picker.Closed = byUser => OnSidebarClosed(picker, byUser);
            picker.PinnedChanged = RememberSidebarPin;
            AttachSidebarView(picker);
            return picker;
        }

        private void AttachSidebarView(ProjectChatPicker picker)
        {
            var view = picker.View;
            if (_sidebarHost.Child == view) return;
            if (view.Parent is Decorator decorator) decorator.Child = null;
            else if (view.Parent is Panel panel) panel.Children.Remove(view);
            _sidebarHost.Child = view;
        }

        private void LayoutSidebar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            var picker = _projectChatPicker;
            var docked = SidebarDocked();
            if (picker != null) { picker.Pinned = SidebarPinned(); picker.Docked = docked; }
            var open = picker?.IsOpen == true && _strip.Visibility == Visibility.Visible && _browser.Visibility == Visibility.Visible;
            if (open) AttachSidebarView(picker);
            _sidebarHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            _sidebarScrim.Visibility = open && !docked ? Visibility.Visible : Visibility.Collapsed;
            var column = open && docked ? SidebarWidth : 0;
            if (_sidebarColumn.Width.Value != column) _sidebarColumn.Width = new GridLength(column);
            Grid.SetColumnSpan(_sidebarHost, docked ? 1 : 2);
            _sidebarHost.HorizontalAlignment = docked ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            var area = ActualWidth > 0 ? ActualWidth : SidebarOverlayMax + SidebarOverlayGap;
            _sidebarHost.Width = docked ? double.NaN : Math.Max(220, Math.Min(SidebarOverlayMax, area - SidebarOverlayGap));
            _sidebarHost.Effect = docked ? null : _sidebarShadow;
            var shown = picker?.IsOpen == true;
            _historyButton.ToolTip = shown ? "Chatleiste schließen" : _chatProject == null ? "Chats anzeigen" : "Chats dieses Projekts anzeigen";
            System.Windows.Automation.AutomationProperties.SetName(_historyButton, (string)_historyButton.ToolTip);
            if (shown) _historyButton.SetResourceReference(ForegroundProperty, "Tc.Primary");
            else _historyButton.ClearValue(ForegroundProperty);
        }

        private void OnSidebarSizeChanged(object sender, SizeChangedEventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || !args.WidthChanged) return;
            var docked = SidebarDocked();
            if (docked != _sidebarWasDocked)
            {
                _sidebarWasDocked = docked;
                // An open bar stays open and turns into the overlay (or back);
                // a wider window may restore the docked preference again.
                if (docked) _sidebarRestored.Clear();
            }
            LayoutSidebar();
            MaintainSidebar();
        }

        // Overlay only: a click outside the bar and the clock closes it.
        private void OnSidebarOutsideMouseDown(object sender, MouseButtonEventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_projectChatPicker?.IsOpen != true || SidebarDocked() || SidebarPinned()) return;
            var source = args.OriginalSource as DependencyObject;
            if (Within(source, _sidebarHost) || Within(source, _historyButton)) return;
            CloseProjectChats();
            if (Within(source, _sidebarScrim)) args.Handled = true;
        }

        private static bool Within(DependencyObject node, DependencyObject container)
        {
            for (; node != null; node = node is Visual || node is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (node == container) return true;
            return false;
        }

        private void OnSidebarClosed(ProjectChatPicker picker, bool byUser)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || picker != _projectChatPicker) return;
            var projectHistory = _pickerScope?.General == false;
            _pickerRequest++;
            _pickerScope = null;
            if (byUser && projectHistory && _chatProject != null) RememberSidebar(_chatProject, false);
            LayoutSidebar();
        }

        // ---- Open state per PLC project and profile ----

        private bool SidebarPinned()
        {
            if (_pickerScope?.General == true) return _generalSidebarPinned;
            var project = _pickerScope?.Project ?? _chatProject;
            if (project == null || _chatBindings == null) return false;
            var key = ProjectKey(project);
            if (_sidebarPinPreferences.TryGetValue(key, out var pinned)) return pinned;
            try { pinned = _chatBindings.ViewState().SidebarPinned(project) ?? false; } catch { pinned = false; }
            _sidebarPinPreferences[key] = pinned;
            return pinned;
        }

        private void RememberSidebarPin(bool pinned)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_pickerScope?.General == true) _generalSidebarPinned = pinned;
            else
            {
                var project = _pickerScope?.Project ?? _chatProject;
                if (project != null)
                {
                    _sidebarPinPreferences[ProjectKey(project)] = pinned;
                    try { _chatBindings.ViewState().RememberPinned(project, pinned); } catch { }
                    RememberSidebar(project, true);
                }
            }
            LayoutSidebar();
        }

        private bool SidebarPreference(NativeProjectIdentity project)
        {
            var key = ProjectKey(project);
            if (_sidebarPreferences.TryGetValue(key, out var open)) return open;
            bool? stored = null;
            try { stored = _chatBindings.ViewState().SidebarOpen(project); } catch { }
            // Like T3: the bar is open beside the chat until the operator closes it.
            open = stored ?? true;
            _sidebarPreferences[key] = open;
            return open;
        }

        private void RememberSidebar(NativeProjectIdentity project, bool open)
        {
            _sidebarPreferences[ProjectKey(project)] = open;
            try { _chatBindings.ViewState().RememberSidebar(project, open); }
            catch { /* The preference still applies until the IDE closes. */ }
        }

        // Reopens the docked bar for the active project when its stored (or
        // default) preference says so, once per project and chat document.
        private void MaintainSidebar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || !_chatPresented || !(SidebarDocked() || SidebarPinned()) || _strip.Visibility != Visibility.Visible || _browser.Visibility != Visibility.Visible) return;
            if (!_chatAdapterReady || _chatSelection?.authenticated != true || _chatActionPending) return;
            var project = _chatProject;
            if (project == null || (_projectChatPicker?.IsOpen == true && _pickerScope != null)) return;
            if (!SidebarPreference(project)) { AbandonSidebarRestore(); return; }
            if (!_sidebarRestored.Add(ProjectKey(project) + "\0" + _chatDocumentGeneration)) return;
            RunAction(() => ShowProjectChatsCoreAsync(false, true));
        }

        // A bar kept open across a project switch waits for its new list;
        // when that list cannot be shown it closes instead of showing stale chats.
        private void AbandonSidebarRestore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_pickerScope == null && _projectChatPicker?.IsOpen == true) CloseProjectChats();
        }

        // Keeps the open bar current: highlight of the open chat at once,
        // titles and answer state through a throttled quiet reload.
        private void UpdateSidebar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            var picker = _projectChatPicker;
            if (picker?.IsOpen == true && _pickerScope != null && _pickerScope.Started)
            {
                var selected = _chatSelection?.IsSelected == true ? _chatSelection.sessionId : null;
                if (!picker.SetSelected(selected) && selected != _pickerUnknownSelection)
                {
                    // A chat created or opened elsewhere: list it soon.
                    _pickerUnknownSelection = selected;
                    RefreshSidebar(true);
                }
            }
            MaintainSidebar();
        }

        private void RefreshSidebar(bool now)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var scope = _pickerScope;
            if (_disposed || scope == null || !scope.Started || _projectChatPicker?.IsOpen != true || _pickerRefreshing > 0
                || _chatActionPending || !_chatAdapterReady || !PickerCurrent(scope)) return;
            if (!now && (DateTime.UtcNow - _pickerRefreshedAt).TotalSeconds < 5) return;
            _pickerRefreshedAt = DateTime.UtcNow;
            RunAction(() => LoadPickerAsync(scope, false, true));
        }

        // ---- Scope lifecycle ----

        private void CloseProjectChats()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _pickerRequest++;
            _pickerScope = null;
            _projectChatPicker?.Close();
            if (!_disposed) LayoutSidebar();
        }

        // Transient closes (other popups, browser focus, the context bar) only
        // concern the narrow overlay; the docked bar is part of the layout.
        private void DismissProjectChatOverlay()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_projectChatPicker?.IsOpen == true && !SidebarDocked() && !SidebarPinned()) CloseProjectChats();
        }

        private void CloseProjectChatsOnChange(NativeProjectIdentity project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!SameProject(_chatProject, project)) { _autoProjectKey = null; _explicitGeneralRequested = false; }
            var picker = _projectChatPicker;
            var stale = (_pickerScope != null && !SameProject(_pickerScope.Project, project))
                || (_pickerScope == null && picker?.IsOpen == true && !SameProject(_sidebarProject, project));
            if (!stale) return;
            if (picker?.IsOpen == true && SidebarDocked() && project != null && SidebarPreference(project) && !_disposed)
            {
                // The docked bar stays in place: a new session with the new
                // project's name and no rows, until its own list is confirmed.
                _pickerRequest++;
                _pickerScope = null;
                _sidebarProject = project;
                picker.BackAsync = null;
                picker.General = false;
                picker.ShowLoading(ProjectName(project.ProjectFile), false, true);
                return;
            }
            CloseProjectChats();
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

        // The clock (general: false) and the "Allgemeine Chats" menu entry
        // (general: true). The same history again closes the bar; the other
        // history switches it.
        private Task ShowProjectChatsAsync(bool general) { return ShowProjectChatsCoreAsync(general, false); }

        private async Task ShowProjectChatsCoreAsync(bool general, bool restore)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || !_chatAdapterReady || _chatSelection?.authenticated != true || _chatActionPending) { if (restore) AbandonSidebarRestore(); return; }
            var project = ReadProjectIdentity();
            if (!general && project == null)
            {
                if (restore) { AbandonSidebarRestore(); return; }
                general = true;
            }
            var current = _pickerScope;
            if (!restore && _projectChatPicker?.IsOpen == true && current != null && current.General == general && SameProject(current.Project, project))
            {
                CloseProjectChats();
                if (project != null && !general) RememberSidebar(project, false);
                return;
            }
            if (restore && _projectChatPicker?.IsOpen == true && current != null) return;
            // Preparation may finish after the page changes or after a user
            // closes/switches the list, including an A -> B -> A project trip.
            var openingRequest = _pickerRequest;
            var openingDocument = _chatDocumentGeneration;
            var prepared = general || await PrepareProjectWorkspaceAsync(project);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || openingRequest != _pickerRequest || openingDocument != _chatDocumentGeneration) return;
            if (!prepared) { if (restore) AbandonSidebarRestore(); return; }
            if (_disposed || !SameProject(project, ReadProjectIdentity()) || (restore && _projectChatPicker?.IsOpen == true && _pickerScope != null))
            { if (restore) AbandonSidebarRestore(); return; }
            if (!restore) { _menuPopup.IsOpen = false; _previewPopup.IsOpen = false; }
            var scope = new PickerScope { Project = project, General = general, Document = _chatDocumentGeneration, Focus = !restore,
                Request = ++_pickerRequest, Directories = general ? new[] { _chatWorkspace() } : (_projectWorkspaces ?? new[] { _projectWorkspace ?? _chatWorkspace() }),
                Name = general ? "Allgemeine Chats" : ProjectName(project.ProjectFile) };
            _pickerScope = scope;
            var canManage = _chatSelection?.canManage == true;
            var picker = EnsureProjectChatPicker();
            picker.SelectAsync = id => SelectPickerChatAsync(scope, id);
            picker.NewAsync = async () => {
                CheckPicker(scope);
                var before = _chatSelection?.sessionId;
                await NewChatAsync(scope.General);
                if (!PickerCurrent(scope)) throw new OperationCanceledException();
                if (_chatSelection?.IsSelected != true || _chatSelection.sessionId == before || !CanPromptCurrentChat())
                    throw new InvalidOperationException("Der neue Chat wurde nicht bestätigt. Bitte zuerst den Verlauf neu laden.");
                // The docked bar stays open and lists the new chat at once.
                if (SidebarDocked() || SidebarPinned()) await LoadPickerAsync(scope, false);
            };
            picker.RetryAsync = () => LoadPickerAsync(scope, true);
            picker.BackAsync = general && project != null ? (Func<Task>)(() => ShowProjectChatsAsync(false)) : null;
            picker.RenameAsync = canManage ? (Func<string, string, Task>)((id, title) => ManagePickerChatAsync(scope, id, "rename", title)) : null;
            picker.GenerateTitleAsync = canManage ? (Func<string, Task>)(id => ManagePickerChatAsync(scope, id, "generate-title")) : null;
            picker.ArchiveAsync = canManage ? (Func<string, Task>)(id => ManagePickerChatAsync(scope, id, "archive")) : null;
            picker.RestoreAsync = canManage ? (Func<string, Task>)(id => ManagePickerChatAsync(scope, id, "restore")) : null;
            if (!restore && SidebarDocked() && project != null && !general) RememberSidebar(project, true);
            await LoadPickerAsync(scope, true);
        }
        private Task LoadPickerAsync(PickerScope scope, bool loading) { return LoadPickerAsync(scope, loading, false); }
        // quiet: a background refresh; a failure keeps the shown list.
        private async Task LoadPickerAsync(PickerScope scope, bool loading, bool quiet)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!PickerCurrent(scope)) return;
            var picker = _projectChatPicker;
            if (picker == null) return;
            if (loading)
            {
                picker.General = scope.General;
                picker.ShowLoading(scope.Name, scope.Focus, !scope.Started);
                scope.Started = true;
                _sidebarProject = scope.Project;
                _pickerUnknownSelection = null;
                LayoutSidebar();
            }
            if (!picker.IsOpen) return;
            _pickerRefreshing++;
            try
            {
                scope.Entries = await PickerEntriesAsync(scope);
                if (!PickerCurrent(scope)) return;
                _pickerRefreshedAt = DateTime.UtcNow;
                picker.Show(scope.Name, _chatSelection?.IsSelected == true ? _chatSelection.sessionId : null, scope.Entries.Select(entry => new ProjectChatPickerItem {
                    Id = entry.id, Title = entry.title, Updated = (long)Math.Max(0, Math.Min(entry.updated, 253402300799999d)), Busy = entry.busy, Archived = entry.archived }).ToArray());
            }
            catch (OperationCanceledException) { }
            catch { if (!quiet && PickerCurrent(scope)) picker.ShowError("Der Verlauf konnte nicht geladen werden. Deine Chats bleiben erhalten."); }
            finally { _pickerRefreshing--; }
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
        // Switching conversations never stops an answer running in the previous one.
        private async Task SelectPickerChatAsync(PickerScope scope, string id)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var entries = await PickerEntriesAsync(scope);
            var target = entries.SingleOrDefault(entry => entry.id == id && !entry.archived);
            if (target == null) throw new InvalidOperationException("Dieser Chat ist nicht mehr unter den offenen Chats verfügbar.");
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
        // "archive" and "restore" are the stored form of "Als erledigt
        // markieren" and "Wieder öffnen"; history and attachments stay.
        private async Task ManagePickerChatAsync(PickerScope scope, string id, string action, string title = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var target = (await PickerEntriesAsync(scope)).SingleOrDefault(entry => entry.id == id);
            if (target == null) throw new InvalidOperationException("Dieser Chat gehört nicht zum ausgewählten Verlauf.");
            if ((action == "archive" || action == "generate-title") && target.busy) throw new InvalidOperationException("In diesem Chat läuft noch eine Antwort.");
            if (action == "generate-title" && target.archived) throw new InvalidOperationException("Zuerst den erledigten Chat wieder öffnen.");
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
            // The open chat was marked done: show another open chat of this
            // history (or a new one) and keep the bar in step.
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
                case "archived": return "Bitte zuerst den erledigten Chat wieder öffnen.";
                case "invalid_title": return "Der Modellanbieter hat einen fehlerhaften Titel geliefert. Der bisherige Titel bleibt erhalten. Bitte erneut versuchen.";
                case "provider_failed": return "Der Modellanbieter konnte die Titelanfrage nicht abschließen. Bitte Anbieter und Modell prüfen. Der bisherige Titel bleibt erhalten.";
                default: return "Die Titelanfrage wurde nicht bestätigt. Bitte den Verlauf neu laden und erneut versuchen. Der bisherige Titel bleibt erhalten.";
            }
        }
    }
}
