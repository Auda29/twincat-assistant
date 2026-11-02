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
Set-Content -LiteralPath $harness -Encoding UTF8 -Value @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
