using System;
using Process = System.Diagnostics.Process;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TwinCatAssistant.Native
{
    // OpenChamber owns chat/auth/session/model behavior. Native source enters
    // the composer only through an explicit, exact-session attachment action.
    // No generic COM bridge or model credential handler is exposed.
    public sealed partial class AssistantControl : UserControl, IDisposable
    {
        // Context bar: one row in OpenChamber's palette above the embedded chat.
        private readonly ResourceDictionary _theme = AssistantTheme.CreateResources();
        private string _themeKey;
        private readonly Border _strip = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 6, 8, 6) };
        private readonly Button _pouButton = new Button { HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 6, 3) };
        private readonly Border _kindBadge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        private readonly TextBlock _kindText = new TextBlock { FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        private readonly TextBlock _objectName = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        private readonly ShapePath _chevron = Icon("chevron", 14);
        private readonly TextBlock _scope = new TextBlock { FontSize = 12, Margin = new Thickness(7, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _statusChip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 9, 2), Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly Ellipse _statusDot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _statusText = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        private Button _historyButton, _newChatButton, _attachPouButton, _moreButton;
        private readonly ShapePath _attachIcon = new ShapePath { Width = 16, Height = 16, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Margin = new Thickness(0, 0, 6, 0) };
        private readonly TextBlock _attachLabel = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        // Banner: at most one actionable message, only when something needs attention.
        private readonly Border _banner = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 7, 8, 7), Visibility = Visibility.Collapsed };
        private readonly TextBlock _bannerText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _bannerAction = new Button { Margin = new Thickness(10, 0, 0, 0) };
        private Action _bannerCommand;
        private Button _bannerDismiss;
        private DateTime _previewClosedAt, _menuClosedAt;
        // Popups: own HWNDs, so they render above the WebView2 airspace.
        private readonly Popup _previewPopup = new Popup { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
        private readonly TextBlock _previewTitle = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 12.5 };
        private readonly TextBlock _sourceNotice = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        private readonly TextBox _source = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 12, MaxHeight = 300, Padding = new Thickness(10, 8, 10, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
        private readonly Popup _menuPopup = new Popup { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
        private readonly StackPanel _menuItems = new StackPanel { Margin = new Thickness(5) };
        // Diagnostics: shown only on request from the menu.
        private readonly Border _diagnostics = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 8, 12, 10), Visibility = Visibility.Collapsed };
        private readonly TextBlock _chatStatus = new TextBlock { Text = "Der lokale Chat startet beim Öffnen automatisch.", TextWrapping = TextWrapping.Wrap };
        private readonly ChatConnectionState _chatConnection = new ChatConnectionState();
        private NativeChatCredentials _chatCredentials = new NativeChatCredentials();
        private NativeChatPreferences _chatPreferences = NativeChatPreferences.Load();
        private readonly DispatcherTimer _chatHealth;
        private NativeChatSession _chatSession;
        private bool _checkingChatHealth, _setupMissing;
        private long _navigationGeneration;
        private readonly TextBlock _diagnosticStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _inventoryStatus = new TextBlock { Text = "Inventar nicht geladen. Die Editor-Verfolgung nutzt diesen Katalog nicht.", TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _port = new TextBox { Text = "3123", Width = 70, Margin = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly ComboBox _targets = new ComboBox { MinWidth = 180, Margin = new Thickness(4), DisplayMemberPath = "Name" };
        private WebView2 _browser = new WebView2();
        private bool _browserNeedsReset;
        private readonly StackPanel _toolbar = new StackPanel();
        private readonly TextBox _diagnosticSource = new TextBox { IsReadOnly = true, AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas, Courier New"), MaxHeight = 180, Visibility = Visibility.Collapsed,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        private NativeProjectReader _reader;
        private ActiveEditorTracker _tracker;
        private DTE _boundDte;
        private Uri _origin;
        private bool _connecting, _disposed;
        private Events _events;
        private SolutionEvents _solutionEvents;

        public AssistantControl()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Resources = _theme;
            ApplyTheme(AssistantTheme.IsDark(TryFindResource(EnvironmentColors.ToolWindowBackgroundBrushKey) as Brush) ? AssistantTheme.Dark : AssistantTheme.Light);
            SetResourceReference(BackgroundProperty, "Tc.Background");
            SetResourceReference(ForegroundProperty, "Tc.Foreground");
            var dock = new DockPanel();
            BuildStrip();
            BuildBanner();
            BuildDiagnostics();
            _toolbar.Children.Add(_strip);
            _toolbar.Children.Add(_banner);
            _toolbar.Children.Add(_diagnostics);
            DockPanel.SetDock(_toolbar, Dock.Top);
            dock.Children.Add(_toolbar);
            var grid = new Grid();
            grid.Children.Add(_browser);
            dock.Children.Add(grid);
            Content = dock;
            _browser.GotFocus += (sender, args) => { _menuPopup.IsOpen = false; _previewPopup.IsOpen = false; };
            _chatHealth = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            _chatHealth.Tick += CheckChatHealth;
            InitializeChatIntegration();
            RenderContext(null, null);
        }

        private void BuildStrip()
        {
            _strip.SetResourceReference(Border.BackgroundProperty, "Tc.Background");
            _strip.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            _kindBadge.SetResourceReference(Border.BackgroundProperty, "Tc.PouBadge");
            _kindBadge.Child = _kindText;
            _objectName.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Foreground");
            _scope.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            var chevron = _chevron;
            chevron.Margin = new Thickness(4, 1, 0, 0);
            chevron.SetResourceReference(Shape.StrokeProperty, "Tc.Muted");
            var identity = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_kindBadge, Dock.Left); DockPanel.SetDock(chevron, Dock.Right); DockPanel.SetDock(_objectName, Dock.Left);
            identity.Children.Add(_kindBadge); identity.Children.Add(chevron); identity.Children.Add(_objectName); identity.Children.Add(_scope);
            _pouButton.Content = identity;
            _pouButton.SetResourceReference(StyleProperty, "Tc.Button");
            _pouButton.Click += (sender, args) => {
                _menuPopup.IsOpen = false;
                if (!JustClosed(_previewClosedAt)) _previewPopup.IsOpen = !_previewPopup.IsOpen && _chatActive != null;
            };
            _previewPopup.Closed += (sender, args) => _previewClosedAt = DateTime.UtcNow;
            _menuPopup.Closed += (sender, args) => _menuClosedAt = DateTime.UtcNow;
            _statusChip.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _statusDot, _statusText } };
            _historyButton = IconButton("history", "Verlauf dieses Projekts", OpenHistoryAsync);
            _newChatButton = IconButton("plus", "Neuer Chat", NewProjectChatAsync);
            _attachPouButton = AsyncButton(null, AttachClickAsync);
            _attachPouButton.SetResourceReference(StyleProperty, "Tc.PrimaryButton");
            _attachPouButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _attachIcon, _attachLabel } };
            _attachIcon.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = _attachPouButton });
            _attachLabel.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = _attachPouButton });
            _attachPouButton.Margin = new Thickness(6, 0, 2, 0);
            _moreButton = IconButton("more", "Weitere Aktionen", async () => {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ToggleMenu();
            });
            var separator = new Border { Width = 1, Height = 18, Margin = new Thickness(4, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            separator.SetResourceReference(Border.BackgroundProperty, "Tc.Border");
            var row = new Grid();
            foreach (var width in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            var column = 0;
            // Auto column first: the chip keeps its width and the POU button
            // trims (project name before object name) on narrow tool windows.
            var left = new Grid();
            left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            left.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
            Grid.SetColumn(_statusChip, 1);
            left.Children.Add(_pouButton); left.Children.Add(_statusChip);
            foreach (UIElement element in new UIElement[] { left, _historyButton, _newChatButton, separator, _attachPouButton, _moreButton, _menuPopup })
            { Grid.SetColumn(element, Math.Min(column++, 6)); row.Children.Add(element); }
            row.Children.Add(_previewPopup);
            _strip.Child = row;
            _strip.SizeChanged += (sender, args) => FitStrip(args.NewSize.Width);
            BuildPreview();
            BuildMenu();
        }

        private void BuildPreview()
        {
            _previewPopup.PlacementTarget = _pouButton;
            _previewTitle.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Foreground");
            _sourceNotice.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            _source.SetResourceReference(TextBox.BackgroundProperty, "Tc.Background");
            _source.SetResourceReference(TextBox.ForegroundProperty, "Tc.Foreground");
            var header = new Border { Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new StackPanel { Children = { _previewTitle, _sourceNotice } } };
            header.SetResourceReference(Border.BackgroundProperty, "Tc.Surface");
            header.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            _previewPopup.Child = PopupFrame(new DockPanel { LastChildFill = true, Children = { Docked(header, Dock.Top), _source } }, 480);
        }

        private void BuildMenu()
        {
            _menuPopup.PlacementTarget = _moreButton;
            _menuPopup.Placement = PlacementMode.Bottom;
            _menuPopup.HorizontalOffset = -220;
            _menuPopup.Child = PopupFrame(_menuItems, 250);
        }

        private void BuildBanner()
        {
            _bannerText.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Foreground");
            _bannerAction.SetResourceReference(StyleProperty, "Tc.SecondaryButton");
            _bannerAction.Click += (sender, args) => _bannerCommand?.Invoke();
            _bannerDismiss = IconButton("close", "Ausblenden", async () => {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                DismissNotice();
            });
            _bannerDismiss.Margin = new Thickness(4, 0, 0, 0);
            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_bannerDismiss, Dock.Right); DockPanel.SetDock(_bannerAction, Dock.Right);
            row.Children.Add(_bannerDismiss); row.Children.Add(_bannerAction); row.Children.Add(_bannerText);
            _banner.Child = row;
            _banner.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
        }

        private void BuildDiagnostics()
        {
            _diagnostics.SetResourceReference(Border.BackgroundProperty, "Tc.Surface");
            _diagnostics.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            _diagnostics.SetResourceReference(TextElement.ForegroundProperty, "Tc.Foreground");
            _port.SetResourceReference(StyleProperty, "Tc.TextBox");
            _diagnosticSource.SetResourceReference(StyleProperty, "Tc.TextBox");
            var title = new TextBlock { Text = "Diagnose", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var close = IconButton("close", "Diagnose ausblenden", () => { _diagnostics.Visibility = Visibility.Collapsed; return System.Threading.Tasks.Task.CompletedTask; });
            var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close); head.Children.Add(title);
            var row = new WrapPanel { Margin = new Thickness(-4, 4, 0, 0) };
            row.Children.Add(new TextBlock { Text = "Port", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            row.Children.Add(_port);
            row.Children.Add(Secondary(AsyncButton("Verbinden", ConnectAsync)));
            row.Children.Add(Secondary(Button("IDE-Kontext lesen", Refresh)));
            row.Children.Add(_targets);
            row.Children.Add(Secondary(Button("Lokal lesen", Read)));
            var panel = new StackPanel();
            foreach (UIElement element in new UIElement[] { head, _chatStatus, _modelStatus, _mcpStatus, row, _inventoryStatus, _diagnosticStatus, _diagnosticSource })
                panel.Children.Add(element);
            _diagnosticSource.Margin = new Thickness(0, 4, 0, 0);
            _diagnostics.Child = panel;
        }

        // Narrow tool windows keep the POU name readable: the attach label and
        // then the chip text collapse to icons with tooltips.
        private void FitStrip(double width)
        {
            _attachLabel.Visibility = width < 460 ? Visibility.Collapsed : Visibility.Visible;
            _attachIcon.Margin = width < 460 ? new Thickness(0) : new Thickness(0, 0, 6, 0);
            _statusText.Visibility = width < 380 ? Visibility.Collapsed : Visibility.Visible;
            _statusDot.Margin = width < 380 ? new Thickness(0) : new Thickness(0, 0, 5, 0);
            _statusChip.Padding = width < 380 ? new Thickness(6) : new Thickness(8, 2, 9, 2);
        }

        // A click on the toggle button first closes a StaysOpen=false popup;
        // without this guard the same click would reopen it.
        private static bool JustClosed(DateTime closedAt) { return (DateTime.UtcNow - closedAt).TotalMilliseconds < 300; }

        private static UIElement Docked(UIElement element, Dock dock) { DockPanel.SetDock(element, dock); return element; }

        private Border PopupFrame(UIElement content, double width)
        {
            var frame = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Width = width, Margin = new Thickness(8, 2, 8, 12),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.16, Direction = 270 }, Child = content };
            frame.SetResourceReference(Border.BackgroundProperty, "Tc.Background");
            frame.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            frame.SetResourceReference(TextElement.ForegroundProperty, "Tc.Foreground");
            return frame;
        }

        private static ShapePath Icon(string name, double size = 16)
        {
            return new ShapePath { Data = AssistantTheme.Icon(name), Width = size, Height = size, Stretch = Stretch.None, StrokeThickness = name == "more" ? 2.4 : 1.6,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }

        private Button IconButton(string icon, string tooltip, Func<System.Threading.Tasks.Task> action)
        {
            var button = AsyncButton(null, action);
            button.SetResourceReference(StyleProperty, "Tc.IconButton");
            var path = Icon(icon);
            path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            button.Content = path;
            button.Margin = new Thickness(1, 0, 1, 0);
            button.ToolTip = tooltip;
            System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
            return button;
        }

        private static Button Secondary(Button button) { button.SetResourceReference(StyleProperty, "Tc.SecondaryButton"); button.Margin = new Thickness(4); return button; }

        private static Button Button(string label, Action action)
        {
            var button = new Button { Content = label, Margin = new Thickness(4) };
            button.Click += (sender, args) => action();
            return button;
        }

        private static Button AsyncButton(string label, Func<System.Threading.Tasks.Task> action)
        {
            var button = new Button { Content = label, Margin = new Thickness(4) };
#pragma warning disable VSTHRD100 // This local function is a WPF event handler, with a catch-all boundary.
            async void OnClick(object sender, RoutedEventArgs args)
            {
                // WPF event handler: exceptions are caught before crossing the IDE boundary.
                try { await action(); } catch { /* Never leak into the IDE. */ }
            }
#pragma warning restore VSTHRD100
            button.Click += OnClick;
            return button;
        }

        // Follow the live OpenChamber palette (light or dark) once the adapter reports it.
        private void ApplyTheme(ChatTheme theme)
        {
            var key = AssistantTheme.Key(theme);
            if (key == null || key == _themeKey) return;
            if (AssistantTheme.Apply(_theme, theme)) _themeKey = key;
        }

        public void Bind(DTE dte, IVsMonitorSelection selection, IVsRunningDocumentTable rdt, Func<string, bool?> dirty)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            if (dte == null)
            {
                _tracker?.Dispose();
                _tracker = null;
                _reader?.Invalidate();
                _reader = null;
                _boundDte = null;
                _chatSnapshotReader = null;
                _chatProjectReader = null;
                RenderProjectContext(null, null);
                RenderContext(null, "Die IDE-Editorbindung fehlt. TwinCAT Assistant erneut über das Menü öffnen.");
                return;
            }
            if (_boundDte == dte && _tracker != null) return;
            _tracker?.Dispose();
            _tracker = null;
            _boundDte = dte;
            if (_solutionEvents != null) { _solutionEvents.Opened -= Invalidate; _solutionEvents.AfterClosing -= Invalidate; }
            _events = dte.Events; // retain event source COM wrappers
            _solutionEvents = _events.SolutionEvents;
            _solutionEvents.Opened += Invalidate;
            _solutionEvents.AfterClosing += Invalidate;
            _reader = new NativeProjectReader(dte, Process.GetCurrentProcess().Id, dirty);
            try { _tracker = new ActiveEditorTracker(dte, selection, rdt, dirty, RenderContext, RenderProjectContext); }
            catch { RenderContext(null, "Die Editor-Dienste der IDE sind nicht verfügbar. Toolfenster erneut öffnen."); }
            _chatSnapshotReader = () => _tracker?.ReadForChat();
            _chatProjectReader = () => _tracker?.ReadProjectForChat();
        }

        private void RenderContext(ActiveEditorSnapshot snapshot, string reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _chatActive = snapshot;
            if (snapshot?.Identity != null) _chatProject = snapshot.Identity.ProjectIdentity;
            if (_disposed) return;
            var view = ActiveEditorPresentation.From(snapshot, reason);
            var title = snapshot == null ? (_chatProject == null ? "Kein PLC-Projekt aktiv" : Path.GetFileNameWithoutExtension(_chatProject.ProjectFile)) : view.Title;
            if (_objectName.Text != title) _objectName.Text = title;
            var scope = snapshot == null || _chatProject == null ? "" : Path.GetFileNameWithoutExtension(_chatProject.ProjectFile);
            if (_scope.Text != scope) _scope.Text = scope;
            _kindText.Text = snapshot == null ? "" : ActiveEditorPresentation.Kind(snapshot.Declaration);
            _kindBadge.Visibility = snapshot == null ? Visibility.Collapsed : Visibility.Visible;
            _chevron.Visibility = snapshot == null ? Visibility.Collapsed : Visibility.Visible;
            _previewTitle.Text = snapshot == null ? "" : view.Title + " · " + scope;
            var notice = snapshot == null ? view.Detail : "Snapshot aus TwinCAT. Ungespeicherter Editorinhalt ist nicht verifiziert.";
            if (_sourceNotice.Text != notice) _sourceNotice.Text = notice;
            _sourceNotice.ToolTip = view.Detail;
            if (_source.Text != view.Source) _source.Text = view.Source;
            _source.Visibility = snapshot == null ? Visibility.Collapsed : Visibility.Visible;
            if (snapshot == null) _previewPopup.IsOpen = false;
            _pouButton.ToolTip = snapshot == null ? (object)(reason ?? "Öffne einen Structured-Text-Baustein in dieser IDE.")
                : "Klicken für die ST-Vorschau" + Environment.NewLine + "IDE PID " + snapshot.Identity.IdeProcessId
                    + Environment.NewLine + snapshot.Identity.SolutionFile + Environment.NewLine + snapshot.Identity.ProjectFile
                    + Environment.NewLine + snapshot.Identity.TreePath;
            RenderChatBinding();
            KickAutomation();
        }

        private void RenderProjectContext(NativeProjectIdentity project, string reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _chatProject = project;
            if (_disposed) return;
            if (_chatActive != null && !_chatActive.Identity.ProjectIdentity.Equals(project))
            { RenderContext(null, reason); return; }
            var scope = project == null || _chatActive == null ? "" : Path.GetFileNameWithoutExtension(project.ProjectFile);
            if (_scope.Text != scope) _scope.Text = scope;
            if (_chatActive == null) _objectName.Text = project == null ? "Kein PLC-Projekt aktiv" : Path.GetFileNameWithoutExtension(project.ProjectFile);
            RenderChatBinding();
            KickAutomation();
        }

        private void Invalidate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _reader?.Invalidate();
            _targets.ItemsSource = null;
            _diagnosticSource.Clear();
            _diagnosticSource.Visibility = Visibility.Collapsed;
            _inventoryStatus.Text = "Inventar nicht geladen. Die Editor-Verfolgung nutzt diesen Katalog nicht.";
            _diagnosticStatus.Text = "";
        }

        private void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Invalidate();
            try
            {
                if (_reader == null) return;
                var context = _reader.GetContext();
                _targets.ItemsSource = context.Targets;
                _inventoryStatus.Text = "IDE PID " + context.IdeProcessId + ": " + context.Targets.Count
                    + " Katalogziele. " + string.Join(" ", context.Warnings);
                _diagnosticStatus.Text = _tracker == null ? "Editor-Verfolgung nicht verfügbar."
                    : "Aktiver Lesevorgang: " + _tracker.LastReadMilliseconds.ToString("F1") + " ms; Maximum: "
                        + _tracker.MaxReadMilliseconds.ToString("F1") + " ms. Kein Quelltext an den Chat übergeben.";
            }
            catch { _diagnosticStatus.Text = "Diagnose-Inventar nicht verfügbar; erneut lesen, wenn die IDE bereit ist."; }
        }

        private void Read()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _diagnosticSource.Clear();
            try
            {
                var target = _targets.SelectedItem as NativeTarget;
                if (target == null || _reader == null) return;
                var read = _reader.ReadTarget(target.Id);
                _diagnosticStatus.Text = "Manueller Diagnose-Lesevorgang. Änderungsstand: " + read.Dirty.State + ". " + read.Dirty.Reason;
                _diagnosticSource.Text = read.Declaration + Environment.NewLine + read.Implementation;
                _diagnosticSource.Visibility = Visibility.Visible;
            }
            catch { _diagnosticStatus.Text = "Diagnose-Lesevorgang abgelehnt; den genauen Solution-Kontext neu lesen."; }
        }

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            if (_connecting || _disposed) return;
            int port;
            if (!int.TryParse(_port.Text, out port) || port < 1024 || port > 65535)
            {
                _chatStatus.Text = "Unter Diagnose einen Loopback-Port zwischen 1024 und 65535 eintragen.";
                _diagnostics.Visibility = Visibility.Visible;
                return;
            }
            _connecting = true;
            _setupMissing = false;
            var generation = _chatConnection.Begin();
            ShowChatStatus();
            try
            {
                var origin = new Uri("http://127.0.0.1:" + port + "/");
                if (_chatSession != null && (_chatSession.Port != port || !_chatSession.IsRunning))
                {
                    await _chatSession.StopAsync();
                    _chatSession = null;
                }
                if (_disposed) return;
                if (!await NativeChatSession.IsProtectedServiceReadyAsync(port))
                {
                    if (_disposed) return;
                    if (_chatSession != null)
                    {
                        await _chatSession.StopAsync();
                        _chatSession = null;
                    }
                    if (_disposed) return;
                    NativeChatConfiguration config;
                    try { config = _chatConfiguration(); }
                    catch
                    {
                        _setupMissing = true;
                        _diagnosticStatus.Text = "Lokalen Chat einrichten: scripts/Configure-NativeChat.ps1 mit Repository-, Node- und .NET-SDK-Pfad ausführen. Konfiguration: " + NativeChatConfiguration.ConfigurationPath;
                        _chatConnection.Fail(generation, "Die Einrichtung des lokalen Chats fehlt oder ist ungültig.");
                        ShowChatStatus();
                        return;
                    }
                    _chatCredentials.EnsureGenerated();
                    _chatStatus.Text = "Geschützter lokaler Chat-Dienst wird gestartet …";
                    var session = new NativeChatSession();
                    _chatSession = session;
                    session.Stopped += () => OnChatServiceStopped(session);
                    await session.StartAsync(config, port, _chatCredentials.Load());
                    if (_disposed) { session.Dispose(); return; }
                }
                _chatConnection.Loading(generation);
                ShowChatStatus();
                if (_browserNeedsReset)
                {
                    var oldBrowser = _browser;
                    var grid = (Grid)oldBrowser.Parent;
                    _browser = new WebView2();
                    _browser.GotFocus += (sender, args) => { _menuPopup.IsOpen = false; _previewPopup.IsOpen = false; };
                    grid.Children.Remove(oldBrowser);
                    oldBrowser.Dispose();
                    grid.Children.Insert(0, _browser);
                    _browserNeedsReset = false;
                }
                _browser.Visibility = Visibility.Visible;
                if (_browser.CoreWebView2 == null)
                {
                    var profile = _chatBrowserProfile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "TwinCatAssistant", "WebView2", Process.GetCurrentProcess().Id.ToString());
                    var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                    if (_disposed) return;
                    await _browser.EnsureCoreWebView2Async(environment);
                    if (_disposed) return;
                    _browser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                    _browser.CoreWebView2.Settings.IsWebMessageEnabled = false;
                    _browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    _browser.CoreWebView2.NavigationStarting += OnChatNavigationStarting;
                    _browser.CoreWebView2.NavigationCompleted += OnChatNavigationCompleted;
                    _browser.CoreWebView2.ProcessFailed += (sender, args) => {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        if (_disposed || !ReferenceEquals(sender, _browser.CoreWebView2)) return;
                        _browserNeedsReset = true;
                        _chatConnection.Fail(_chatConnection.Generation, "Der Chat-Browser wurde beendet. Neu verbinden erstellt ihn neu.");
                        ShowChatStatus();
                    };
                    _browser.CoreWebView2.FrameNavigationStarting += (sender, args) => { if (!Allowed(args.Uri)) args.Cancel = true; };
                    _browser.CoreWebView2.NewWindowRequested += (sender, args) => { args.Handled = true; };
                    _browser.CoreWebView2.PermissionRequested += (sender, args) => { args.State = CoreWebView2PermissionState.Deny; };
                    _browser.CoreWebView2.DownloadStarting += (sender, args) => { args.Cancel = true; };
                    _browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    _browser.CoreWebView2.WebResourceRequested += (sender, args) => {
                        if (!AllowedResource(args.Request.Uri)) args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked external request", "");
                        else if (ChatContextPolicy.BlocksMutatingFeature(args.Request.Method, new Uri(args.Request.Uri)))
                        {
                            var bytes = System.Text.Encoding.UTF8.GetBytes("{\"code\":\"TWINCAT_READ_ONLY\",\"error\":\"Datei-, Git-, Terminal- und Update-Aktionen sind im eingebetteten TwinCAT-Chat gesperrt.\"}");
                            args.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes), 403, "TwinCAT read-only chat", "Content-Type: application/json; charset=utf-8");
                        }
                        else if (ChatContextPolicy.IsPromptRequest(args.Request.Method, new Uri(args.Request.Uri), out var session)
                            && (!CanPromptCurrentChat() || session != _chatSelection.sessionId))
                        {
                            var bytes = System.Text.Encoding.UTF8.GetBytes("{\"code\":\"TWINCAT_CONTEXT_UNBOUND\",\"error\":\"Diese Unterhaltung gehört nicht zum aktiven TwinCAT-Projekt. Einen Projektchat öffnen.\"}");
                            args.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes), 403, "TwinCAT chat binding required", "Content-Type: application/json; charset=utf-8");
                        }
                    };
                }
                _origin = origin;
                // Sign in natively before the first page load so OpenChamber's
                // password screen never appears. A failure keeps that screen as fallback.
                await SignInBrowserAsync(port);
                if (_disposed) return;
                _navigationGeneration = generation;
                _browser.CoreWebView2.Navigate(origin.AbsoluteUri);
                _chatHealth.Start();
            }
            catch
            {
                if (!_disposed)
                {
                    _chatConnection.Fail(generation, "Chat-Verbindung fehlgeschlagen. Laufzeit-Einrichtung, Port und eine bestehende Profilsperre unter Diagnose prüfen.");
                    ShowChatStatus();
                }
            }
            finally { _connecting = false; }
        }

        private async System.Threading.Tasks.Task<bool> SignInBrowserAsync(int port)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                if (!_chatCredentials.Exists || _browser.CoreWebView2 == null) return false;
                var cookie = await NativeChatLogin.SignInAsync(port, _chatCredentials.Load());
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed || _browser.CoreWebView2 == null) return false;
                var manager = _browser.CoreWebView2.CookieManager;
                var value = manager.CreateCookie(cookie.Name, cookie.Value, "127.0.0.1", "/");
                value.IsHttpOnly = true;
                value.SameSite = CoreWebView2CookieSameSiteKind.Strict;
                value.Expires = DateTime.Now.AddSeconds(cookie.MaxAgeSeconds);
                manager.AddOrUpdateCookie(value);
                return true;
            }
            catch { return false; }
        }

        private void OnChatNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || !ReferenceEquals(sender, _browser.CoreWebView2) || !Allowed(args.Uri)) { args.Cancel = true; return; }
            _chatDocumentGeneration++;
            _preparedProject = null; _mcpFailureKey = null;
            _chatAdapterReady = false; _chatSelection = null; _chatBinding = null;
            RenderChatBinding();
            _navigationGeneration = _chatConnection.Generation;
            _chatConnection.Navigating(args.NavigationId);
            ShowChatStatus();
        }
#pragma warning disable VSTHRD100 // WebView2 navigation event handler.
        private async void OnChatNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || !ReferenceEquals(sender, _browser.CoreWebView2) || _navigationGeneration != _chatConnection.Generation) return;
            _chatConnection.Completed(args.NavigationId, args.IsSuccess, args.HttpStatusCode, args.WebErrorStatus.ToString());
            ShowChatStatus();
            if (args.IsSuccess && args.NavigationId == _chatConnection.NavigationId && args.HttpStatusCode >= 200 && args.HttpStatusCode < 400)
            {
                _chatReconnectAttempts = 0;
                _chatReconnect.Stop();
                await InitializeChatAdapterAsync(_chatConnection.Generation);
            }
        }
#pragma warning restore VSTHRD100
        private void ShowChatStatus()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            _chatStatus.Text = !_chatAdapterReady ? _chatConnection.Message
                : _chatSelection?.authenticated == true ? "Lokaler Chat verbunden und angemeldet."
                : "Lokaler Chat verbunden. Anmeldung läuft …";
            if (_chatConnection.Phase == "failed")
            {
                _browser.Visibility = Visibility.Collapsed;
                _chatAdapterReady = false; _chatSelection = null; _chatBinding = null;
            }
            RenderChatBinding();
        }
#pragma warning disable VSTHRD100 // WPF timer callback; exceptions stay inside the UI boundary.
        private async void OnChatServiceStopped(NativeChatSession session)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed || _chatSession != session) return;
                _chatConnection.Fail(_chatConnection.Generation, "Der lokale Chat-Dienst wurde beendet. Er wird automatisch neu gestartet.");
                ShowChatStatus();
                ScheduleChatReconnect();
            }
            catch { /* The dispatcher may already be shutting down. */ }
        }
        private async void CheckChatHealth(object sender, EventArgs args)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed || _checkingChatHealth || _connecting || _origin == null) return;
                _checkingChatHealth = true;
                var generation = _chatConnection.Generation;
                try
                {
                    if (!await NativeChatSession.IsProtectedServiceReadyAsync(_origin.Port) && !_disposed && generation == _chatConnection.Generation)
                    {
                        _chatConnection.Fail(generation, "Der lokale Chat-Dienst ist nicht mehr erreichbar.");
                        ShowChatStatus();
                        _chatHealth.Stop();
                        ScheduleChatReconnect();
                    }
                    else if (!_disposed && generation == _chatConnection.Generation)
                    {
                        await PollChatSelectionAsync();
                        ShowChatStatus();
                    }
                }
                finally { _checkingChatHealth = false; }
            }
            catch { }
        }
#pragma warning restore VSTHRD100

        private bool Allowed(string url)
        {
            Uri uri;
            return _origin != null && Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == _origin.Scheme
                && uri.Host == _origin.Host && uri.Port == _origin.Port && string.IsNullOrEmpty(uri.UserInfo);
        }
        private bool AllowedResource(string url)
        {
            if (Allowed(url)) return true;
            Uri uri;
            return _origin != null && Uri.TryCreate(url, UriKind.Absolute, out uri)
                && uri.Scheme == "ws" && uri.Host == _origin.Host && uri.Port == _origin.Port
                && string.IsNullOrEmpty(uri.UserInfo);
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            _disposed = true;
            _chatConnection.Dispose();
            _chatHealth.Stop();
            _chatHealth.Tick -= CheckChatHealth;
            DisposeChatIntegration();
            _chatSession?.Dispose();
            _chatSession = null;
            _tracker?.Dispose();
            _tracker = null;
            _chatSnapshotReader = null;
            if (_solutionEvents != null) { _solutionEvents.Opened -= Invalidate; _solutionEvents.AfterClosing -= Invalidate; }
            _reader = null;
            _boundDte = null;
            _menuPopup.IsOpen = false; _previewPopup.IsOpen = false;
            _source.Clear();
            _diagnosticSource.Clear();
            _browser.Dispose();
        }
    }
}
