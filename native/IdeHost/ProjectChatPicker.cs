using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;

// Pure WPF component without VS services: every entry point checks the
// dispatcher with VerifyAccess, and deferred focus work is posted to the same
// dispatcher. The VS threading analyzers do not recognize that pattern.
#pragma warning disable VSTHRD001, VSTHRD010, VSTHRD110

namespace TwinCatAssistant.Native
{
    // Metadata of one chat of the active project, supplied by the host.
    internal sealed class ProjectChatPickerItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        // Last activity, Unix epoch milliseconds (0 = unknown).
        public long Updated { get; set; }
        public bool Busy { get; set; }
        // Stored as OpenChamber's archive flag; shown as "erledigt".
        public bool Archived { get; set; }
    }

    // Chat bar of the active TwinCAT project (or of the general chats), drawn
    // as an ordinary WPF element inside the tool window: no OS popup, so it
    // composes with the WebView2 composition control and IDE auto-hide panes.
    // The host places View either docked beside the conversation (Docked) or
    // as an overlay on narrow windows. Pure presentation: the host supplies
    // metadata and performs every action through the delegates; nothing here
    // reads projects, sessions, the network or the browser.
    //
    // Lifecycle: ShowLoading opens the bar (an answer to the user or to a
    // restored preference). Show, ShowError and SetSelected only update an
    // open bar and never reopen a closed one, so late results after closing
    // or a project switch are dropped. Closing, a new session or a different
    // project name start a new session; completions of actions started
    // earlier are ignored. Title generation never writes a title locally:
    // the host refreshes the list through Show once the title is stored.
    internal sealed class ProjectChatPicker : IDisposable
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
        private const string Selecting = "select", Renaming = "rename", Archiving = "archive", Restoring = "restore", Generating = "generate";
        // Keyboard target of the "Erledigte Chats" section toggle; never a session id.
        private const string DoneToggleId = "\0done";

        private enum PickerView { Loading, Ready, Error }

        private sealed class Entry
        {
            public string Id, Title;
            public long Updated;
            public bool Busy, Archived;
        }

        // Keyboard order of the visible rows: the primary control of each row
        // and its action button.
        private sealed class RowTarget
        {
            public string Id;
            public Control Primary;
            public Button More;
        }

        private readonly FrameworkElement _anchor;
        private readonly Border _frame = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Focusable = true, ClipToBounds = true };
        // Quiet tint over the background: the bar reads as its own plane in
        // the live palette without a new colour.
        private readonly Border _tint = new Border { IsHitTestVisible = false, Opacity = 0.55 };
        private readonly Canvas _overlay = new Canvas { Visibility = Visibility.Collapsed, Background = Brushes.Transparent };
        private readonly Border _menu = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(3), Width = 224,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.18, Direction = 270 } };
        private readonly StackPanel _menuItems = new StackPanel();
        // Header
        private readonly TextBlock _title = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        private readonly ShapePath _titleIcon = Glyph("chat");
        private Button _backButton, _collapseButton, _newButton;
        private readonly TextBlock _newLabel = new TextBlock { Text = "Neuer Chat", FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly ShapePath _newIcon = Glyph("plus");
        private readonly FrameworkElement _newSpinner = Spinner(14);
        // Search
        private readonly Border _searchFrame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 8, 8), Height = 28 };
        private readonly TextBox _search = new TextBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(2, 0, 2, 0),
            VerticalContentAlignment = VerticalAlignment.Center, FontSize = 12.5, MaxLength = 200 };
        private readonly TextBlock _searchPlaceholder = new TextBlock { Text = "Chats durchsuchen", FontSize = 12.5, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        private Button _searchClear;
        // Action feedback
        private readonly Border _banner = new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(8, 0, 8, 6), Padding = new Thickness(8, 5, 3, 5), Visibility = Visibility.Collapsed };
        private readonly TextBlock _bannerText = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        // Body
        private readonly ScrollViewer _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, Padding = new Thickness(0, 0, 2, 8) };
        private readonly StackPanel _list = new StackPanel();
        private readonly Border _state = new Border { Padding = new Thickness(18, 22, 18, 24), Visibility = Visibility.Collapsed };
        // Inline rename editor, reused across renders so text and caret survive a refresh.
        private readonly StackPanel _renameEditor = new StackPanel();
        private readonly Border _renameFrame = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Height = 26 };
        private readonly TextBox _renameBox = new TextBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(3, 0, 3, 0),
            VerticalContentAlignment = VerticalAlignment.Center, FontSize = 12.5, MaxLength = 200 };
        private readonly TextBlock _renameErrorText = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 4, 0, 0), Visibility = Visibility.Collapsed };
        private Button _renameSave, _renameCancel;
        private Border _renameHost;

        private readonly List<RowTarget> _targets = new List<RowTarget>();
        private readonly Dictionary<string, string> _pending = new Dictionary<string, string>(StringComparer.Ordinal);
        private List<Entry> _entries = new List<Entry>();
        private PickerView _view = PickerView.Loading;
        private string _projectName, _selectedId, _errorMessage, _actionError, _renameId, _renameError, _menuId, _signature;
        // _showArchived: the "Erledigte Chats" section is expanded.
        private bool _open, _docked, _general, _showArchived, _newPending, _retryPending, _disposed, _suppressSearch, _revealSelected;
        private int _session, _content;

        public ProjectChatPicker(FrameworkElement anchor, ResourceDictionary theme)
        {
            _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
            _anchor.Dispatcher.VerifyAccess();
            // Shared with the host: live palette changes reach the bar too.
            _frame.Resources = theme ?? throw new ArgumentNullException(nameof(theme));
            _frame.SetResourceReference(Border.BackgroundProperty, "Tc.Background");
            _frame.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            _frame.SetResourceReference(TextElement.ForegroundProperty, "Tc.Foreground");
            _frame.SetValue(TextElement.FontFamilyProperty, TextElement.GetFontFamily(anchor));
            _frame.SetValue(TextElement.FontSizeProperty, 12.0);
            _frame.FocusVisualStyle = null;
            _frame.UseLayoutRounding = true;
            _frame.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(_frame, TextFormattingMode.Display);
            KeyboardNavigation.SetDirectionalNavigation(_frame, KeyboardNavigationMode.None);
            AutomationProperties.SetName(_frame, "Chatleiste");
            _tint.SetResourceReference(Border.BackgroundProperty, "Tc.HoverOverlay");

            var layout = new DockPanel { LastChildFill = true };
            layout.Children.Add(DockTo(BuildHeader(), Dock.Top));
            layout.Children.Add(DockTo(BuildNew(), Dock.Top));
            layout.Children.Add(DockTo(BuildSearch(), Dock.Top));
            layout.Children.Add(DockTo(BuildBanner(), Dock.Top));
            _scroll.Resources = (ResourceDictionary)XamlReader.Parse(ScrollBarXaml);
            _scroll.Content = _list;
            layout.Children.Add(new Grid { Children = { _scroll, _state } });
            BuildRenameEditor();
            _menu.Child = _menuItems;
            _menu.SetResourceReference(Border.BackgroundProperty, "Tc.Background");
            _menu.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            KeyboardNavigation.SetTabNavigation(_menu, KeyboardNavigationMode.Cycle);
            _overlay.Children.Add(_menu);
            _overlay.MouseDown += (sender, args) => { if (args.OriginalSource == _overlay) { CloseMenu(false); args.Handled = true; } };
            _frame.Child = new Grid { Children = { _tint, layout, _overlay } };
            ApplyMode();

            _frame.PreviewKeyDown += OnPreviewKeyDown;
            _anchor.IsVisibleChanged += OnAnchorVisibleChanged;
        }

        // The element the host places beside or above the conversation.
        public FrameworkElement View => _frame;
        public bool IsOpen => _open;

        // Docked: the bar sits beside the conversation and stays open after
        // selecting or creating a chat; Tab continues into the chat. Overlay
        // (narrow windows): selection, Escape and the host's outside click
        // close it and Tab cycles inside it.
        public bool Docked
        {
            get { return _docked; }
            set { _anchor.Dispatcher.VerifyAccess(); if (_docked == value) return; _docked = value; ApplyMode(); }
        }

        // The general chat history rather than one PLC project; wording only.
        public bool General
        {
            get { return _general; }
            set { _anchor.Dispatcher.VerifyAccess(); if (_general == value) return; _general = value; if (!_disposed) Render(); }
        }

        public Func<string, Task> SelectAsync { get; set; }
        public Func<string, string, Task> RenameAsync { get; set; }
        // Marks a chat as done (stored through the existing archive flag).
        public Func<string, Task> ArchiveAsync { get; set; }
        // Reopens a done chat (existing restore action).
        public Func<string, Task> RestoreAsync { get; set; }
        // Generates and stores a title for the chat; hidden when null.
        public Func<string, Task> GenerateTitleAsync { get; set; }
        public Func<Task> RetryAsync { get; set; }
        public Func<Task> NewAsync { get; set; }
        // Back from another history to the project chats; hidden when null.
        public Func<Task> BackAsync { get; set; }
        // Called after the bar closed; true when the user closed it (collapse
        // button or Escape), false for programmatic and post-action closes.
        public Action<bool> Closed { get; set; }

        public void ShowLoading(string projectName) { ShowLoading(projectName, true, false); }

        // Opens the bar with a loading state; when already open, it reloads in
        // place (search text survives for the same project) unless newSession
        // asks for a clean list. focus moves the keyboard into the search box.
        public void ShowLoading(string projectName, bool focus, bool newSession)
        {
            _anchor.Dispatcher.VerifyAccess();
            if (_disposed) return;
            var opening = !_open;
            if (opening || newSession || !SameProject(projectName)) BeginSession(projectName);
            _open = true;
            _content++;
            _view = PickerView.Loading;
            _errorMessage = null;
            _signature = null;
            Render();
            if (focus && (opening || !_frame.IsKeyboardFocusWithin))
                Defer(() => { if (!_frame.IsKeyboardFocusWithin && !FocusElement(_search)) FocusElement(_frame); });
        }

        // Updates an open bar with the chats (open and done). Ignored while
        // closed. An unchanged list does not rebuild the rows, so hover,
        // scroll position and focus stay where they are.
        public void Show(string projectName, string selectedSessionId, ProjectChatPickerItem[] items)
        {
            _anchor.Dispatcher.VerifyAccess();
            if (_disposed || !_open) return;
            if (!SameProject(projectName)) BeginSession(projectName);
            _content++;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            _entries = (items ?? new ProjectChatPickerItem[0])
                .Where(item => item != null && !string.IsNullOrEmpty(item.Id) && item.Id != DoneToggleId && seen.Add(item.Id))
                .Select(item => new Entry { Id = item.Id, Title = item.Title, Updated = item.Updated, Busy = item.Busy, Archived = item.Archived })
                .OrderByDescending(entry => entry.Updated).ThenBy(entry => DisplayTitle(entry), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var wasReady = _view == PickerView.Ready;
            _view = PickerView.Ready;
            _errorMessage = null;
            _selectedId = string.IsNullOrEmpty(selectedSessionId) ? null : selectedSessionId;
            if (_renameId != null)
            {
                var renamed = Find(_renameId);
                if (renamed == null || renamed.Archived) EndRename();
            }
            if (_menuId != null && Find(_menuId) == null) CloseMenu(false);
            if (wasReady && Signature() == _signature) return;
            Render();
        }

        // Highlights the chat that is open in the conversation. Returns false
        // when the bar is closed or does not list that chat (yet), so the host
        // can refresh the list.
        public bool SetSelected(string sessionId)
        {
            _anchor.Dispatcher.VerifyAccess();
            if (_disposed || !_open || _view != PickerView.Ready) return false;
            var id = string.IsNullOrEmpty(sessionId) ? null : sessionId;
            if (id != _selectedId)
            {
                _selectedId = id;
                _revealSelected = true;
                Render();
            }
            return id == null || Find(id) != null;
        }

        // Shows a load failure with a retry action in an open bar. Never opens it.
        public void ShowError(string message)
        {
            _anchor.Dispatcher.VerifyAccess();
            if (_disposed || !_open) return;
            _content++;
            _view = PickerView.Error;
            _errorMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
            CloseMenu(false);
            EndRename();
            Render();
        }

        public void Close() { _anchor.Dispatcher.VerifyAccess(); CloseCore(false); }

        public void Dispose()
        {
            _anchor.Dispatcher.VerifyAccess();
            if (_disposed) return;
            CloseCore(false);
            _disposed = true;
            _session++;
            _frame.PreviewKeyDown -= OnPreviewKeyDown;
            _anchor.IsVisibleChanged -= OnAnchorVisibleChanged;
            SelectAsync = null; RenameAsync = null; ArchiveAsync = null; RestoreAsync = null; RetryAsync = null; NewAsync = null; GenerateTitleAsync = null;
            BackAsync = null; Closed = null;
            _list.Children.Clear();
            _entries = new List<Entry>();
            if (_frame.Parent is Decorator decorator && decorator.Child == _frame) decorator.Child = null;
            else if (_frame.Parent is Panel panel) panel.Children.Remove(_frame);
            _frame.Resources = new ResourceDictionary(); // release the shared palette
        }

        // ---- Session and lifecycle ----

        private bool SameProject(string projectName) { return string.Equals(_projectName, projectName, StringComparison.Ordinal); }

        private void BeginSession(string projectName)
        {
            _session++;
            _projectName = projectName;
            _selectedId = null;
            _entries = new List<Entry>();
            _pending.Clear();
            _newPending = false; _retryPending = false;
            _showArchived = false;
            _actionError = null;
            _revealSelected = true;
            _signature = null;
            CloseMenu(false);
            EndRename();
            _suppressSearch = true;
            _search.Text = "";
            _suppressSearch = false;
            _scroll.ScrollToVerticalOffset(0);
        }

        private void CloseCore(bool byUser)
        {
            if (!_open) return;
            var focused = _frame.IsKeyboardFocusWithin;
            _open = false;
            _session++;
            _pending.Clear();
            _newPending = false; _retryPending = false;
            CloseMenu(false);
            EndRename();
            // Focus inside the closing bar returns to the anchor (the clock);
            // focus the user put elsewhere stays there.
            if (focused && !_disposed && _anchor.IsVisible && _anchor.Focusable) _anchor.Focus();
            var closed = Closed;
            if (closed != null) { try { closed(byUser); } catch { /* Host layout never breaks the bar. */ } }
        }

        private void ApplyMode()
        {
            KeyboardNavigation.SetTabNavigation(_frame, _docked ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.Cycle);
            _collapseButton.ToolTip = _docked ? "Chatleiste schließen" : "Chatleiste schließen (Esc)";
        }

        private void OnAnchorVisibleChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!_anchor.IsVisible) CloseCore(false); }

        // ---- Building blocks ----

        private UIElement BuildHeader()
        {
            _backButton = IconButton("back", "Zurück zu den Projektchats", () => BackAsync?.Invoke() ?? Task.CompletedTask);
            _backButton.Width = 24; _backButton.Height = 24;
            _backButton.Margin = new Thickness(0, 0, 4, 0);
            _collapseButton = IconButton("sidebar", "Chatleiste schließen", () => { CloseCore(true); return Task.CompletedTask; });
            _collapseButton.Width = 26; _collapseButton.Height = 26;
            AutomationProperties.SetName(_collapseButton, "Chatleiste schließen");
            _titleIcon.SetResourceReference(Shape.StrokeProperty, "Tc.Muted");
            _titleIcon.Margin = new Thickness(0, 0, 8, 0);
            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 8, 6, 6), MinHeight = 26 };
            header.Children.Add(DockTo(_collapseButton, Dock.Right));
            header.Children.Add(DockTo(_backButton, Dock.Left));
            header.Children.Add(DockTo(_titleIcon, Dock.Left));
            header.Children.Add(_title);
            return header;
        }

        private UIElement BuildNew()
        {
            _newButton = new Button { Height = 30, Margin = new Thickness(8, 0, 8, 6), Padding = new Thickness(9, 0, 9, 0), HorizontalContentAlignment = HorizontalAlignment.Left };
            _newButton.SetResourceReference(FrameworkElement.StyleProperty, "Tc.SecondaryButton");
            _newButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            _newIcon.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = _newButton });
            _newSpinner.Visibility = Visibility.Collapsed;
            _newButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { new Grid { Children = { _newIcon, _newSpinner } }, _newLabel } };
            AutomationProperties.SetName(_newButton, "Neuer Chat");
            _newButton.Click += (sender, args) => Run(CreateChatAsync);
            return _newButton;
        }

        private UIElement BuildSearch()
        {
            _searchFrame.SetResourceReference(Border.BackgroundProperty, "Tc.Surface");
            _searchFrame.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            _searchFrame.IsKeyboardFocusWithinChanged += (sender, args) =>
                _searchFrame.SetResourceReference(Border.BorderBrushProperty, _searchFrame.IsKeyboardFocusWithin ? "Tc.Primary" : "Tc.Border");
            StyleInput(_search);
            _searchPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            AutomationProperties.SetName(_search, "Chats durchsuchen");
            var icon = Glyph("search");
            icon.SetResourceReference(Shape.StrokeProperty, "Tc.Muted");
            icon.Margin = new Thickness(7, 0, 2, 0);
            _searchClear = IconButton("close", "Suche zurücksetzen", () => { ClearSearch(); return Task.CompletedTask; });
            _searchClear.Width = 22; _searchClear.Height = 22; _searchClear.Margin = new Thickness(0, 0, 2, 0);
            _searchClear.Visibility = Visibility.Collapsed;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_search, 1); Grid.SetColumn(_searchPlaceholder, 1); Grid.SetColumn(_searchClear, 2);
            grid.Children.Add(icon); grid.Children.Add(_searchPlaceholder); grid.Children.Add(_search); grid.Children.Add(_searchClear);
            _searchFrame.Child = grid;
            _search.TextChanged += (sender, args) => {
                var empty = _search.Text.Length == 0;
                _searchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                _searchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
                if (_suppressSearch) return;
                CloseMenu(false);
                _scroll.ScrollToVerticalOffset(0);
                Render();
            };
            return _searchFrame;
        }

        private UIElement BuildBanner()
        {
            _banner.SetResourceReference(Border.BackgroundProperty, "Tc.ErrorSurface");
            _bannerText.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Foreground");
            var icon = Glyph("alert");
            icon.SetResourceReference(Shape.StrokeProperty, "Tc.Error");
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 0, 6, 0);
            var dismiss = IconButton("close", "Meldung ausblenden", () => { _actionError = null; Render(); return Task.CompletedTask; });
            dismiss.Width = 22; dismiss.Height = 22; dismiss.VerticalAlignment = VerticalAlignment.Top; dismiss.Margin = new Thickness(4, -2, 0, -2);
            var row = new DockPanel { LastChildFill = true };
            row.Children.Add(DockTo(dismiss, Dock.Right));
            row.Children.Add(DockTo(icon, Dock.Left));
            row.Children.Add(_bannerText);
            _banner.Child = row;
            return _banner;
        }

        private void BuildRenameEditor()
        {
            _renameFrame.SetResourceReference(Border.BackgroundProperty, "Tc.Surface");
            _renameFrame.SetResourceReference(Border.BorderBrushProperty, "Tc.Primary");
            StyleInput(_renameBox);
            AutomationProperties.SetName(_renameBox, "Neuer Chattitel");
            _renameFrame.Child = _renameBox;
            _renameErrorText.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Error");
            _renameBox.TextChanged += (sender, args) => {
                if (_renameError == null) return;
                _renameError = null;
                _renameErrorText.Visibility = Visibility.Collapsed;
            };
            _renameBox.PreviewKeyDown += (sender, args) => {
                if (args.Key != Key.Enter) return;
                args.Handled = true;
                Run(CommitRenameAsync);
            };
            _renameCancel = SmallButton("Abbrechen", "Tc.SecondaryButton");
            _renameCancel.Click += (sender, args) => CancelRename();
            _renameSave = SmallButton("Speichern", "Tc.PrimaryButton");
            _renameSave.Margin = new Thickness(6, 0, 0, 0);
            _renameSave.Click += (sender, args) => Run(CommitRenameAsync);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0),
                Children = { _renameCancel, _renameSave } };
            _renameEditor.Children.Add(_renameFrame);
            _renameEditor.Children.Add(_renameErrorText);
            _renameEditor.Children.Add(buttons);
        }

        private void StyleInput(TextBox box)
        {
            box.SetResourceReference(Control.ForegroundProperty, "Tc.Foreground");
            box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Tc.Foreground");
            box.SetResourceReference(TextBoxBase.SelectionBrushProperty, "Tc.Primary");
            box.FocusVisualStyle = null;
        }

        // ---- Rendering ----

        // Everything a row shows, so an identical refresh can skip the rebuild.
        private string Signature()
        {
            var now = DateTime.Now;
            var text = new StringBuilder();
            text.Append(_selectedId).Append('\u0001');
            foreach (var entry in _entries)
                text.Append(entry.Id).Append('\u0002').Append(entry.Title).Append('\u0002').Append(entry.Busy ? '1' : '0').Append(entry.Archived ? '1' : '0')
                    .Append('\u0002').Append(FormatUpdated(entry.Updated, now)).Append('\u0001');
            return text.ToString();
        }

        private void Render()
        {
            if (_disposed) return;
            var focusId = FocusedRowId(out var focusMore);
            var renameFocused = _renameEditor.IsKeyboardFocusWithin;
            var searchFocused = _search.IsKeyboardFocusWithin;
            var offset = _scroll.VerticalOffset;
            if (_renameHost != null) _renameHost.Child = null;
            _renameHost = null;
            _list.Children.Clear();
            _targets.Clear();

            var active = _entries.Where(entry => !entry.Archived).ToList();
            var archived = _entries.Where(entry => entry.Archived).ToList();
            if (_showArchived && _view == PickerView.Ready && archived.Count == 0 && !_pending.ContainsValue(Restoring)) _showArchived = false;
            RenderHeader();

            var ready = _view == PickerView.Ready;
            _searchFrame.IsEnabled = _view != PickerView.Error;
            _banner.Visibility = _actionError == null ? Visibility.Collapsed : Visibility.Visible;
            _bannerText.Text = _actionError ?? "";

            UIElement state = null;
            if (_view == PickerView.Loading) state = LoadingState();
            else if (_view == PickerView.Error) state = ErrorState();
            else
            {
                var terms = Terms(_search.Text);
                var openMatches = active.Where(entry => Matches(entry, terms)).ToList();
                var doneMatches = archived.Where(entry => Matches(entry, terms)).ToList();
                if (_entries.Count == 0) state = EmptyState();
                else if (openMatches.Count == 0 && doneMatches.Count == 0) state = NoMatchState();
                else
                {
                    if (openMatches.Count == 0) _list.Children.Add(InlineNote(terms.Length > 0 ? "Keine offenen Chats mit diesem Suchbegriff." : "Keine offenen Chats. Neuer Chat beginnt einen."));
                    foreach (var entry in openMatches) _list.Children.Add(BuildRow(entry));
                    if (doneMatches.Count > 0)
                    {
                        _list.Children.Add(BuildDoneToggle(doneMatches.Count, terms.Length > 0));
                        if (_showArchived) foreach (var entry in doneMatches) _list.Children.Add(BuildRow(entry));
                    }
                }
            }
            _state.Child = state;
            _state.Visibility = state == null ? Visibility.Collapsed : Visibility.Visible;
            _scroll.Visibility = state == null ? Visibility.Visible : Visibility.Collapsed;
            RenderRenameEditor();
            _signature = ready ? Signature() : null;
            // The rebuilt rows keep the reading position of the list.
            if (state == null && offset > 0) { _scroll.UpdateLayout(); _scroll.ScrollToVerticalOffset(offset); }

            if (ready && _revealSelected && _selectedId != null)
            {
                _revealSelected = false;
                var target = _targets.FirstOrDefault(row => row.Id == _selectedId);
                if (target != null) Defer(() => target.Primary.BringIntoView());
            }
            if (renameFocused && _renameId != null) Defer(() => FocusElement(_renameBox));
            else if (focusId != null)
            {
                var target = _targets.FirstOrDefault(row => row.Id == focusId);
                Defer(() => {
                    if (target == null) FocusElement(_search);
                    else if (!(focusMore && target.More != null && FocusElement(target.More))) FocusElement(target.Primary);
                });
            }
            else if (searchFocused && !_search.IsKeyboardFocusWithin) Defer(() => FocusElement(_search));
        }

        private void RenderHeader()
        {
            var name = string.IsNullOrWhiteSpace(_projectName) ? (_general ? "Allgemeine Chats" : "Aktives Projekt") : _projectName;
            _title.Text = name;
            _title.ToolTip = _general ? "Allgemeine Chats ohne Projekt- und Bausteinkontext" : "Chats im Projekt " + name;
            _titleIcon.Data = _general ? Geometry.Parse("M4,2.5 L12,2.5 A1.5,1.5 0 0 1 13.5,4 L13.5,9.5 A1.5,1.5 0 0 1 12,11 L7,11 L4.5,13.5 L4.5,11 L4,11 A1.5,1.5 0 0 1 2.5,9.5 L2.5,4 A1.5,1.5 0 0 1 4,2.5 Z")
                : AssistantTheme.Icon("chat");
            _backButton.Visibility = BackAsync == null ? Visibility.Collapsed : Visibility.Visible;
            _titleIcon.Visibility = BackAsync == null ? Visibility.Visible : Visibility.Collapsed;
            _newButton.Visibility = NewAsync == null ? Visibility.Collapsed : Visibility.Visible;
            _newButton.IsEnabled = !_newPending && _view != PickerView.Loading;
            _newButton.ToolTip = _general ? "Neuer allgemeiner Chat ohne Projektkontext" : "Neuer Chat in diesem Projekt";
            _newLabel.Text = _newPending ? "Wird erstellt …" : "Neuer Chat";
            _newIcon.Visibility = _newPending ? Visibility.Collapsed : Visibility.Visible;
            _newSpinner.Visibility = _newPending ? Visibility.Visible : Visibility.Collapsed;
        }

        private UIElement BuildDoneToggle(int count, bool searching)
        {
            var chevron = Glyph("chevron");
            chevron.SetResourceReference(Shape.StrokeProperty, "Tc.Muted");
            chevron.RenderTransformOrigin = new Point(0.5, 0.5);
            chevron.RenderTransform = new RotateTransform(_showArchived ? 0 : -90);
            var label = new TextBlock { Text = "Erledigte Chats", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            var number = new TextBlock { Text = count.ToString(German), FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            number.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            var content = new DockPanel { LastChildFill = false };
            content.Children.Add(DockTo(chevron, Dock.Left));
            content.Children.Add(DockTo(label, Dock.Left));
            content.Children.Add(DockTo(number, Dock.Right));
            var toggle = new Button { Content = content, Padding = new Thickness(8, 5, 10, 5) };
            toggle.SetResourceReference(FrameworkElement.StyleProperty, "Tc.MenuRow");
            toggle.ToolTip = _showArchived ? "Erledigte Chats einklappen" : "Als erledigt markierte Chats anzeigen und wieder öffnen";
            AutomationProperties.SetName(toggle, "Erledigte Chats, " + count + (searching ? " Treffer" : "") + (_showArchived ? ", ausgeklappt" : ", eingeklappt"));
            toggle.Click += (sender, args) => SetArchived(!_showArchived);
            _targets.Add(new RowTarget { Id = DoneToggleId, Primary = toggle });
            var line = new Border { Height = 1, Margin = new Thickness(10, 6, 10, 4) };
            line.SetResourceReference(Border.BackgroundProperty, "Tc.Border");
            var section = new StackPanel { Margin = new Thickness(4, 2, 4, 1) };
            section.Children.Add(line);
            section.Children.Add(toggle);
            return section;
        }

        private UIElement BuildRow(Entry entry)
        {
            var id = entry.Id;
            var selected = id == _selectedId && !entry.Archived;
            _pending.TryGetValue(id, out var pending);
            var row = new Grid { Margin = new Thickness(4, 0, 4, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (_renameId == id && !entry.Archived)
            {
                var editor = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 6, 8, 7) };
                editor.SetResourceReference(Border.BackgroundProperty, "Tc.NeutralSurface");
                Grid.SetColumnSpan(editor, 2);
                row.Children.Add(editor);
                _renameHost = editor;
                _targets.Add(new RowTarget { Id = id, Primary = _renameBox });
                return row;
            }

            var text = new StackPanel();
            text.Children.Add(TitleBlock(entry, selected));
            text.Children.Add(MetaBlock(entry, pending));
            var target = new RowTarget { Id = id };

            if (!entry.Archived)
            {
                if (selected)
                {
                    // The open chat: a primary-tinted plate and an accent bar,
                    // legible in both palettes and without relying on colour alone.
                    var plate = new Border { CornerRadius = new CornerRadius(7), Opacity = 0.13, IsHitTestVisible = false };
                    plate.SetResourceReference(Border.BackgroundProperty, "Tc.Primary");
                    Grid.SetColumnSpan(plate, 2);
                    row.Children.Add(plate);
                }
                var select = new Button { Content = text, Tag = id, Padding = new Thickness(12, 6, 34, 6) };
                select.SetResourceReference(FrameworkElement.StyleProperty, "Tc.MenuRow");
                Grid.SetColumnSpan(select, 2);
                select.Click += (sender, args) => Run(() => SelectEntryAsync(id));
                select.MouseRightButtonUp += (sender, args) => { args.Handled = true; OpenMenu(id); };
                AutomationProperties.SetName(select, DisplayTitle(entry) + (selected ? ", geöffnet" : "") + (entry.Busy ? ", Antwort läuft" : ""));
                AutomationProperties.SetHelpText(select, "Eingabe öffnet den Chat. F2 benennt um, Kontextmenütaste zeigt weitere Aktionen.");
                row.Children.Add(select);
                target.Primary = select;
                if (selected)
                {
                    var accent = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(2, 7, 0, 7), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
                    accent.SetResourceReference(Border.BackgroundProperty, "Tc.Primary");
                    row.Children.Add(accent);
                }
                if (pending != null) row.Children.Add(Trailing(Spinner(14), new Thickness(0, 0, 11, 0)));
                else if (CanRename(entry) || CanGenerate(entry) || ArchiveAsync != null)
                {
                    var more = IconButton("more", "Aktionen für „" + DisplayTitle(entry) + "“", () => { OpenMenu(id); return Task.CompletedTask; });
                    more.Width = 24; more.Height = 24;
                    target.More = more;
                    row.Children.Add(Trailing(more, new Thickness(0, 0, 6, 0)));
                    void Reveal() { more.Opacity = row.IsMouseOver || row.IsKeyboardFocusWithin || _menuId == id ? 1 : 0; }
                    row.MouseEnter += (sender, args) => Reveal();
                    row.MouseLeave += (sender, args) => Reveal();
                    row.IsKeyboardFocusWithinChanged += (sender, args) => Reveal();
                    Reveal();
                }
            }
            else
            {
                row.Children.Add(new Border { Padding = new Thickness(12, 6, 8, 6), Child = text });
                if (RestoreAsync != null)
                {
                    var restore = SmallButton(pending == Restoring ? "Wird geöffnet …" : "Wieder öffnen", "Tc.SecondaryButton");
                    restore.FontSize = 11.5;
                    restore.IsEnabled = pending == null;
                    restore.Tag = id;
                    restore.ToolTip = "Zurück zu den offenen Chats; Verlauf und Anhänge bleiben erhalten";
                    AutomationProperties.SetName(restore, "„" + DisplayTitle(entry) + "“ wieder öffnen");
                    restore.Click += (sender, args) => Run(() => RestoreEntryAsync(id));
                    row.Children.Add(Trailing(restore, new Thickness(4, 0, 6, 0)));
                    target.Primary = restore;
                }
            }
            if (target.Primary != null) _targets.Add(target);
            return row;
        }

        private static UIElement Trailing(FrameworkElement element, Thickness margin)
        {
            element.Margin = margin;
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(element, 1);
            return element;
        }

        private TextBlock TitleBlock(Entry entry, bool selected)
        {
            var untitled = string.IsNullOrWhiteSpace(entry.Title);
            var title = new TextBlock { Text = DisplayTitle(entry), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = 17, MaxHeight = 34,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, FontStyle = untitled ? FontStyles.Italic : FontStyles.Normal };
            title.SetResourceReference(TextBlock.ForegroundProperty, entry.Archived || untitled ? "Tc.Muted" : "Tc.Foreground");
            if (!untitled && entry.Title.Trim().Length > 30) title.ToolTip = entry.Title.Trim();
            return title;
        }

        private TextBlock MetaBlock(Entry entry, string pending)
        {
            var meta = new TextBlock { FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextWrapping = TextWrapping.Wrap };
            meta.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            var parts = 0;
            void Separator() { if (parts++ > 0) meta.Inlines.Add(new Run(", ")); }
            if (pending != null)
            {
                Separator();
                meta.Inlines.Add(new Run(pending == Selecting ? "Wird geöffnet …" : pending == Renaming ? "Wird umbenannt …"
                    : pending == Generating ? "Titel wird generiert …" : pending == Archiving ? "Wird als erledigt markiert …" : "Wird wieder geöffnet …"));
            }
            if (entry.Busy && !entry.Archived)
            {
                Separator();
                var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 4, 1) };
                dot.SetResourceReference(Shape.FillProperty, "Tc.Primary");
                Pulse(dot);
                meta.Inlines.Add(new InlineUIContainer(dot) { BaselineAlignment = BaselineAlignment.Center });
                var busy = new Run("Antwort läuft");
                busy.SetResourceReference(TextElement.ForegroundProperty, "Tc.Primary");
                meta.Inlines.Add(busy);
                meta.ToolTip = "Die Antwort läuft weiter, auch wenn ein anderer Chat geöffnet wird.";
            }
            var when = FormatUpdated(entry.Updated, DateTime.Now);
            if (when.Length > 0)
            {
                Separator();
                meta.Inlines.Add(new Run(when));
                if (meta.ToolTip == null) meta.ToolTip = FormatUpdatedFull(entry.Updated);
            }
            if (parts == 0) meta.Inlines.Add(new Run(entry.Archived ? "Erledigt" : "Keine Aktivität bekannt"));
            return meta;
        }

        private void RenderRenameEditor()
        {
            if (_renameId == null || _renameHost == null) return;
            var saving = _pending.TryGetValue(_renameId, out var pending) && pending == Renaming;
            _renameBox.IsReadOnly = saving;
            _renameSave.IsEnabled = !saving;
            _renameCancel.IsEnabled = !saving;
            _renameSave.Content = saving ? "Speichert …" : "Speichern";
            _renameErrorText.Text = _renameError ?? "";
            _renameErrorText.Visibility = _renameError == null ? Visibility.Collapsed : Visibility.Visible;
            _renameHost.Child = _renameEditor;
        }

        private static UIElement InlineNote(string text)
        {
            var note = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 4, 12, 8) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            return note;
        }

        private UIElement LoadingState()
        {
            var spinner = Spinner(18);
            spinner.Margin = new Thickness(0, 0, 0, 8);
            return Centered(spinner, Line("Chats werden geladen …", "Tc.Muted"));
        }

        private UIElement ErrorState()
        {
            var icon = Glyph("alert", 1.4);
            icon.SetResourceReference(Shape.StrokeProperty, "Tc.Error");
            icon.Margin = new Thickness(0, 0, 0, 8);
            var heading = Line("Chats konnten nicht geladen werden.", "Tc.Foreground");
            heading.FontWeight = FontWeights.SemiBold;
            var panel = Centered(icon, heading);
            if (_errorMessage != null)
            {
                var detail = Line(_errorMessage, "Tc.Muted");
                detail.Margin = new Thickness(0, 3, 0, 0);
                panel.Children.Add(detail);
            }
            if (RetryAsync != null)
            {
                var retry = SmallButton(_retryPending ? "Wird geladen …" : "Erneut versuchen", "Tc.SecondaryButton");
                retry.Margin = new Thickness(0, 12, 0, 0);
                retry.HorizontalAlignment = HorizontalAlignment.Center;
                retry.IsEnabled = !_retryPending;
                retry.Click += (sender, args) => Run(RetryLoadAsync);
                panel.Children.Add(retry);
                Defer(() => { if (!_frame.IsKeyboardFocusWithin || _search.IsKeyboardFocusWithin) return; FocusElement(retry); });
            }
            return panel;
        }

        private UIElement EmptyState()
        {
            var icon = Glyph("chat", 1.4);
            icon.SetResourceReference(Shape.StrokeProperty, "Tc.Muted");
            icon.Margin = new Thickness(0, 0, 0, 8);
            var panel = Centered(icon, Line(_general ? "Noch keine allgemeinen Chats." : "In diesem Projekt gibt es noch keine Chats.", "Tc.Muted"));
            if (NewAsync != null)
            {
                var start = SmallButton(_newPending ? "Wird erstellt …" : "Neuen Chat beginnen", "Tc.SecondaryButton");
                start.Margin = new Thickness(0, 12, 0, 0);
                start.HorizontalAlignment = HorizontalAlignment.Center;
                start.IsEnabled = !_newPending;
                start.Click += (sender, args) => Run(CreateChatAsync);
                panel.Children.Add(start);
            }
            return panel;
        }

        private UIElement NoMatchState()
        {
            var panel = Centered(Line("Keine Treffer für „" + _search.Text.Trim() + "“.", "Tc.Muted"));
            var reset = SmallButton("Suche zurücksetzen", "Tc.Button");
            reset.SetResourceReference(Control.ForegroundProperty, "Tc.Primary");
            reset.Margin = new Thickness(0, 6, 0, 0);
            reset.HorizontalAlignment = HorizontalAlignment.Center;
            reset.Click += (sender, args) => ClearSearch();
            panel.Children.Add(reset);
            return panel;
        }

        private static StackPanel Centered(params UIElement[] children)
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260 };
            foreach (var child in children)
            {
                if (child is FrameworkElement element) element.HorizontalAlignment = HorizontalAlignment.Center;
                panel.Children.Add(child);
            }
            return panel;
        }

        private static TextBlock Line(string text, string brush)
        {
            var line = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            line.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return line;
        }

        // ---- Row menu (inside the bar, so it composes with the chat) ----

        private void OpenMenu(string id)
        {
            var entry = Find(id);
            var target = _targets.FirstOrDefault(row => row.Id == id);
            if (entry == null || entry.Archived || target == null || _pending.ContainsKey(id) || _view != PickerView.Ready) return;
            _menuItems.Children.Clear();
            if (CanRename(entry)) MenuItem("pencil", "Umbenennen", "F2", true, null, () => BeginRename(id));
            if (CanGenerate(entry))
                MenuItem("refresh", "Titel automatisch generieren", null, !entry.Busy, entry.Busy ? "Erst nach Abschluss der laufenden Antwort möglich." : "Titel aus dem Chatverlauf erzeugen und speichern.",
                    () => Run(() => GenerateTitleEntryAsync(id)));
            if (ArchiveAsync != null)
                MenuItem("check", "Als erledigt markieren", null, !entry.Busy, entry.Busy ? "Erst nach Abschluss der laufenden Antwort möglich."
                    : "Unter „Erledigte Chats“ ablegen. Verlauf und Anhänge bleiben erhalten; jederzeit wieder zu öffnen.",
                    () => Run(() => ArchiveEntryAsync(id)));
            if (_menuItems.Children.Count == 0) return;
            _menuId = id;
            _overlay.Visibility = Visibility.Visible;
            _frame.UpdateLayout();
            var anchor = (FrameworkElement)target.More ?? target.Primary;
            _menu.Width = Math.Max(160, Math.Min(224, _overlay.ActualWidth - 8));
            _menu.Measure(new Size(_menu.Width, double.PositiveInfinity));
            var height = _menu.DesiredSize.Height;
            var corner = anchor.TranslatePoint(new Point(0, 0), _overlay);
            var x = Math.Max(4, Math.Min(corner.X + anchor.ActualWidth - _menu.Width, _overlay.ActualWidth - _menu.Width - 4));
            var y = corner.Y + anchor.ActualHeight + 2;
            if (y + height > _overlay.ActualHeight - 4) y = Math.Max(4, corner.Y - height - 2);
            Canvas.SetLeft(_menu, x);
            Canvas.SetTop(_menu, y);
            if (target.More != null) target.More.Opacity = 1;
            Defer(() => FocusElement(_menuItems.Children.OfType<Button>().FirstOrDefault(button => button.IsEnabled)));
        }

        private void MenuItem(string icon, string label, string shortcut, bool enabled, string detail, Action action)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = Glyph(icon);
            glyph.SetResourceReference(Shape.StrokeProperty, "Tc.Muted");
            glyph.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(glyph);
            var text = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (shortcut != null)
            {
                var keys = new TextBlock { Text = shortcut, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                keys.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
                Grid.SetColumn(keys, 2);
                grid.Children.Add(keys);
            }
            var item = new Button { Content = grid, IsEnabled = enabled, ToolTip = detail, Padding = new Thickness(7, 5, 8, 5) };
            item.SetResourceReference(FrameworkElement.StyleProperty, "Tc.MenuRow");
            ToolTipService.SetShowOnDisabled(item, true);
            AutomationProperties.SetName(item, label);
            item.Click += (sender, args) => { CloseMenu(false); action(); };
            _menuItems.Children.Add(item);
        }

        private void CloseMenu(bool restoreFocus)
        {
            if (_menuId == null && _overlay.Visibility != Visibility.Visible) return;
            var id = _menuId;
            _menuId = null;
            _overlay.Visibility = Visibility.Collapsed;
            _menuItems.Children.Clear();
            var target = _targets.FirstOrDefault(row => row.Id == id);
            if (target?.More != null) target.More.Opacity = target.More.IsMouseOver || target.More.IsKeyboardFocusWithin ? 1 : 0;
            if (restoreFocus && target != null) Defer(() => { if (!(target.More != null && FocusElement(target.More))) FocusElement(target.Primary); });
        }

        // ---- Actions ----

        // A running answer in the previous chat continues; only the
        // conversation shown in the chat changes.
        private async Task SelectEntryAsync(string id)
        {
            if (_disposed || !_open || _view != PickerView.Ready) return;
            var entry = Find(id);
            if (entry == null || entry.Archived) return;
            if (id == _selectedId) { if (!_docked) CloseCore(false); return; }
            var handler = SelectAsync;
            if (handler == null || _pending.ContainsKey(id) || _pending.ContainsValue(Selecting)) return;
            var session = _session;
            _pending[id] = Selecting;
            _actionError = null;
            Render();
            try { await handler(id); }
            catch (Exception error)
            {
                if (session != _session) return;
                _pending.Remove(id);
                Fail("Der Chat konnte nicht geöffnet werden.", error);
                return;
            }
            if (session != _session) return;
            _pending.Remove(id);
            // The host confirms the selection with SetSelected or Show.
            if (!_docked) CloseCore(false); else Render();
        }

        private bool CanRename(Entry entry) { return RenameAsync != null && !entry.Archived; }

        private void BeginRename(string id)
        {
            var entry = Find(id);
            if (entry == null || !CanRename(entry) || _pending.ContainsKey(id) || _view != PickerView.Ready) return;
            CloseMenu(false);
            _renameId = id;
            _renameError = null;
            _renameBox.IsReadOnly = false;
            _renameBox.Text = entry.Title?.Trim() ?? "";
            Render();
            Defer(() => { if (FocusElement(_renameBox)) _renameBox.SelectAll(); });
        }

        private void CancelRename()
        {
            var id = _renameId;
            if (id == null || (_pending.TryGetValue(id, out var pending) && pending == Renaming)) return;
            EndRename();
            Render();
            FocusRow(id);
        }

        private void EndRename()
        {
            _renameId = null;
            _renameError = null;
        }

        private async Task CommitRenameAsync()
        {
            var id = _renameId;
            var entry = id == null ? null : Find(id);
            var handler = RenameAsync;
            if (entry == null || handler == null || _pending.ContainsKey(id)) return;
            var title = (_renameBox.Text ?? "").Trim();
            if (title.Length == 0)
            {
                _renameError = "Bitte einen Titel eingeben.";
                Render();
                return;
            }
            if (string.Equals(title, entry.Title?.Trim(), StringComparison.Ordinal)) { CancelRename(); return; }
            var session = _session;
            var content = _content;
            _pending[id] = Renaming;
            _actionError = null;
            Render();
            try { await handler(id, title); }
            catch (Exception error)
            {
                if (session != _session) return;
                _pending.Remove(id);
                var message = Describe("Umbenennen fehlgeschlagen.", error);
                if (message != null && _renameId == id) _renameError = message;
                else if (message != null) _actionError = message;
                Render();
                if (_renameId == id) Defer(() => FocusElement(_renameBox));
                return;
            }
            if (session != _session) return;
            _pending.Remove(id);
            // Confirmed by the host; a refresh via Show replaces it anyway.
            if (content == _content) entry.Title = title;
            if (_renameId == id) EndRename();
            Render();
            FocusRow(id);
        }

        private bool CanGenerate(Entry entry) { return GenerateTitleAsync != null && !entry.Archived; }

        // The host stores the title and answers with Show; the bar only
        // reports progress and failures and never invents a title.
        private async Task GenerateTitleEntryAsync(string id)
        {
            var entry = Find(id);
            var handler = GenerateTitleAsync;
            if (_disposed || !_open || _view != PickerView.Ready || entry == null || entry.Archived || entry.Busy || handler == null
                || _pending.ContainsKey(id) || _renameId == id) return;
            var session = _session;
            _pending[id] = Generating;
            _actionError = null;
            Render();
            KeepFocusOnRow(id);
            try { await handler(id); }
            catch (Exception error)
            {
                if (session != _session) return;
                _pending.Remove(id);
                Fail("Der Titel konnte nicht generiert werden.", error);
                KeepFocusOnRow(id);
                return;
            }
            if (session != _session) return;
            _pending.Remove(id);
            Render();
            KeepFocusOnRow(id);
        }

        // Focus that the row lost to a rebuild or the closed menu returns to
        // the row; focus the user moved elsewhere in the bar stays there.
        private void KeepFocusOnRow(string id)
        {
            Defer(() => {
                var focused = Keyboard.FocusedElement as DependencyObject;
                if (focused != null && focused != _frame && IsWithin(focused, _frame) && !IsWithin(focused, _overlay)) return;
                if (!_frame.IsKeyboardFocusWithin && focused != null && !IsWithin(focused, _overlay)) return;
                var target = _targets.FirstOrDefault(row => row.Id == id);
                if (target == null || !FocusElement(target.Primary)) FocusElement(_search);
            });
        }

        // "Als erledigt markieren": the existing archive action. The host
        // keeps the history and opens another chat when this one was open.
        private async Task ArchiveEntryAsync(string id)
        {
            var entry = Find(id);
            var handler = ArchiveAsync;
            if (entry == null || entry.Archived || entry.Busy || handler == null || _pending.ContainsKey(id)) return;
            var session = _session;
            var content = _content;
            var next = NeighbourId(id);
            _pending[id] = Archiving;
            _actionError = null;
            Render();
            try { await handler(id); }
            catch (Exception error)
            {
                if (session != _session) return;
                _pending.Remove(id);
                Fail("Als erledigt markieren fehlgeschlagen.", error);
                return;
            }
            if (session != _session) return;
            _pending.Remove(id);
            if (content == _content) entry.Archived = true;
            Render();
            if (next != null) FocusRow(next); else Defer(() => FocusElement(_search));
        }

        private async Task RestoreEntryAsync(string id)
        {
            var entry = Find(id);
            var handler = RestoreAsync;
            if (entry == null || !entry.Archived || handler == null || _pending.ContainsKey(id)) return;
            var session = _session;
            var content = _content;
            var next = NeighbourId(id);
            _pending[id] = Restoring;
            _actionError = null;
            Render();
            try { await handler(id); }
            catch (Exception error)
            {
                if (session != _session) return;
                _pending.Remove(id);
                Fail("Wieder öffnen fehlgeschlagen.", error);
                return;
            }
            if (session != _session) return;
            _pending.Remove(id);
            if (content == _content) (Find(id) ?? entry).Archived = false;
            Render();
            if (next != null) FocusRow(next); else Defer(() => FocusElement(_search));
        }

        private async Task CreateChatAsync()
        {
            var handler = NewAsync;
            if (_disposed || !_open || handler == null || _newPending) return;
            var session = _session;
            _newPending = true;
            _actionError = null;
            Render();
            try { await handler(); }
            catch (Exception error)
            {
                if (session != _session) return;
                _newPending = false;
                Fail("Neuer Chat konnte nicht erstellt werden.", error);
                return;
            }
            if (session != _session) return;
            _newPending = false;
            if (!_docked) CloseCore(false); else Render();
        }

        // The host reloads and answers with Show or ShowError; only a thrown
        // retry that nothing else answered is reported here.
        private async Task RetryLoadAsync()
        {
            var handler = RetryAsync;
            if (_disposed || !_open || handler == null || _retryPending) return;
            var session = _session;
            var content = _content;
            _retryPending = true;
            Render();
            try { await handler(); }
            catch (Exception error)
            {
                if (session != _session) return;
                _retryPending = false;
                if (content != _content) return;
                _view = PickerView.Error;
                _errorMessage = Describe("Erneuter Versuch fehlgeschlagen.", error) ?? _errorMessage;
                Render();
                return;
            }
            if (session != _session) return;
            _retryPending = false;
            if (content == _content) Render();
        }

        private void Fail(string fallback, Exception error)
        {
            var message = Describe(fallback, error);
            if (message != null) _actionError = message;
            Render();
        }

        // Only InvalidOperationException carries a user-facing host message;
        // anything else gets the neutral fallback. Cancellation stays silent.
        // A host message that already opens with the fallback sentence is
        // shown once, as sent, instead of repeating that sentence.
        private static string Describe(string fallback, Exception error)
        {
            if (error is AggregateException aggregate && aggregate.InnerExceptions.Count == 1) error = aggregate.InnerExceptions[0];
            if (error is OperationCanceledException) return null;
            var detail = error is InvalidOperationException ? error.Message?.Trim() : null;
            if (string.IsNullOrEmpty(detail)) return fallback;
            return RepeatsFallback(detail, fallback) ? detail : fallback + " " + detail;
        }

        // True when the detail starts with the fallback sentence, with or
        // without its closing period, followed by nothing or punctuation/space.
        private static bool RepeatsFallback(string detail, string fallback)
        {
            var sentence = fallback.TrimEnd('.', ' ');
            if (sentence.Length == 0 || !detail.StartsWith(sentence, StringComparison.OrdinalIgnoreCase)) return false;
            return detail.Length == sentence.Length || !char.IsLetterOrDigit(detail[sentence.Length]);
        }

        // ---- Keyboard ----

        private void OnPreviewKeyDown(object sender, KeyEventArgs args)
        {
            var key = args.Key == Key.System ? args.SystemKey : args.Key;
            // Key events route from the focused element; OriginalSource names it.
            var source = args.OriginalSource as DependencyObject;
            if (key == Key.Escape)
            {
                if (_menuId != null) { args.Handled = true; CloseMenu(true); return; }
                if (_renameId != null && IsWithin(source, _renameEditor) && !(_pending.TryGetValue(_renameId, out var pending) && pending == Renaming))
                { args.Handled = true; CancelRename(); return; }
                if (IsWithin(source, _search) && _search.Text.Length > 0) { args.Handled = true; ClearSearch(); return; }
                // The docked bar is part of the layout; Escape leaves it open.
                if (_docked) return;
                args.Handled = true;
                CloseCore(true);
                return;
            }
            if (_menuId != null)
            {
                if (key == Key.Down || key == Key.Up)
                {
                    args.Handled = true;
                    MoveWithin(_menuItems.Children.OfType<Button>().Where(button => button.IsEnabled).Cast<Control>().ToList(), source, key == Key.Down);
                }
                return;
            }
            if (IsWithin(source, _renameEditor)) return;
            if (IsWithin(source, _search))
            {
                if (key == Key.Down && _targets.Count > 0) { args.Handled = true; FocusElement(_targets[0].Primary); }
                else if (key == Key.Enter && _search.Text.Trim().Length > 0)
                {
                    var first = _targets.FirstOrDefault(row => Find(row.Id)?.Archived == false);
                    if (first != null) { args.Handled = true; var id = first.Id; Run(() => SelectEntryAsync(id)); }
                }
                return;
            }
            var index = _targets.FindIndex(row => IsWithin(source, row.Primary) || (row.More != null && IsWithin(source, row.More)));
            if (index < 0) return;
            var current = _targets[index];
            switch (key)
            {
                case Key.Down:
                    args.Handled = true;
                    if (index + 1 < _targets.Count) FocusElement(_targets[index + 1].Primary);
                    break;
                case Key.Up:
                    args.Handled = true;
                    if (index > 0) FocusElement(_targets[index - 1].Primary); else FocusElement(_search);
                    break;
                case Key.Home:
                    args.Handled = true; FocusElement(_targets[0].Primary);
                    break;
                case Key.End:
                    args.Handled = true; FocusElement(_targets[_targets.Count - 1].Primary);
                    break;
                case Key.Left:
                    if (current.Id == DoneToggleId && _showArchived) { args.Handled = true; SetArchived(false); }
                    break;
                case Key.Right:
                    if (current.Id == DoneToggleId && !_showArchived) { args.Handled = true; SetArchived(true); }
                    break;
                case Key.F2:
                    args.Handled = true; BeginRename(current.Id);
                    break;
                case Key.Apps:
                    args.Handled = true; OpenMenu(current.Id);
                    break;
                case Key.F10:
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { args.Handled = true; OpenMenu(current.Id); }
                    break;
            }
        }

        private static void MoveWithin(List<Control> items, DependencyObject source, bool forward)
        {
            if (items.Count == 0) return;
            var index = items.FindIndex(item => IsWithin(source, item));
            index = index < 0 ? 0 : (index + (forward ? 1 : -1) + items.Count) % items.Count;
            FocusElement(items[index]);
        }

        private static bool IsWithin(DependencyObject node, DependencyObject container)
        {
            for (; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (node == container) return true;
            return false;
        }

        private string FocusedRowId(out bool more)
        {
            more = false;
            var source = Keyboard.FocusedElement as DependencyObject;
            if (source == null || !_list.IsKeyboardFocusWithin) return null;
            foreach (var row in _targets)
            {
                if (row.More != null && IsWithin(source, row.More)) { more = true; return row.Id; }
                if (IsWithin(source, row.Primary)) return row.Id;
            }
            return null;
        }

        private void FocusRow(string id)
        {
            Defer(() => {
                var target = _targets.FirstOrDefault(row => row.Id == id);
                if (target == null || !FocusElement(target.Primary)) FocusElement(_search);
            });
        }

        private static bool FocusElement(UIElement element)
        {
            if (element == null || !element.IsVisible || !element.IsEnabled || !element.Focusable) return false;
            return element.Focus();
        }

        private void Defer(Action action)
        {
            var session = _session;
            _frame.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
                if (_disposed || session != _session || !_open) return;
                action();
            }));
        }

        // ---- Helpers ----

        private void SetArchived(bool expanded)
        {
            if (_showArchived == expanded) return;
            CloseMenu(false);
            if (_renameId != null && Find(_renameId)?.Archived == true) EndRename();
            _showArchived = expanded;
            Render();
            FocusRow(DoneToggleId);
        }

        private void ClearSearch()
        {
            _search.Text = "";
            FocusElement(_search);
        }

        private Entry Find(string id) { return _entries.FirstOrDefault(entry => entry.Id == id); }

        private string NeighbourId(string id)
        {
            var rows = _targets.Where(row => row.Id != DoneToggleId).ToList();
            var index = rows.FindIndex(row => row.Id == id);
            if (index < 0) return null;
            if (index + 1 < rows.Count) return rows[index + 1].Id;
            return index > 0 ? rows[index - 1].Id : null;
        }

        private static string DisplayTitle(Entry entry) { return string.IsNullOrWhiteSpace(entry.Title) ? "Unbenannter Chat" : entry.Title.Trim(); }

        private static string[] Terms(string query)
        {
            return (query ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // Case- and accent-insensitive; every term must occur in the title.
        private static bool Matches(Entry entry, string[] terms)
        {
            if (terms.Length == 0) return true;
            var title = DisplayTitle(entry);
            var compare = German.CompareInfo;
            return terms.All(term => compare.IndexOf(title, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth) >= 0);
        }

        internal static string FormatUpdated(long millis, DateTime now)
        {
            if (!TryLocal(millis, out var local)) return "";
            var age = now - local;
            if (age < TimeSpan.FromMinutes(-5)) return local.ToString("dd.MM.yyyy", German);
            if (age.TotalSeconds < 60) return "gerade eben";
            if (age.TotalMinutes < 60) return "vor " + (int)age.TotalMinutes + " Min.";
            if (local.Date == now.Date) return "heute, " + local.ToString("HH:mm", German);
            if (local.Date == now.Date.AddDays(-1)) return "gestern, " + local.ToString("HH:mm", German);
            if (local.Date > now.Date.AddDays(-7)) return local.ToString("ddd, HH:mm", German);
            return local.ToString(local.Year == now.Year ? "dd.MM." : "dd.MM.yyyy", German);
        }

        private static string FormatUpdatedFull(long millis)
        {
            return TryLocal(millis, out var local) ? "Letzte Aktivität: " + local.ToString("dddd, dd.MM.yyyy, HH:mm", German) : null;
        }

        private static bool TryLocal(long millis, out DateTime local)
        {
            local = default(DateTime);
            if (millis <= 0) return false;
            try { local = DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime; return true; }
            catch (ArgumentOutOfRangeException) { return false; }
        }

#pragma warning disable VSTHRD100 // UI command boundary; exceptions never leave it.
        private static async void Run(Func<Task> action) { try { await action(); } catch { /* Never leak into the IDE. */ } }
#pragma warning restore VSTHRD100

        private static UIElement DockTo(UIElement element, Dock dock) { DockPanel.SetDock(element, dock); return element; }

        private Button IconButton(string icon, string tooltip, Func<Task> action)
        {
            var button = new Button { ToolTip = tooltip };
            button.SetResourceReference(FrameworkElement.StyleProperty, "Tc.IconButton");
            var path = Glyph(icon);
            path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            button.Content = path;
            AutomationProperties.SetName(button, tooltip);
            button.Click += (sender, args) => Run(action);
            return button;
        }

        private static Button SmallButton(string label, string style)
        {
            var button = new Button { Content = label, FontSize = 12 };
            button.SetResourceReference(FrameworkElement.StyleProperty, style);
            button.Height = 24;
            button.Padding = new Thickness(9, 0, 9, 0);
            return button;
        }

        private static ShapePath Glyph(string name, double scale = 1)
        {
            Geometry data;
            switch (name)
            {
                case "search": data = Geometry.Parse("M11.5,7 A4.5,4.5 0 1 1 2.5,7 A4.5,4.5 0 1 1 11.5,7 Z M10.3,10.3 L13.5,13.5"); break;
                case "pencil": data = Geometry.Parse("M10.6,2.7 L13.3,5.4 L5.8,12.9 L2.6,13.4 L3.1,10.2 Z M9.3,4 L12,6.7"); break;
                case "back": data = Geometry.Parse("M9.8,3.5 L5.3,8 L9.8,12.5"); break;
                case "alert": data = Geometry.Parse("M14,8 A6,6 0 1 1 2,8 A6,6 0 1 1 14,8 Z M8,4.9 L8,8.7 M8,11 L8,11.1"); break;
                // Panel with its left pane: the bar itself.
                case "sidebar": data = Geometry.Parse("M3.5,2.5 L12.5,2.5 A1,1 0 0 1 13.5,3.5 L13.5,12.5 A1,1 0 0 1 12.5,13.5 L3.5,13.5 A1,1 0 0 1 2.5,12.5 L2.5,3.5 A1,1 0 0 1 3.5,2.5 Z M6.5,2.5 L6.5,13.5"); break;
                default: data = AssistantTheme.Icon(name); break;
            }
            var path = new ShapePath { Data = data, Width = 16, Height = 16, Stretch = Stretch.None, StrokeThickness = name == "more" ? 2.4 : 1.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            if (scale != 1) path.LayoutTransform = new ScaleTransform(scale, scale);
            return path;
        }

        // Animations run only while visible, so a closed bar costs nothing.
        private static FrameworkElement Spinner(double size)
        {
            var arc = new ShapePath { Data = Geometry.Parse("M8,2 A6,6 0 1 1 2,8"), Width = 16, Height = 16, Stretch = Stretch.None, StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransformOrigin = new Point(0.5, 0.5) };
            arc.SetResourceReference(Shape.StrokeProperty, "Tc.Primary");
            var rotation = new RotateTransform();
            arc.RenderTransform = rotation;
            var box = new Viewbox { Width = size, Height = size, Child = arc, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(box, "Wird geladen");
            box.IsVisibleChanged += (sender, args) => rotation.BeginAnimation(RotateTransform.AngleProperty,
                box.IsVisible && SystemParameters.ClientAreaAnimation ? new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever } : null);
            return box;
        }

        private static void Pulse(UIElement element)
        {
            element.IsVisibleChanged += (sender, args) => element.BeginAnimation(UIElement.OpacityProperty,
                element.IsVisible && SystemParameters.ClientAreaAnimation ? new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null);
        }

        // Slim themed scrollbar for the list; the system scrollbar ignores the dark palette.
        private const string ScrollBarXaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Tc.PickerThumb' TargetType='Thumb'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border x:Name='T' CornerRadius='3' Background='{DynamicResource Tc.Muted}' Opacity='0.4'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='T' Property='Opacity' Value='0.65'/></Trigger>
            <Trigger Property='IsDragging' Value='True'><Setter TargetName='T' Property='Opacity' Value='0.8'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='8'/>
    <Setter Property='MinWidth' Value='8'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True' Margin='1,2,1,2'>
            <Track.Thumb><Thumb Style='{StaticResource Tc.PickerThumb}'/></Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
    }
}
