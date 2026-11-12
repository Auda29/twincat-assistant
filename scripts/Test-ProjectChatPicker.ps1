param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'local-data/sidebar-views/component'))
$ErrorActionPreference = 'Stop'

# UI fixture for the native project chat bar. It compiles only the WPF
# component and the shared palette with a small harness, places the real bar
# beside (docked) or above (overlay) a synthetic conversation exactly as the
# tool window does, checks states, actions, keyboard handling and late
# completions, and renders light and dark states at 340, 390, 768 and 1100 px
# as PNG files. No IDE, WebView, chat service, profile or session data is
# involved; all chats are synthetic. The harness window is nearly transparent
# and never activated.
$root = Split-Path $PSScriptRoot -Parent
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuildRoot = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
$csc = Join-Path $msbuildRoot 'MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw 'Roslyn csc from Visual Studio 2022 (Build Tools) is required.' }
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$refs = Join-Path $packages 'microsoft.netframework.referenceassemblies.net472\1.0.3\build\.NETFramework\v4.7.2'
if (-not (Test-Path -LiteralPath $refs)) { throw 'Restore the native host once so the net472 reference assemblies are cached.' }

[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$buildDirectory = Join-Path $OutputDirectory 'harness'
[IO.Directory]::CreateDirectory($buildDirectory) | Out-Null
$harness = Join-Path $buildDirectory 'SidebarHarness.cs'
$exe = Join-Path $buildDirectory 'SidebarHarness.exe'
$harnessSource = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TwinCatAssistant.Native
{
    // Fixture copy of the palette record from native/Shared/ChatContextBinding.cs,
    // whose other types need the IDE reader stack.
    public sealed class ChatTheme
    {
        public bool dark { get; set; }
        public string background { get; set; }
        public string foreground { get; set; }
        public string muted { get; set; }
        public string border { get; set; }
        public string surface { get; set; }
        public string primary { get; set; }
        public string primaryForeground { get; set; }
        public string error { get; set; }
        public string warning { get; set; }
        public string success { get; set; }
    }

    internal static class SidebarHarness
    {
        // Same geometry as AssistantControl (ProjectChatNavigation.cs).
        const double DockFrom = 760, DockedWidth = 272, OverlayMax = 304, OverlayGap = 44;
        static string _out;
        static int _failures, _checks;
        static Window _window;
        static UserControl _host;
        static Button _anchor;
        static Grid _area;
        static ColumnDefinition _column;
        static Border _slot, _scrim;
        static ResourceDictionary _theme;
        static ProjectChatPicker _picker;
        static readonly List<string> _calls = new List<string>();
        static readonly List<bool> _closes = new List<bool>();
        static TaskCompletionSource<bool> _gate;
        static string _palette = "light";

        [STAThread]
        static int Main(string[] args)
        {
            _out = args[0];
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { Run(); }
            catch (Exception error) { Console.WriteLine("FAIL harness: " + error); _failures++; }
            if (_window != null) _window.Close();
            Console.WriteLine(_failures == 0 ? _checks + " chat bar checks passed." : _failures + " chat bar check(s) failed.");
            return _failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string name) { _checks++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) _failures++; }
        static T Field<T>(string name) { return (T)typeof(ProjectChatPicker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_picker); }
        static void SetField(string name, object value) { typeof(ProjectChatPicker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_picker, value); }
        static object Call(string name, params object[] args) { return typeof(ProjectChatPicker).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_picker, args); }

        static void Pump()
        {
            for (var i = 0; i < 3; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }
        }

        static Task Gate(string call)
        {
            _calls.Add(call);
            _gate = new TaskCompletionSource<bool>();
            return _gate.Task;
        }

        static void Run()
        {
            _theme = AssistantTheme.CreateResources();
            AssistantTheme.Apply(_theme, AssistantTheme.Light);
            BuildHost();
            _picker = new ProjectChatPicker(_anchor, _theme);
            _slot.Child = _picker.View;
            _picker.Closed = byUser => { _closes.Add(byUser); Layout(); };
            _picker.PinnedChanged = pinned => Layout();
            _picker.SelectAsync = id => Gate("select:" + id);
            _picker.RenameAsync = (id, title) => Gate("rename:" + id + ":" + title);
            _picker.ArchiveAsync = id => Gate("archive:" + id);
            _picker.RestoreAsync = id => Gate("restore:" + id);
            _picker.RetryAsync = () => Gate("retry");
            _picker.NewAsync = () => Gate("new");
            _picker.GenerateTitleAsync = id => Gate("generate:" + id);
            SetWidth(1100);

            Check(!_picker.IsOpen, "bar starts closed");
            _picker.Show("Ignored", null, Items());
            _picker.ShowError("ignored");
            Check(!_picker.IsOpen && !_picker.SetSelected("s1"), "Show, ShowError and SetSelected never open a closed bar");

            _picker.ShowLoading("Anlage_Linie3");
            Layout();
            Pump();
            Check(_picker.IsOpen && _picker.Docked, "ShowLoading opens the bar, docked on a wide tool window");
            Check(Field<Border>("_state").Visibility == Visibility.Visible && Field<ScrollViewer>("_scroll").Visibility == Visibility.Collapsed, "loading state replaces the list");
            Check(Field<TextBlock>("_title").Text == "Anlage_Linie3" && !Field<Button>("_newButton").IsEnabled, "header names the project; Neuer Chat waits for the list");
            Shots("loading", 390, 1100);

            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();
            var targets = Targets();
            Check(targets.Count == 9 && targets.Last() == "\0done", "open chats are listed, then the collapsed Erledigte Chats toggle (" + targets.Count + ")");
            Check(targets.First() == "s1", "newest chat first");
            Check(!targets.Contains("a1") && !targets.Contains("a2"), "Erledigte Chats starts collapsed");
            var toggle = Primary("\0done");
            Check(AutomationProperties.GetName(toggle) == "Erledigte Chats, 2, eingeklappt", "done section names its count and state for assistive technology");
            var open = Primary("s2");
            Check(AutomationProperties.GetName(open).Contains(", geöffnet") && HasPlate(open), "the open chat is named and highlighted");
            Check(!HasPlate(Primary("s3")), "other chats are not highlighted");
            Check(MetaText("s1").Contains("Antwort läuft") && AutomationProperties.GetName(Primary("s1")).Contains("Antwort läuft"), "a running answer is visible on its row");
            Check(MetaText("s8") == "Keine Aktivität bekannt", "rows without activity say so");
            foreach (var width in new[] { 340, 390, 768, 1100 }) Shot("list", width);

            // Docked geometry: the conversation keeps a readable width.
            SetWidth(768);
            Check(_picker.Docked && Math.Abs(_slot.ActualWidth - DockedWidth) < 1 && ConversationWidth() >= 488, "docked at 768 px with a 272 px bar and at least 488 px allocated to the conversation");
            SetWidth(759);
            Check(!_picker.Docked && _picker.IsOpen && ConversationWidth() >= 758, "below 760 px the open bar becomes an overlay and the conversation keeps its full width");
            SetWidth(390);
            Check(Math.Abs(_slot.ActualWidth - 304) < 1 && _scrim.Visibility == Visibility.Visible, "overlay at 390 px is 304 px wide above a scrim");
            SetWidth(340);
            Check(Math.Abs(_slot.ActualWidth - 296) < 1 && Field<Button>("_newButton").ActualWidth > 200, "overlay at 340 px leaves a visible strip of the conversation");
            SetWidth(1100);
            Check(_picker.Docked && _scrim.Visibility == Visibility.Collapsed, "widening returns the open bar to the docked column");

            // Pinned bars remain open below the normal responsive breakpoint.
            SetWidth(650);
            Field<Button>("_pinButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(_picker.Pinned && _picker.Docked && ConversationWidth() >= 376, "pin reserves a sidebar column below the normal breakpoint");
            Check(AutomationProperties.GetName(Field<Button>("_pinButton")) == "Chatleiste lösen", "pin action announces its current state");
            Shot("pinned", 650);
            SetWidth(390);
            Call("SelectEntryAsync", "s2"); Pump();
            Check(_picker.Pinned && !_picker.Docked && _picker.IsOpen, "selecting the current chat keeps a pinned narrow overlay open");
            _anchor.Visibility = Visibility.Collapsed; Pump();
            Check(_picker.IsOpen, "a pinned list survives temporary host or anchor invisibility");
            _anchor.Visibility = Visibility.Visible; Pump();
            Key(Field<TextBox>("_search"), System.Windows.Input.Key.Escape);
            Check(_picker.IsOpen, "Escape leaves a pinned narrow sidebar open");
            Field<Button>("_pinButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(!_picker.Pinned && AutomationProperties.GetName(Field<Button>("_pinButton")) == "Chatleiste anheften", "unpin restores the transient overlay behavior");
            _anchor.Visibility = Visibility.Collapsed; Pump();
            Check(!_picker.IsOpen, "an unpinned list still closes when its anchor disappears");
            _anchor.Visibility = Visibility.Visible; Pump();
            _picker.ShowLoading("Anlage_Linie3", false, true);
            _picker.Show("Anlage_Linie3", "s2", Items());
            _closes.Clear(); // The visibility regression above deliberately closed one unpinned session.
            SetWidth(1100);

            // Live refresh: identical lists keep the rows, changes update them in place.
            var before = Primary("s3");
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();
            Check(ReferenceEquals(before, Primary("s3")), "an identical refresh does not rebuild rows (hover, focus and scroll stay)");
            var search = Field<TextBox>("_search");
            search.Text = "e";
            Pump();
            var scroll = Field<ScrollViewer>("_scroll");
            SetWidth(1100, 420);
            scroll.ScrollToVerticalOffset(60);
            Pump();
            var offset = scroll.VerticalOffset;
            _picker.Show("Anlage_Linie3", "s2", ItemsWith("s3", "PID-Regler Temperaturzone 2 abstimmen (neu)", busy: true));
            Pump();
            Check(Title("s3") == "PID-Regler Temperaturzone 2 abstimmen (neu)" && MetaText("s3").Contains("Antwort läuft"), "a new title and answer state appear on refresh");
            Check(offset > 0 && Math.Abs(scroll.VerticalOffset - offset) < 1 && search.Text == "e", "refresh keeps the scroll position and the search text (" + offset + " -> " + scroll.VerticalOffset + ")");
            search.Text = "";
            SetWidth(1100);
            Check(_picker.SetSelected("s3") && HasPlate(Primary("s3")) && !HasPlate(Primary("s2")), "SetSelected moves the highlight to the newly opened chat");
            Check(!_picker.SetSelected("unknown"), "SetSelected reports a chat the list does not know yet");
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();

            // Search: local, case and accent insensitive, multiple terms; done chats count separately.
            search.Text = "regler";
            Pump();
            Check(Targets().SequenceEqual(new[] { "s3", "s5" }), "search filters titles case-insensitively");
            search.Text = "uberlauf";
            Pump();
            Check(Targets().SequenceEqual(new[] { "s4" }), "search ignores umlaut accents");
            search.Text = "inbetriebnahme";
            Pump();
            Check(Targets().SequenceEqual(new[] { "\0done" }) && AutomationProperties.GetName(Primary("\0done")).Contains("1 Treffer"), "a match among done chats shows in their section count");
            Shot("search-done-only", 390);
            search.Text = "zzz";
            Pump();
            Check(Targets().Count == 0 && Field<Border>("_state").Visibility == Visibility.Visible, "no-match state");
            Shot("nomatch", 390);
            search.Text = "";
            Pump();

            // Row menu: labels, busy chats cannot be marked done.
            Call("OpenMenu", "s2");
            Pump();
            Check(Field<Canvas>("_overlay").Visibility == Visibility.Visible && MenuButton("Umbenennen") != null && MenuButton("Titel automatisch generieren") != null
                && MenuButton("Als erledigt markieren") != null && MenuButton("Archivieren") == null, "row menu offers rename, title generation and Als erledigt markieren");
            Shot("menu", 390);
            Shot("menu", 1100);
            Call("CloseMenu", false);
            Call("OpenMenu", "s1");
            Pump();
            Check(!MenuButton("Als erledigt markieren").IsEnabled && !MenuButton("Titel automatisch generieren").IsEnabled, "a chat with a running answer can be neither marked done nor retitled");
            Call("CloseMenu", false);
            Call("ArchiveEntryAsync", "s1");
            Pump();
            Check(!_calls.Contains("archive:s1"), "marking a busy chat done is refused even without the menu");

            // Rename: inline editor, success applies the confirmed title.
            Call("BeginRename", "s3");
            Pump();
            var renameBox = Field<TextBox>("_renameBox");
            Check(renameBox.Text == "PID-Regler Temperaturzone 2 abstimmen", "rename editor starts with the current title");
            renameBox.Text = "";
            Call("CommitRenameAsync");
            Pump();
            Check(Field<string>("_renameError") == "Bitte einen Titel eingeben." && _calls.Count == 0, "empty title is rejected locally");
            renameBox.Text = "Temperaturregler Zone 2";
            _picker.Show("Anlage_Linie3", "s2", ItemsWith("s1", "Warum springt MAIN.fbAxis in 0x4260?", busy: false));
            Pump();
            Check(Field<string>("_renameId") == "s3" && renameBox.Text == "Temperaturregler Zone 2", "a refresh while renaming keeps the editor and its text");
            Shot("rename", 390);
            Call("CommitRenameAsync");
            Pump();
            Check(_calls.LastOrDefault() == "rename:s3:Temperaturregler Zone 2", "rename delegate receives id and trimmed title");
            Check(!Field<Button>("_renameSave").IsEnabled, "rename buttons disabled while saving");
            _gate.SetResult(true);
            Pump();
            Check(Field<string>("_renameId") == null && Title("s3") == "Temperaturregler Zone 2", "confirmed rename updates the row");
            Call("BeginRename", "s4");
            Pump();
            renameBox.Text = "Neu";
            Call("CommitRenameAsync");
            Pump();
            _gate.SetException(new InvalidOperationException("Titel konnte nicht gespeichert werden."));
            Pump();
            Check(Field<string>("_renameId") == "s4" && (Field<string>("_renameError") ?? "").Contains("Titel konnte nicht gespeichert werden.") && Title("s4") != "Neu", "failed rename stays editable with the error");
            Call("CancelRename");
            Pump();
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();

            // Title generation: explicit, pending row, host refresh decides the title.
            Call("OpenMenu", "s4");
            Pump();
            MenuButton("Titel automatisch generieren").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Check(_calls.Last() == "generate:s4" && Pending("s4") == "generate" && More("s4") == null && Field<string>("_menuId") == null, "explicit click generates; the row shows progress instead of actions");
            Check(MetaText("s4").Contains("Titel wird generiert"), "generation progress is named on the row");
            Shot("generating", 390);
            Shot("generating", 1100);
            var generating = _gate;
            var count = _calls.Count;
            Call("GenerateTitleEntryAsync", "s4");
            Call("BeginRename", "s4");
            Call("ArchiveEntryAsync", "s4");
            Call("SelectEntryAsync", "s4");
            Pump();
            Check(_calls.Count == count, "pending generation blocks competing row actions");
            _picker.Show("Anlage_Linie3", "s2", ItemsWith("s4", "Zählerüberlauf in GVL_Zaehler beheben"));
            Pump();
            Check(Pending("s4") == "generate" && Title("s4") == "Zählerüberlauf in GVL_Zaehler beheben", "host refresh during generation keeps the pending row");
            generating.SetResult(true);
            Pump();
            Check(Pending("s4") == null && More("s4") != null && Field<string>("_actionError") == null, "completed generation frees the row");
            Call("GenerateTitleEntryAsync", "s5");
            Pump();
            _gate.SetException(new TimeoutException("internal"));
            Pump();
            Check(Field<string>("_actionError") == "Der Titel konnte nicht generiert werden." && Pending("s5") == null, "non-host failures show the neutral message");
            const string TitleFallback = "Der Titel konnte nicht generiert werden.";
            foreach (var hostMessage in new[] { TitleFallback, TitleFallback + " Das Modell hat keine Antwort geliefert.", "der titel konnte nicht generiert werden: Zeitüberschreitung." })
            {
                Call("GenerateTitleEntryAsync", "s5");
                Pump();
                _gate.SetException(new AggregateException(new InvalidOperationException(hostMessage)));
                Pump();
                Check(Field<string>("_actionError") == hostMessage.Trim() && Field<TextBlock>("_bannerText").Text == hostMessage.Trim(), "fallback sentence appears once: " + hostMessage);
            }
            Shot("action-error", 390);
            Call("GenerateTitleEntryAsync", "s5");
            Pump();
            _gate.SetException(new OperationCanceledException(TitleFallback));
            Pump();
            Check(Field<string>("_actionError") == null && Pending("s5") == null, "cancelled generation stays silent");

            // Unicode titles render exactly as the host sends them.
            var unicode = new[] { "Größe prüfen – Rückzug", "Ä Ö Ü ä ö ü ß ẞ – „Zitat“ · µs ± 5 % · Ø 12 mm · 25 °C", "Messwert 𝛼 ≤ 0,5 ✓ 東京 Linie №3 🚀" };
            var unicodeItems = Items().ToList();
            for (var i = 0; i < unicode.Length; i++) unicodeItems.Insert(0, Item("u" + i, unicode[i], 0.5 + i, false, false));
            _picker.Show("Anlage_Linie3", "u0", unicodeItems.ToArray());
            Pump();
            for (var i = 0; i < unicode.Length; i++)
            {
                var blocks = Descendants<TextBlock>(Field<StackPanel>("_list")).Where(block => block.Text == unicode[i]).ToList();
                Check(Title("u" + i) == unicode[i] && blocks.Count == 1 && blocks[0].ActualWidth > 0, "unicode title renders unchanged: " + unicode[i]);
            }
            Shot("unicode", 390);
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();

            // Als erledigt markieren: failure keeps the row, success moves it to the section.
            Call("ArchiveEntryAsync", "s6");
            Pump();
            Check(MetaText("s6").Contains("Wird als erledigt markiert"), "marking done shows its progress on the row");
            _gate.SetException(new TimeoutException("internal"));
            Pump();
            Check(Field<string>("_actionError") == "Als erledigt markieren fehlgeschlagen." && Targets().Contains("s6"), "failed marking keeps the row with a neutral message");
            Call("ArchiveEntryAsync", "s6");
            Pump();
            _gate.SetException(new InvalidOperationException("In diesem Chat läuft noch eine Antwort."));
            Pump();
            Check(Field<string>("_actionError") == "Als erledigt markieren fehlgeschlagen. In diesem Chat läuft noch eine Antwort.", "host refusal for a running answer is shown");
            Call("ArchiveEntryAsync", "s6");
            Pump();
            _gate.SetResult(true);
            Pump();
            Check(!Targets().Contains("s6") && AutomationProperties.GetName(Primary("\0done")).StartsWith("Erledigte Chats, 3"), "a chat marked done leaves the open list and joins Erledigte Chats");

            // Expanding the section, reopening.
            Primary("\0done").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Check(Targets().Skip(Targets().IndexOf("\0done") + 1).SequenceEqual(new[] { "s6", "a1", "a2" }), "expanded section lists the done chats");
            var reopen = Primary("a1") as Button;
            Check(reopen != null && (string)reopen.Content == "Wieder öffnen" && AutomationProperties.GetName(reopen) == "„Alte Inbetriebnahme Notizen“ wieder öffnen", "done chats offer Wieder öffnen");
            Check(MetaText("a1").Length > 0 && Field<StackPanel>("_list").Children.Count > 0, "done chats keep their activity time");
            foreach (var width in new[] { 390, 1100 }) Shot("done-expanded", width);
            Call("RestoreEntryAsync", "a1");
            Pump();
            Check((string)((Button)Primary("a1")).Content == "Wird geöffnet …" && !Primary("a1").IsEnabled, "reopening shows progress and blocks a second click");
            Shot("reopening", 390);
            _gate.SetException(new InvalidOperationException("Wieder öffnen fehlgeschlagen. Der Verlauf ist nicht erreichbar."));
            Pump();
            Check(Field<string>("_actionError") == "Wieder öffnen fehlgeschlagen. Der Verlauf ist nicht erreichbar." && Primary("a1").IsEnabled, "failed reopening keeps the done row usable");
            Call("RestoreEntryAsync", "a1");
            Pump();
            _gate.SetResult(true);
            Pump();
            Check(Targets().IndexOf("a1") >= 0 && Targets().IndexOf("a1") < Targets().IndexOf("\0done") && ((Primary("a1") as Button)?.Content is StackPanel), "a reopened chat is an open chat again");
            Key(Primary("\0done"), System.Windows.Input.Key.Left);
            Pump();
            Check(!Targets().Contains("a2"), "Left arrow on the section toggle collapses it");
            Key(Primary("\0done"), System.Windows.Input.Key.Right);
            Pump();
            Check(Targets().Contains("a2"), "Right arrow expands it again");

            // All chats done: an inline note, the section stays reachable.
            var allDone = Items().Select(item => { item.Archived = true; item.Busy = false; return item; }).ToArray();
            _picker.Show("Anlage_Linie3", null, allDone);
            Pump();
            Check(Targets().First() == "\0done" && Descendants<TextBlock>(Field<StackPanel>("_list")).Any(block => block.Text.StartsWith("Keine offenen Chats")), "only done chats: an inline note above the section");
            Shot("all-done", 390);
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();

            // Pinning controls lifetime independently of the responsive layout.
            _picker.Pinned = true;
            // Selection: pinned stays open; failures are reported.
            Call("SelectEntryAsync", "s5");
            Pump();
            Check(MetaText("s5").Contains("Wird geöffnet"), "opening shows progress on the row");
            _gate.SetException(new InvalidOperationException("Der Chat gehört zu einem anderen Projekt."));
            Pump();
            Check(_picker.IsOpen && (Field<string>("_actionError") ?? "").Contains("anderen Projekt"), "failed selection keeps the bar open with the message");
            Call("SelectEntryAsync", "s5");
            Pump();
            _gate.SetResult(true);
            Pump();
            Check(_picker.IsOpen && _closes.Count == 0 && Pending("s5") == null, "pinned: a selected chat keeps the bar open");
            Call("CreateChatAsync");
            Pump();
            Check(Field<TextBlock>("_newLabel").Text == "Wird erstellt …" && !Field<Button>("_newButton").IsEnabled, "Neuer Chat shows its progress");
            Shot("creating", 1100);
            _gate.SetResult(true);
            Pump();
            Check(_picker.IsOpen && _closes.Count == 0, "pinned: Neuer Chat keeps the bar open");
            Key(Field<TextBox>("_search"), System.Windows.Input.Key.Escape);
            Pump();
            Check(_picker.IsOpen, "pinned: Escape does not close the bar");
            search.Text = "zone";
            Key(Field<TextBox>("_search"), System.Windows.Input.Key.Escape);
            Pump();
            Check(_picker.IsOpen && search.Text == "", "Escape in the search box clears the search first");

            _picker.Pinned = false;
            SetWidth(390);
            Check(!_picker.Docked && KeyboardNavigation.GetTabNavigation(_picker.View) == KeyboardNavigationMode.Cycle, "overlay keeps Tab inside the bar");
            Call("SelectEntryAsync", "s4");
            Pump();
            _gate.SetResult(true);
            Pump();
            Check(!_picker.IsOpen && _closes.LastOrDefault() == false && _slot.Visibility == Visibility.Collapsed, "overlay: a selected chat closes the bar (not a user close)");
            Reopen("s4");
            Call("SelectEntryAsync", "s4");
            Pump();
            Check(!_picker.IsOpen && _closes.Count == 2, "overlay: choosing the already open chat closes the bar without a delegate call");
            Reopen("s4");
            Call("CreateChatAsync");
            Pump();
            _gate.SetResult(true);
            Pump();
            Check(!_picker.IsOpen, "overlay: Neuer Chat closes the bar after the chat exists");
            Reopen("s4");
            Call("OpenMenu", "s3");
            Pump();
            Key(MenuButton("Umbenennen"), System.Windows.Input.Key.Escape);
            Pump();
            Check(_picker.IsOpen && Field<string>("_menuId") == null, "Escape closes the row menu first");
            Key(Field<TextBox>("_search"), System.Windows.Input.Key.Escape);
            Pump();
            Check(!_picker.IsOpen && _closes.LastOrDefault() == true, "overlay: Escape closes the bar as a user close");
            Reopen("s4");
            Field<Button>("_collapseButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Check(!_picker.IsOpen && _closes.LastOrDefault() == true, "the collapse button closes the bar as a user close");
            SetWidth(1100);
            Reopen("s4");
            Check(_picker.Docked && !_picker.Pinned, "wide geometry does not imply pinning");
            Call("SelectEntryAsync", "s4"); Pump();
            Check(!_picker.IsOpen, "unpinned wide bar closes when choosing the current chat");
            Reopen("s4");
            Call("SelectEntryAsync", "s5"); Pump(); _gate.SetResult(true); Pump();
            Check(!_picker.IsOpen, "unpinned wide bar closes after an asynchronous chat selection");
            Reopen("s4");
            Call("CreateChatAsync"); Pump(); _gate.SetResult(true); Pump();
            Check(!_picker.IsOpen, "unpinned wide bar closes after creating a chat");
            Reopen("s4");
            Key(Field<TextBox>("_search"), System.Windows.Input.Key.Escape); Pump();
            Check(!_picker.IsOpen, "unpinned wide bar closes on Escape");

            SetWidth(1100);

            // Late completions: a closed bar or a new session is never changed.
            Reopen("s2");
            Call("SelectEntryAsync", "s4");
            Pump();
            var stale = _gate;
            _picker.Close();
            stale.SetException(new InvalidOperationException("late"));
            Pump();
            _picker.ShowError("late error");
            _picker.Show("Anlage_Linie3", "s2", Items());
            Check(!_picker.IsOpen, "late failure, Show and ShowError after closing are ignored");
            Reopen("s2");
            Call("GenerateTitleEntryAsync", "s3");
            Pump();
            var lateGenerate = _gate;
            // Same project name, another PLC project (other solution): the host starts a new session.
            _picker.ShowLoading("Anlage_Linie3", false, true);
            _picker.Show("Anlage_Linie3", null, new[] { Item("p1", "Achsen referenzieren", 5, false, false), Item("s3", "Gleiche ID im gleichnamigen Projekt", 9, false, false) });
            Pump();
            Check(Pending("s3") == null && !Targets().Contains("s2") && Title("s3") == "Gleiche ID im gleichnamigen Projekt", "a new session for a same-named project drops the pending work and the previous rows");
            lateGenerate.SetException(new InvalidOperationException("late"));
            Pump();
            Check(Field<string>("_actionError") == null && Pending("s3") == null, "late generation failure from the previous project is ignored");
            Call("ArchiveEntryAsync", "p1");
            Pump();
            var old = _gate;
            _picker.ShowLoading("Pruefstand");
            _picker.Show("Pruefstand", null, new[] { Item("p1", "Gleiche ID, anderes Projekt", 9, false, false) });
            Pump();
            old.SetResult(true);
            Pump();
            Check(Targets().Contains("p1") && Field<TextBlock>("_title").Text == "Pruefstand", "a different project name starts a new session; old completions are ignored");

            // Load error and retry.
            _picker.ShowError("Der lokale Chat-Dienst antwortet nicht.");
            Pump();
            Check(Field<Border>("_state").Visibility == Visibility.Visible && !Field<Border>("_searchFrame").IsEnabled, "error state with disabled search");
            Shots("error", 390, 1100);
            Call("RetryLoadAsync");
            Pump();
            Check(_calls.LastOrDefault() == "retry", "retry delegate called");
            _gate.SetException(new InvalidOperationException("Projekt nicht eindeutig."));
            Pump();
            Check((Field<string>("_errorMessage") ?? "").Contains("Projekt nicht eindeutig."), "failed retry stays in the error state");

            // Empty project and the general history.
            _picker.Show("Pruefstand", null, new ProjectChatPickerItem[0]);
            Pump();
            Check(Targets().Count == 0 && Field<Border>("_state").Visibility == Visibility.Visible, "empty state");
            Shots("empty", 340, 768);
            _picker.General = true;
            _picker.BackAsync = () => { _calls.Add("back"); return Task.CompletedTask; };
            _picker.ShowLoading("Allgemeine Chats", false, true);
            _picker.Show("Allgemeine Chats", "g1", new[] { Item("g1", "Allgemeine Frage zu IEC 61131-3", 3, false, false), Item("g2", "Vergleich ST und CFC", 60 * 30, false, false) });
            Pump();
            Check(Field<Button>("_backButton").Visibility == Visibility.Visible && (string)Field<Button>("_newButton").ToolTip == "Neuer allgemeiner Chat ohne Projektkontext", "general history: own scope wording and a way back to the project chats");
            Shot("general", 1100);
            Field<Button>("_backButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Check(_calls.Last() == "back", "back button calls the host");
            _picker.General = false;
            _picker.BackAsync = null;

            // Dark palette follows the shared dictionary live.
            _palette = "dark";
            AssistantTheme.Apply(_theme, AssistantTheme.Dark);
            _picker.ShowLoading("Anlage_Linie3", false, true);
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();
            Check(AssistantTheme.IsDark(((Border)_picker.View).Background), "dark palette reaches the bar");
            foreach (var width in new[] { 340, 390, 768, 1100 }) Shot("list", width);
            Call("OpenMenu", "s3");
            Pump();
            Shot("menu", 390);
            Call("CloseMenu", false);
            Call("BeginRename", "s3");
            Pump();
            Shot("rename", 390);
            Call("CancelRename");
            Primary("\0done").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Shots("done-expanded", 390, 1100);
            Call("GenerateTitleEntryAsync", "s4");
            Pump();
            Shot("generating", 768);
            _gate.SetResult(true);
            Pump();
            _picker.ShowError("Der lokale Chat-Dienst antwortet nicht.");
            Pump();
            Shot("error", 390);
            _picker.ShowLoading("Anlage_Linie3");
            Pump();
            Shot("loading", 1100);
            _picker.Show("Pruefstand", null, new ProjectChatPickerItem[0]);
            Pump();
            Shot("empty", 390);

            Check(ProjectChatPicker.FormatUpdated(0, DateTime.Now) == "", "unknown activity has no time");
            var now = new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Local);
            Check(ProjectChatPicker.FormatUpdated(Millis(now.AddMinutes(-12)), now) == "vor 12 Min.", "minutes");
            Check(ProjectChatPicker.FormatUpdated(Millis(now.AddDays(-1).Date.AddHours(9)), now) == "gestern, 09:00", "yesterday");
            Check(ProjectChatPicker.FormatUpdated(Millis(new DateTime(2025, 3, 4, 8, 0, 0, DateTimeKind.Local)), now) == "04.03.2025", "older date");

            HostText.SetLanguage("en");
            _picker.ShowLoading("PLC_SYNTHETIC", false, true);
            _picker.Show("PLC_SYNTHETIC", "s1", new[] { Item("s1", "Gespeichert", 12, false, false) });
            _picker.RefreshLanguage(); Pump();
            Check((string)Field<Button>("_newButton").ToolTip == "New chat in this project", "English tooltips update in the existing picker");
            Check(Descendants<TextBlock>(_picker.View).Any(text => text.Text == "Gespeichert"), "a user title equal to a German UI label is not translated");
            Check(ProjectChatPicker.FormatUpdated(Millis(now.AddMinutes(-12)), now) == "12 min ago", "English relative time");
            Shot("english", 390);
            Call("OpenMenu", "s1"); Pump();
            Check(MenuButton("Rename") != null, "English row menu");
            Call("CloseMenu", false);
            HostText.SetLanguage("de"); _picker.RefreshLanguage(); Pump();
            Check((string)Field<Button>("_newButton").ToolTip == "Neuer Chat in diesem Projekt", "German round trip preserves the picker");
            Check(Descendants<TextBlock>(_picker.View).Any(text => text.Text == "Gespeichert"), "German round trip preserves the user title");

            _picker.Dispose();
            _picker.ShowLoading("Anlage_Linie3");
            Check(!_picker.IsOpen && _slot.Child == null, "disposed bar stays closed and leaves the layout");
            Check(_picker.GenerateTitleAsync == null && _picker.RenameAsync == null && _picker.Closed == null, "dispose clears the delegates");
        }

        static void Reopen(string selected)
        {
            _picker.ShowLoading("Anlage_Linie3");
            _picker.Show("Anlage_Linie3", selected, Items());
            Layout();
            Pump();
        }

        static string Pending(string id)
        {
            string pending;
            return Field<Dictionary<string, string>>("_pending").TryGetValue(id, out pending) ? pending : null;
        }

        static object Target(string id)
        {
            var list = (System.Collections.IList)Field<object>("_targets");
            return list.Cast<object>().FirstOrDefault(t => (string)t.GetType().GetField("Id").GetValue(t) == id);
        }
        static Control Primary(string id) { var t = Target(id); return t == null ? null : (Control)t.GetType().GetField("Primary").GetValue(t); }
        static Button More(string id) { var t = Target(id); return t == null ? null : (Button)t.GetType().GetField("More").GetValue(t); }

        // The highlight plate sits under the row's button in the same grid.
        static bool HasPlate(Control primary)
        {
            var row = primary == null ? null : VisualTreeHelper.GetParent(primary) as Grid;
            return row != null && row.Children.OfType<Border>().Any(border => Math.Abs(border.Opacity - 0.13) < 0.001);
        }

        static string MetaText(string id)
        {
            var primary = Primary(id);
            var row = primary == null ? null : VisualTreeHelper.GetParent(primary) as Grid;
            if (row == null) return "";
            var blocks = Descendants<TextBlock>(row).ToList();
            var meta = blocks.FirstOrDefault(block => Math.Abs(block.FontSize - 11) < 0.01);
            return meta == null ? "" : new System.Windows.Documents.TextRange(meta.ContentStart, meta.ContentEnd).Text;
        }

        static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        static Button MenuButton(string name)
        {
            return Field<StackPanel>("_menuItems").Children.OfType<Button>().FirstOrDefault(button => AutomationProperties.GetName(button) == name);
        }

        static ProjectChatPickerItem[] ItemsWith(string id, string title, bool? busy = null)
        {
            var items = Items();
            var item = items.First(entry => entry.Id == id);
            item.Title = title;
            if (busy.HasValue) item.Busy = busy.Value;
            return items;
        }

        static long Millis(DateTime local) { return new DateTimeOffset(local).ToUnixTimeMilliseconds(); }

        static void Key(UIElement target, Key key)
        {
            var source = PresentationSource.FromVisual(target);
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }

        static List<string> Targets()
        {
            var list = (System.Collections.IList)Field<object>("_targets");
            return list.Cast<object>().Select(t => (string)t.GetType().GetField("Id").GetValue(t)).ToList();
        }

        static string Title(string id)
        {
            var entries = (System.Collections.IList)Field<object>("_entries");
            var entry = entries.Cast<object>().First(e => (string)e.GetType().GetField("Id").GetValue(e) == id);
            return (string)entry.GetType().GetField("Title").GetValue(entry);
        }

        static ProjectChatPickerItem Item(string id, string title, double minutesAgo, bool busy, bool archived)
        {
            return new ProjectChatPickerItem { Id = id, Title = title, Busy = busy, Archived = archived,
                Updated = minutesAgo < 0 ? 0 : DateTimeOffset.Now.AddMinutes(-minutesAgo).ToUnixTimeMilliseconds() };
        }

        static ProjectChatPickerItem[] Items()
        {
            return new[] {
                Item("s2", "FB_Foerderband: Anlauframpe und Notaus-Verriegelung prüfen", 7, false, false),
                Item("s1", "Warum springt MAIN.fbAxis nach dem Reset in den Fehlerzustand 0x4260?", 1, true, false),
                Item("s3", "PID-Regler Temperaturzone 2 abstimmen", 95, false, false),
                Item("s4", "Überlauf in GVL_Zaehler bei 32-Bit-Zählern", 60 * 26, false, false),
                Item("s5", "regler Kaskade – Stellgrößenbegrenzung", 60 * 24 * 3, false, false),
                Item("s6", "", 60 * 24 * 12, false, false),
                Item("s7", "Sehr langer Titel einer Unterhaltung über die Umstellung der kompletten Schrittkette von SFC auf eine Zustandsmaschine in strukturiertem Text mit Enumerationen", 60 * 24 * 40, false, false),
                Item("s8", "Kommunikation EtherCAT-Slave", -1, false, false),
                Item("a1", "Alte Inbetriebnahme Notizen", 60 * 24 * 90, false, true),
                Item("a2", "Test Achsfreigabe", 60 * 24 * 400, false, true),
            };
        }

        // ---- Synthetic tool window: context bar, chat area, conversation ----

        static FrameworkElement Conversation() { return (FrameworkElement)_area.Children[0]; }
        // The synthetic conversation includes content gutters; measure its
        // allocated column, as the real WebView fixture measures its surface.
        static double ConversationWidth() { var view = Conversation(); return view.ActualWidth + view.Margin.Left + view.Margin.Right; }

        static void BuildHost()
        {
            _host = new UserControl { Resources = _theme, Width = 1100, Height = 640 };
            _host.SetResourceReference(Control.BackgroundProperty, "Tc.Background");
            _host.SetResourceReference(Control.ForegroundProperty, "Tc.Foreground");
            var strip = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 6, 8, 6) };
            strip.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 4; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = "MAIN  Anlage_Linie3", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            row.Children.Add(name);
            var column = 1;
            foreach (var icon in new[] { "history", "plus", "attach", "more" })
            {
                var button = new Button { Content = new System.Windows.Shapes.Path { Data = AssistantTheme.Icon(icon), Width = 16, Height = 16, StrokeThickness = 1.6 } };
                button.SetResourceReference(FrameworkElement.StyleProperty, "Tc.IconButton");
                ((System.Windows.Shapes.Path)button.Content).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, icon == "history" ? "Tc.Primary" : "Tc.Muted");
                Grid.SetColumn(button, column++);
                row.Children.Add(button);
                if (_anchor == null) _anchor = button;
            }
            strip.Child = row;
            _area = new Grid();
            _column = new ColumnDefinition { Width = new GridLength(0) };
            _area.ColumnDefinitions.Add(_column);
            _area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var conversation = BuildConversation();
            Grid.SetColumn(conversation, 1);
            _area.Children.Add(conversation);
            _scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x47, 0, 0, 0)), Visibility = Visibility.Collapsed };
            Grid.SetColumnSpan(_scrim, 2);
            _area.Children.Add(_scrim);
            _slot = new Border { Visibility = Visibility.Collapsed };
            _area.Children.Add(_slot);
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(strip, Dock.Top);
            dock.Children.Add(strip);
            dock.Children.Add(_area);
            _host.Content = dock;
            var area = SystemParameters.WorkArea;
            _window = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Opacity = 0.01,
                ShowActivated = false, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight, Content = _host,
                Left = area.Right - 1160, Top = area.Bottom - 700 };
            _window.Show();
        }

        static UIElement BuildConversation()
        {
            var panel = new DockPanel { LastChildFill = false, Margin = new Thickness(18, 14, 18, 14) };
            var composer = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 10, 0, 0) };
            composer.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            composer.SetResourceReference(Border.BackgroundProperty, "Tc.Surface");
            var hint = new TextBlock { Text = "Nachricht an den Assistant …", FontSize = 13 };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            composer.Child = hint;
            DockPanel.SetDock(composer, Dock.Bottom);
            panel.Children.Add(composer);
            foreach (var line in new[] {
                "Warum geht die Anlauframpe von FB_Foerderband nach einem Notaus nicht wieder hoch?",
                "Nach dem Notaus bleibt bEnable gesetzt, während fbRampe.bReset nur auf der steigenden Flanke ausgewertet wird. Setze bReset im ersten Zyklus nach der Quittierung, dann startet die Rampe bei 0 % und läuft in 2,5 s auf den Sollwert." })
            {
                var bubble = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 10), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
                bubble.SetResourceReference(Border.BackgroundProperty, "Tc.NeutralSurface");
                bubble.Child = new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                DockPanel.SetDock(bubble, Dock.Top);
                panel.Children.Add(bubble);
            }
            return panel;
        }

        // Mirror of AssistantControl.LayoutSidebar for the synthetic window.
        static void Layout()
        {
            var width = _host.Width;
            var docked = width >= DockFrom || _picker?.Pinned == true && width >= DockedWidth + 320;
            if (_picker != null && _picker.Docked != docked) _picker.Docked = docked;
            var open = _picker != null && _picker.IsOpen;
            _slot.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            _scrim.Visibility = open && !docked ? Visibility.Visible : Visibility.Collapsed;
            _column.Width = new GridLength(open && docked ? DockedWidth : 0);
            Grid.SetColumnSpan(_slot, docked ? 1 : 2);
            _slot.HorizontalAlignment = docked ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            _slot.Width = docked ? double.NaN : Math.Max(220, Math.Min(OverlayMax, width - OverlayGap));
            _slot.Effect = docked ? null : new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 0, Opacity = 0.22 };
        }

        static void SetWidth(double width, double height = 640)
        {
            _host.Width = width;
            _host.Height = height;
            Layout();
            _window.UpdateLayout();
            Pump();
        }

        static void Shots(string state, params double[] widths) { foreach (var width in widths) Shot(state, width); }

        static void Shot(string state, double width)
        {
            var oldWidth = _host.Width;
            SetWidth(width);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(_host.ActualWidth), (int)Math.Ceiling(_host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(_host);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(_out, state + "-" + _palette + "-" + width + ".png"))) encoder.Save(file);
            SetWidth(oldWidth);
        }
    }
}
'@
# Windows PowerShell reads this BOM-less UTF-8 script with the ANSI code page,
# which garbles the umlauts of the harness; take its text from a UTF-8 parse.
if ($PSVersionTable.PSEdition -ne 'Core') {
    $ast = [Management.Automation.Language.Parser]::ParseInput([IO.File]::ReadAllText($PSCommandPath, [Text.Encoding]::UTF8), [ref]$null, [ref]$null)
    $harnessSource = $ast.Find({ param($node) $node -is [Management.Automation.Language.StringConstantExpressionAst] -and $node.StringConstantType -eq 'SingleQuotedHereString' }, $true).Value
}
Set-Content -LiteralPath $harness -Encoding UTF8 -Value $harnessSource

$sources = @(
    (Join-Path $root 'native/IdeHost/ProjectChatPicker.cs'),
    (Join-Path $root 'native/IdeHost/AssistantTheme.cs'),
    (Join-Path $root 'native/IdeHost/HostTextBindings.cs'),
    (Join-Path $root 'native/Shared/HostText.cs'),
    (Join-Path $root 'native/Shared/HostStatusText.cs'),
    $harness)
$resourceArguments = @{}
Add-Type -AssemblyName System.Windows.Forms
foreach ($language in @('de','en')) {
    $resx = Join-Path $root ('native/Shared/Localization/Strings' + $(if ($language -eq 'en') {'.en'} else {''}) + '.resx')
    $resource = Join-Path $buildDirectory ("Strings.$language.resources")
    $reader = New-Object System.Resources.ResXResourceReader($resx)
    $writer = New-Object System.Resources.ResourceWriter($resource)
    try { foreach ($entry in $reader.GetEnumerator()) { $writer.AddResource([string]$entry.Key,[string]$entry.Value) }; $writer.Generate() }
    finally { $writer.Dispose(); $reader.Dispose() }
    $resourceArguments[$language] = $resource
}
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xaml.dll', 'WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') |
    ForEach-Object { '/reference:' + (Join-Path $refs $_) }
& $csc /nologo /utf8output /noconfig /nostdlib+ /target:exe /platform:anycpu /langversion:latest /warnaserror+ /nowarn:1701,1702 "/out:$exe" "/resource:$($resourceArguments.de),TwinCatAssistant.Native.Localization.Strings.resources" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Chat bar fixture compilation failed.' }
$satelliteDirectory = Join-Path $buildDirectory 'en'
[IO.Directory]::CreateDirectory($satelliteDirectory) | Out-Null
$satelliteSource = Join-Path $buildDirectory 'EnglishSatellite.cs'
[IO.File]::WriteAllText($satelliteSource, '[assembly: System.Reflection.AssemblyCulture("en")]')
& $csc /nologo /target:library /platform:anycpu "/out:$(Join-Path $satelliteDirectory 'SidebarHarness.resources.dll')" "/resource:$($resourceArguments.en),TwinCatAssistant.Native.Localization.Strings.en.resources" $satelliteSource
if ($LASTEXITCODE -ne 0) { throw 'English chat bar resources failed to compile.' }
& $exe $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Chat bar fixture checks failed.' }
Write-Output "Screenshots: $OutputDirectory"
