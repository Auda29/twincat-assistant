param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'local-data/project-chat-picker'))
$ErrorActionPreference = 'Stop'

# UI fixture for the native project chat picker. It compiles only the WPF
# component and the shared palette with a small harness, opens the real popup
# next to a synthetic context bar, checks state and stale-completion behavior
# and renders light, dark, narrow and wide states as PNG files. No IDE,
# WebView, chat service, profile or session data is involved. The harness
# window is transparent and never activated; the popup is shown briefly.
$root = Split-Path $PSScriptRoot -Parent
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuildRoot = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
$csc = Join-Path $msbuildRoot 'MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw 'Roslyn csc from Visual Studio 2022 (Build Tools) is required.' }
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$refs = Join-Path $packages 'microsoft.netframework.referenceassemblies.net472\1.0.3\build\.NETFramework\v4.7.2'
if (-not (Test-Path -LiteralPath $refs)) { throw 'Restore the native host once so the net472 reference assemblies are cached.' }

[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$harness = Join-Path $OutputDirectory 'PickerHarness.cs'
$exe = Join-Path $OutputDirectory 'PickerHarness.exe'
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

    internal static class PickerHarness
    {
        static string _out;
        static int _failures;
        static Window _window;
        static UserControl _host;
        static Button _anchor;
        static ResourceDictionary _theme;
        static ProjectChatPicker _picker;
        static readonly List<string> _calls = new List<string>();
        static TaskCompletionSource<bool> _gate;

        [STAThread]
        static int Main(string[] args)
        {
            _out = args[0];
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { Run(); }
            catch (Exception error) { Console.WriteLine("FAIL harness: " + error); _failures++; }
            if (_window != null) _window.Close();
            Console.WriteLine(_failures == 0 ? "All picker checks passed." : _failures + " picker check(s) failed.");
            return _failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) _failures++; }
        static T Field<T>(string name) { return (T)typeof(ProjectChatPicker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_picker); }
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
            BuildHost(520);
            _picker = new ProjectChatPicker(_anchor, _theme);
            Field<Popup>("_popup").StaysOpen = true; // never grab the user's mouse
            _picker.SelectAsync = id => Gate("select:" + id);
            _picker.RenameAsync = (id, title) => Gate("rename:" + id + ":" + title);
            _picker.ArchiveAsync = id => Gate("archive:" + id);
            _picker.RestoreAsync = id => Gate("restore:" + id);
            _picker.RetryAsync = () => Gate("retry");
            _picker.NewAsync = () => Gate("new");
            _picker.GenerateTitleAsync = id => Gate("generate:" + id);

            Check(!_picker.IsOpen, "picker starts closed");
            _picker.Show("Ignored", null, Items());
            _picker.ShowError("ignored");
            Check(!_picker.IsOpen, "Show and ShowError never open a closed picker");

            _picker.ShowLoading("Anlage_Linie3");
            Pump();
            Check(_picker.IsOpen, "ShowLoading opens the picker");
            Check(Field<Border>("_state").Visibility == Visibility.Visible && Field<ScrollViewer>("_scroll").Visibility == Visibility.Collapsed, "loading state replaces the list");
            Snap("loading-light", 390);

            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();
            var targets = Targets();
            Check(targets.Count == 8, "only active chats are listed (" + targets.Count + ")");
            Check(targets.First() == "s1", "newest chat first");
            Check(Field<Border>("_footer").Visibility == Visibility.Visible, "archive switch visible when archived chats exist");
            foreach (var width in new[] { 340, 390, 520, 900 }) Snap("list-light", width);

            // Search: local, case and accent insensitive, multiple terms.
            var search = Field<TextBox>("_search");
            search.Text = "regler";
            Pump();
            Check(Targets().SequenceEqual(new[] { "s3", "s5" }), "search filters titles case-insensitively");
            search.Text = "uberlauf";
            Pump();
            Check(Targets().SequenceEqual(new[] { "s4" }), "search ignores umlaut accents");
            search.Text = "zzz";
            Pump();
            Check(Targets().Count == 0 && Field<Border>("_state").Visibility == Visibility.Visible, "no-match state");
            Snap("nomatch-light", 390);
            search.Text = "";
            Pump();

            // Row menu, busy chats cannot be archived.
            Call("OpenMenu", "s2");
            Pump();
            Check(Field<Canvas>("_overlay").Visibility == Visibility.Visible, "row menu opens inside the popup");
            Snap("menu-light", 390);
            Call("CloseMenu", false);
            Call("OpenMenu", "s1");
            Pump();
            var archiveItem = Field<StackPanel>("_menuItems").Children.OfType<Button>().Last();
            Check(!archiveItem.IsEnabled, "busy chat cannot be archived");
            Call("CloseMenu", false);

            // Rename: inline editor, success applies the confirmed title.
            Call("BeginRename", "s3");
            Pump();
            var renameBox = Field<TextBox>("_renameBox");
            Check(renameBox.Text == "PID-Regler Temperaturzone 2 abstimmen", "rename editor starts with the current title");
            renameBox.Text = "";
            Call("CommitRenameAsync");
            Pump();
            Check(Field<string>("_renameError") != null && _calls.Count == 0, "empty title is rejected locally");
            renameBox.Text = "Temperaturregler Zone 2";
            Snap("rename-light", 390);
            Call("CommitRenameAsync");
            Pump();
            Check(_calls.LastOrDefault() == "rename:s3:Temperaturregler Zone 2", "rename delegate receives id and trimmed title");
            Check(!Field<Button>("_renameSave").IsEnabled, "rename buttons disabled while saving");
            _gate.SetResult(true);
            Pump();
            Check(Field<string>("_renameId") == null && Title("s3") == "Temperaturregler Zone 2", "confirmed rename updates the row");

            // Rename failure keeps the editor with the host message.
            Call("BeginRename", "s4");
            Pump();
            renameBox.Text = "Neu";
            Call("CommitRenameAsync");
            Pump();
            _gate.SetException(new InvalidOperationException("Titel konnte nicht gespeichert werden."));
            Pump();
            Check(Field<string>("_renameId") == "s4" && (Field<string>("_renameError") ?? "").Contains("Titel konnte nicht gespeichert werden."), "failed rename stays editable with the error");
            Check(Title("s4") != "Neu", "failed rename does not pretend success");
            Call("CancelRename");
            Pump();

            // Title generation: only an explicit menu click starts it; the
            // host refresh via Show decides the title, never the picker.
            const string GenerateLabel = "Titel automatisch generieren";
            var before = _calls.Count;
            Call("OpenMenu", "s3");
            Pump();
            var generateItem = MenuButton(GenerateLabel);
            Check(generateItem != null && generateItem.IsEnabled && MenuButton("Umbenennen") != null, "row menu offers title generation next to rename");
            Call("CloseMenu", false);
            Pump();
            Check(_calls.Count == before, "opening and closing the menu does not generate");
            Call("OpenMenu", "s1");
            Pump();
            var busyItem = MenuButton(GenerateLabel);
            Check(busyItem != null && !busyItem.IsEnabled, "busy chat cannot generate a title");
            Call("CloseMenu", false);
            Call("GenerateTitleEntryAsync", "s1");
            Call("GenerateTitleEntryAsync", "a1");
            Pump();
            Check(_calls.Count == before, "busy and archived chats are guarded against generation");
            Call("OpenMenu", "s4");
            Pump();
            MenuButton(GenerateLabel).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Check(_calls.Count == before + 1 && _calls.Last() == "generate:s4", "explicit click delegates the original session id");
            Check(_picker.IsOpen && Field<string>("_menuId") == null && Field<Canvas>("_overlay").Visibility == Visibility.Collapsed, "menu closes, picker stays open while generating");
            Check(Pending("s4") == "generate" && More("s4") == null, "pending generation shows the row spinner instead of actions");
            Snap("generate-pending-light", 390);
            var generating = _gate;
            Call("GenerateTitleEntryAsync", "s4");
            Call("BeginRename", "s4");
            Call("ArchiveEntryAsync", "s4");
            Call("SelectEntryAsync", "s4");
            Call("OpenMenu", "s4");
            Pump();
            Check(_calls.Count == before + 1 && Field<string>("_renameId") == null && Field<string>("_menuId") == null, "pending generation blocks duplicate and competing row actions");
            Call("BeginRename", "s5");
            Pump();
            Check(Field<string>("_renameId") == "s5", "manual rename of another chat stays available");
            Call("CancelRename");
            Pump();
            _picker.Show("Anlage_Linie3", "s2", ItemsWith("s4", "Zählerüberlauf in GVL_Zaehler beheben"));
            Pump();
            Check(Pending("s4") == "generate" && Title("s4") == "Zählerüberlauf in GVL_Zaehler beheben", "host refresh during generation keeps the pending row");
            generating.SetResult(true);
            Pump();
            Check(_picker.IsOpen && Pending("s4") == null && More("s4") != null && Title("s4") == "Zählerüberlauf in GVL_Zaehler beheben" && Field<string>("_actionError") == null,
                "completed generation keeps the refreshed title and the open picker");

            // Generation failures free the row; only host messages are shown.
            Call("GenerateTitleEntryAsync", "s5");
            Pump();
            _gate.SetException(new TimeoutException("internal"));
            Pump();
            Check(Field<string>("_actionError") == "Der Titel konnte nicht generiert werden." && Pending("s5") == null && More("s5") != null, "failed generation shows the neutral message and frees the row");
            Call("GenerateTitleEntryAsync", "s5");
            Pump();
            _gate.SetException(new InvalidOperationException("Für leere Chats kann kein Titel erzeugt werden."));
            Pump();
            Check(Field<string>("_actionError") == "Der Titel konnte nicht generiert werden. Für leere Chats kann kein Titel erzeugt werden." && Title("s5") == "regler Kaskade – Stellgrößenbegrenzung",
                "host message is shown and the title stays unchanged");
            Snap("generate-error-light", 390);

            // A host message that already opens with the fallback sentence is
            // shown once; the banner the user sees carries the same text.
            const string TitleFallback = "Der Titel konnte nicht generiert werden.";
            foreach (var hostMessage in new[] {
                TitleFallback,
                "  " + TitleFallback + "  ",
                TitleFallback + " Das Modell hat keine Antwort geliefert.",
                "Der Titel konnte nicht generiert werden: Zeitüberschreitung beim lokalen Modell (60 s).",
                "der titel konnte nicht generiert werden. Größe der Unterhaltung überschreitet das Limit." })
            {
                foreach (var wrapped in new[] { false, true })
                {
                    Call("GenerateTitleEntryAsync", "s5");
                    Pump();
                    Exception thrown = new InvalidOperationException(hostMessage);
                    if (wrapped) thrown = new AggregateException(thrown);
                    _gate.SetException(thrown);
                    Pump();
                    var shown = Field<string>("_actionError") ?? "";
                    var banner = Field<TextBlock>("_bannerText");
                    Check(shown == hostMessage.Trim() && Occurrences(shown, "konnte nicht generiert werden") == 1
                        && banner.Text == shown && banner.IsVisible && Pending("s5") == null,
                        "fallback sentence appears once" + (wrapped ? " (aggregate)" : "") + ": " + hostMessage.Trim());
                }
            }
            Snap("generate-duplicate-error-light", 390);
            Call("GenerateTitleEntryAsync", "s5");
            Pump();
            _gate.SetException(new InvalidOperationException("Der Titel konnte nicht generiert werdenden Zustand prüfen."));
            Pump();
            Check(Field<string>("_actionError") == TitleFallback + " Der Titel konnte nicht generiert werdenden Zustand prüfen.",
                "only a whole leading fallback sentence counts as a repeat");
            Call("GenerateTitleEntryAsync", "s5");
            Pump();
            _gate.SetException(new InvalidOperationException("Für diesen Chat fehlt der Modellzugang. Der Titel konnte nicht generiert werden."));
            Pump();
            Check(Field<string>("_actionError") == TitleFallback + " Für diesen Chat fehlt der Modellzugang. Der Titel konnte nicht generiert werden.",
                "a host detail that does not lead with the fallback keeps its full text");
            Call("GenerateTitleEntryAsync", "s5");
            Pump();
            _gate.SetException(new InvalidOperationException("   "));
            Pump();
            Check(Field<string>("_actionError") == TitleFallback, "a blank host message falls back to the neutral sentence");
            Call("GenerateTitleEntryAsync", "s5");
            Pump();
            Check(Field<string>("_actionError") == null, "a new generation clears the previous error");
            _gate.SetException(new OperationCanceledException(TitleFallback));
            Pump();
            Check(Field<string>("_actionError") == null && Pending("s5") == null && More("s5") != null, "cancelled generation stays silent and frees the row");
            Call("GenerateTitleEntryAsync", "s5");
            Pump();
            _gate.SetException(new AggregateException(new TaskCanceledException()));
            Pump();
            Check(Field<string>("_actionError") == null, "a wrapped cancellation stays silent");
            Call("GenerateTitleEntryAsync", "s5");
            Pump();
            _gate.SetException(new TimeoutException(TitleFallback + " intern"));
            Pump();
            Check(Field<string>("_actionError") == TitleFallback, "non-host exceptions never leak their text, even when it repeats the fallback");

            // The shared formatter covers the other actions in the same way.
            Call("ArchiveEntryAsync", "s6");
            Pump();
            _gate.SetException(new InvalidOperationException("Archivieren fehlgeschlagen. Der Chat wird gerade beantwortet."));
            Pump();
            Check(Field<string>("_actionError") == "Archivieren fehlgeschlagen. Der Chat wird gerade beantwortet.", "archive host message repeating the fallback is shown once");
            Call("BeginRename", "s3");
            Pump();
            Field<TextBox>("_renameBox").Text = "Größe prüfen";
            Call("CommitRenameAsync");
            Pump();
            _gate.SetException(new InvalidOperationException("Umbenennen fehlgeschlagen."));
            Pump();
            Check(Field<string>("_renameError") == "Umbenennen fehlgeschlagen.", "rename host message equal to the fallback is shown once");
            Call("CancelRename");
            Pump();

            // Unicode titles render exactly as the host sends them: no
            // character is stripped, replaced or normalized by the picker.
            var unicodeTitles = new[] {
                "Alternativer Fix für Anschnitt-Klinkungen in TwinCAT ST",
                "Größe prüfen – Rückzug",
                "Ä Ö Ü ä ö ü ß ẞ – „Zitat“ · µs ± 5 % · Ø 12 mm · 25 °C",
                "Messwert 𝛼 ≤ 0,5 ✓ 東京 Linie №3 🚀",
                "Combining: Größe prüfen" };
            var unicodeItems = Items().ToList();
            for (var i = 0; i < unicodeTitles.Length; i++) unicodeItems.Insert(0, Item("u" + i, unicodeTitles[i], 0.5 + i, false, false));
            _picker.Show("Anlage_Linie3", "u0", unicodeItems.ToArray());
            Pump();
            for (var i = 0; i < unicodeTitles.Length; i++)
            {
                var expected = unicodeTitles[i];
                var blocks = Descendants<TextBlock>(Field<StackPanel>("_list")).Where(block => block.Text == expected).ToList();
                var block0 = blocks.FirstOrDefault();
                Check(Title("u" + i) == expected && blocks.Count == 1 && block0.IsVisible && block0.ActualWidth > 0 && block0.ActualHeight > 0
                    && (expected.Length <= 36 || (block0.ToolTip as string) == expected),
                    "unicode title renders unchanged: " + expected);
            }
            Snap("unicode-titles-light", 390);
            Snap("unicode-titles-light", 900);
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();

            // The delegate alone provides the row action button; without it the item is hidden.
            var renameHandler = _picker.RenameAsync;
            var archiveHandler = _picker.ArchiveAsync;
            var generateHandler = _picker.GenerateTitleAsync;
            _picker.RenameAsync = null;
            _picker.ArchiveAsync = null;
            Call("Render");
            Pump();
            Call("OpenMenu", "s3");
            Pump();
            Check(More("s3") != null && Field<StackPanel>("_menuItems").Children.Count == 1 && MenuButton(GenerateLabel) != null, "generation alone provides the row action menu");
            Call("CloseMenu", false);
            _picker.GenerateTitleAsync = null;
            Call("Render");
            Pump();
            Check(More("s3") == null, "no row action button without any action delegate");
            _picker.RenameAsync = renameHandler;
            _picker.ArchiveAsync = archiveHandler;
            Call("Render");
            Call("OpenMenu", "s3");
            Pump();
            Check(MenuButton(GenerateLabel) == null && MenuButton("Umbenennen") != null, "generation item hidden without its delegate");
            Call("CloseMenu", false);
            _picker.GenerateTitleAsync = generateHandler;
            Call("Render");
            Pump();

            // Archive failure shows the banner; success moves the row.
            Call("ArchiveEntryAsync", "s6");
            Pump();
            _gate.SetException(new TimeoutException("internal"));
            Pump();
            Check(Field<string>("_actionError") == "Archivieren fehlgeschlagen.", "non-host exceptions show only the neutral message");
            Check(Targets().Contains("s6"), "failed archive keeps the row");
            Snap("action-error-light", 390);
            Call("ArchiveEntryAsync", "s6");
            Pump();
            _gate.SetResult(true);
            Pump();
            Check(!Targets().Contains("s6"), "archived chat leaves the active list");

            // Archived view and restore.
            Call("SetArchived", true);
            Pump();
            Check(Targets().SequenceEqual(new[] { "s6", "a1", "a2" }), "archived view lists archived chats");
            Snap("archived-light", 390);
            Call("RestoreEntryAsync", "a1");
            Pump();
            _gate.SetResult(true);
            Pump();
            Check(!Targets().Contains("a1"), "restored chat leaves the archive view");
            Call("SetArchived", false);
            Pump();
            Check(Targets().Contains("a1"), "restored chat is active again");

            // Selection: failure is reported, success closes.
            Call("SelectEntryAsync", "s5");
            Pump();
            _gate.SetException(new InvalidOperationException("Der Chat gehört zu einem anderen Projekt."));
            Pump();
            Check(_picker.IsOpen && (Field<string>("_actionError") ?? "").Contains("anderen Projekt"), "failed selection keeps the picker open with the message");
            Call("SelectEntryAsync", "s5");
            Pump();
            _gate.SetResult(true);
            Pump();
            Check(!_picker.IsOpen, "successful selection closes the picker");

            // Stale completions: a closed picker is never reopened or changed.
            _picker.ShowLoading("Anlage_Linie3");
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();
            Call("SelectEntryAsync", "s4");
            Pump();
            var stale = _gate;
            _picker.Close();
            stale.SetException(new InvalidOperationException("late"));
            Pump();
            Check(!_picker.IsOpen, "late failure after close does not reopen");
            _picker.ShowError("late error");
            _picker.Show("Anlage_Linie3", "s2", Items());
            Check(!_picker.IsOpen, "late Show/ShowError after close are ignored");

            // Late generation results never reopen, unblock or overwrite a newer session.
            _picker.ShowLoading("Anlage_Linie3");
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();
            Call("GenerateTitleEntryAsync", "s3");
            Pump();
            var lateGenerate = _gate;
            _picker.Close();
            Pump();
            Check(!_picker.IsOpen && Pending("s3") == null, "closing drops the pending generation");
            _picker.ShowLoading("Anlage_Linie3");
            _picker.Show("Anlage_Linie3", "s2", ItemsWith("s3", "Neu geladener Titel"));
            Pump();
            Call("GenerateTitleEntryAsync", "s3");
            Pump();
            var currentGenerate = _gate;
            lateGenerate.SetException(new InvalidOperationException("late"));
            Pump();
            Check(_picker.IsOpen && Pending("s3") == "generate" && Field<string>("_actionError") == null && Title("s3") == "Neu geladener Titel",
                "late generation failure leaves the refreshed session untouched");
            currentGenerate.SetResult(true);
            Pump();
            Check(Pending("s3") == null && Title("s3") == "Neu geladener Titel", "current generation completes against the refreshed list");
            Call("GenerateTitleEntryAsync", "s4");
            Pump();
            lateGenerate = _gate;
            _picker.Close();
            lateGenerate.SetResult(true);
            Pump();
            Check(!_picker.IsOpen, "late generation success after close does not reopen");

            // Project switch inside an open picker starts a new session.
            _picker.ShowLoading("Anlage_Linie3");
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();
            Call("ArchiveEntryAsync", "s7");
            Pump();
            var old = _gate;
            _picker.ShowLoading("Pruefstand");
            _picker.Show("Pruefstand", null, new[] { Item("p1", "Achsen referenzieren", 5, false, false), Item("s7", "Gleiche ID, anderes Projekt", 9, false, false) });
            Pump();
            old.SetResult(true);
            Pump();
            Check(Targets().Contains("s7"), "completion from the previous project is ignored");

            // Load error and retry.
            _picker.ShowError("Der lokale Chat-Dienst antwortet nicht.");
            Pump();
            Check(Field<Border>("_state").Visibility == Visibility.Visible && !Field<Border>("_searchFrame").IsEnabled, "error state with disabled search");
            Snap("error-light", 390);
            Call("RetryLoadAsync");
            Pump();
            Check(_calls.LastOrDefault() == "retry", "retry delegate called");
            _gate.SetException(new InvalidOperationException("Projekt nicht eindeutig."));
            Pump();
            Check((Field<string>("_errorMessage") ?? "").Contains("Projekt nicht eindeutig."), "failed retry stays in the error state");

            // Empty project.
            _picker.Show("Pruefstand", null, new ProjectChatPickerItem[0]);
            Pump();
            Check(Targets().Count == 0 && Field<Border>("_footer").Visibility == Visibility.Collapsed, "empty state without archive switch");
            Snap("empty-light", 340);

            // Keyboard: Escape closes; anchor-toggle guard does not apply without the mouse.
            _picker.Show("Anlage_Linie3", "s2", Items());
            Pump();
            Key(Field<TextBox>("_search"), System.Windows.Input.Key.Escape);
            Pump();
            Check(!_picker.IsOpen, "Escape closes the picker");

            // Dark palette follows the shared dictionary live.
            _picker.ShowLoading("Anlage_Linie3");
            _picker.Show("Anlage_Linie3", "s2", Items());
            AssistantTheme.Apply(_theme, AssistantTheme.Dark);
            Pump();
            var frame = Field<Border>("_frame");
            Check(AssistantTheme.IsDark(frame.Background), "dark palette reaches the popup");
            foreach (var width in new[] { 340, 520, 900 }) Snap("list-dark", width);
            Call("OpenMenu", "s3");
            Pump();
            Snap("menu-dark", 390);
            Call("CloseMenu", false);
            Call("BeginRename", "s3");
            Pump();
            Snap("rename-dark", 390);
            Call("CancelRename");
            Call("SetArchived", true);
            Pump();
            Snap("archived-dark", 390);
            _picker.ShowError("Der lokale Chat-Dienst antwortet nicht.");
            Pump();
            Snap("error-dark", 390);
            _picker.ShowLoading("Anlage_Linie3");
            Pump();
            Snap("loading-dark", 390);

            Check(ProjectChatPicker.FormatUpdated(0, DateTime.Now) == "", "unknown activity has no time");
            var now = new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Local);
            Check(ProjectChatPicker.FormatUpdated(Millis(now.AddMinutes(-12)), now) == "vor 12 Min.", "minutes");
            Check(ProjectChatPicker.FormatUpdated(Millis(now.AddDays(-1).Date.AddHours(9)), now) == "gestern, 09:00", "yesterday");
            Check(ProjectChatPicker.FormatUpdated(Millis(new DateTime(2025, 3, 4, 8, 0, 0, DateTimeKind.Local)), now) == "04.03.2025", "older date");

            _picker.Dispose();
            _picker.ShowLoading("Anlage_Linie3");
            Check(!_picker.IsOpen, "disposed picker stays closed");
            Check(_picker.GenerateTitleAsync == null && _picker.RenameAsync == null, "dispose clears the action delegates");
        }

        static string Pending(string id)
        {
            string pending;
            return Field<Dictionary<string, string>>("_pending").TryGetValue(id, out pending) ? pending : null;
        }

        static Button More(string id)
        {
            var list = (System.Collections.IList)Field<object>("_targets");
            var target = list.Cast<object>().FirstOrDefault(t => (string)t.GetType().GetField("Id").GetValue(t) == id);
            return target == null ? null : (Button)target.GetType().GetField("More").GetValue(target);
        }

        static int Occurrences(string text, string part)
        {
            var count = 0;
            for (var at = text.IndexOf(part, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.OrdinalIgnoreCase)) count++;
            return count;
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

        static ProjectChatPickerItem[] ItemsWith(string id, string title)
        {
            var items = Items();
            items.First(item => item.Id == id).Title = title;
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
                Item("a2", "Test", 60 * 24 * 400, false, true),
            };
        }

        static void BuildHost(double width)
        {
            _host = new UserControl { Resources = _theme, Width = width, Height = 640 };
            _host.SetResourceReference(Control.BackgroundProperty, "Tc.Background");
            var strip = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 6, 8, 6) };
            strip.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 4; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = "MAIN  ·  Anlage_Linie3", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Foreground");
            row.Children.Add(name);
            var column = 1;
            foreach (var icon in new[] { "history", "plus", "attach", "more" })
            {
                var button = new Button { Content = new System.Windows.Shapes.Path { Data = AssistantTheme.Icon(icon), Width = 16, Height = 16, StrokeThickness = 1.6 } };
                button.SetResourceReference(FrameworkElement.StyleProperty, "Tc.IconButton");
                ((System.Windows.Shapes.Path)button.Content).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Tc.Muted");
                Grid.SetColumn(button, column++);
                row.Children.Add(button);
                if (_anchor == null) _anchor = button;
            }
            strip.Child = row;
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(strip, Dock.Top);
            dock.Children.Add(strip);
            dock.Children.Add(new Border());
            _host.Content = dock;
            var area = SystemParameters.WorkArea;
            _window = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Opacity = 0.01,
                ShowActivated = false, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight, Content = _host,
                Left = area.Right - 960, Top = area.Bottom - 700 };
            _window.Show();
        }

        static void Snap(string state, double width)
        {
            _host.Width = width;
            _window.UpdateLayout();
            Pump();
            var popup = Field<Popup>("_popup");
            var frame = Field<Border>("_frame");
            DependencyObject top = frame;
            while (VisualTreeHelper.GetParent(top) != null) top = VisualTreeHelper.GetParent(top);
            var popupRoot = (FrameworkElement)top;
            popupRoot.UpdateLayout();
            var hostBitmap = Render(_host, _host.ActualWidth, _host.ActualHeight);
            var popupBitmap = Render(popupRoot, popupRoot.ActualWidth, popupRoot.ActualHeight);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(hostBitmap, new Rect(0, 0, _host.ActualWidth, _host.ActualHeight));
                context.DrawImage(popupBitmap, new Rect(popup.HorizontalOffset, popup.VerticalOffset, popupRoot.ActualWidth, popupRoot.ActualHeight));
            }
            var output = new RenderTargetBitmap((int)Math.Ceiling(_host.ActualWidth), (int)Math.Ceiling(_host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            output.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(output));
            using (var file = File.Create(Path.Combine(_out, state + "-" + width + ".png"))) encoder.Save(file);
        }

        static BitmapSource Render(FrameworkElement element, double width, double height)
        {
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, (int)Math.Ceiling(height)), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            return bitmap;
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
    $harness)
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xaml.dll', 'WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll') |
    ForEach-Object { '/reference:' + (Join-Path $refs $_) }
& $csc /nologo /utf8output /noconfig /nostdlib+ /target:exe /platform:anycpu /langversion:latest /warnaserror+ /nowarn:1701,1702 "/out:$exe" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Picker fixture compilation failed.' }
& $exe $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Picker fixture checks failed.' }
Write-Output "Screenshots: $OutputDirectory"
