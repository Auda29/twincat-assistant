using System;
using Process = System.Diagnostics.Process;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TwinCatAssistant.Native
{
    // OpenChamber owns chat/auth/session/model behavior. This host never injects
    // source into chat, adds a generic COM bridge, or handles model credentials.
    public sealed class AssistantControl : UserControl, IDisposable
    {
        private readonly TextBlock _objectName = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _scope = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        private readonly TextBlock _evidence = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBlock _sourceNotice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        private readonly TextBlock _chatStatus = new TextBlock { Text = "Connect chat starts the protected local service if needed.", TextWrapping = TextWrapping.Wrap };
        private readonly ChatConnectionState _chatConnection = new ChatConnectionState();
        private readonly NativeChatCredentials _chatCredentials = new NativeChatCredentials();
        private readonly PasswordBox _chatPassword = new PasswordBox { MinWidth = 160, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
        private readonly PasswordBox _chatPasswordConfirmation = new PasswordBox { MinWidth = 160, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
        private readonly StackPanel _chatSetup = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 4) };
        private readonly DispatcherTimer _chatHealth;
        private NativeChatSession _chatSession;
        private bool _checkingChatHealth;
        private long _navigationGeneration;
        private readonly TextBlock _diagnosticStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _inventoryStatus = new TextBlock { Text = "Inventory not loaded. Active editor following does not use this catalog.", TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _port = new TextBox { Text = "3123", Width = 70, Margin = new Thickness(4) };
        private readonly ComboBox _targets = new ComboBox { MinWidth = 180, Margin = new Thickness(4), DisplayMemberPath = "Name" };
        private readonly TextBox _source = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
        private WebView2 _browser = new WebView2();
        private bool _browserNeedsReset;
        private readonly StackPanel _toolbar = new StackPanel();
        private readonly TextBox _diagnosticSource = new TextBox { IsReadOnly = true, AcceptsReturn = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New"), MaxHeight = 180,
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
            var dock = new DockPanel();
            DockPanel.SetDock(_toolbar, Dock.Top);
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            _toolbar.Margin = new Thickness(10);
            _toolbar.Children.Add(_objectName);
            _toolbar.Children.Add(_scope);
            _toolbar.Children.Add(_evidence);
            _toolbar.Children.Add(_sourceNotice);
            _toolbar.Children.Add(new TextBlock { Text = ActiveEditorPresentation.Disclosure,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) });
            var connection = new WrapPanel();
            connection.Children.Add(AsyncButton("Connect chat", ConnectAsync));
            _toolbar.Children.Add(connection);
            _toolbar.Children.Add(_chatStatus);
            _chatSetup.Children.Add(new TextBlock { Text = "Choose a local chat password (at least 12 characters). Use the same password in OpenChamber's login.", TextWrapping = TextWrapping.Wrap });
            _chatSetup.Children.Add(_chatPassword);
            _chatSetup.Children.Add(new TextBlock { Text = "Confirm password", TextWrapping = TextWrapping.Wrap });
            _chatSetup.Children.Add(_chatPasswordConfirmation);
            _chatSetup.Children.Add(new TextBlock { Text = "Stored encrypted for your Windows account. Then press Start local chat.", TextWrapping = TextWrapping.Wrap });
            _chatSetup.Children.Add(AsyncButton("Start local chat", ConnectAsync));
            _toolbar.Children.Add(_chatSetup);
            var diagnostics = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var row = new WrapPanel();
            row.Children.Add(new TextBlock { Text = "OpenChamber port:", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(_port);
            row.Children.Add(Button("Refresh IDE context", Refresh));
            row.Children.Add(_targets);
            row.Children.Add(Button("Read locally", Read));
            diagnostics.Children.Add(row);
            diagnostics.Children.Add(_inventoryStatus);
            diagnostics.Children.Add(_diagnosticStatus);
            diagnostics.Children.Add(_diagnosticSource);
            _toolbar.Children.Add(ThemedExpander("Diagnostics", diagnostics, new Thickness(0, 8, 0, 0)));
            dock.Children.Add(_toolbar);
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _source.MaxHeight = 220;
            var sourcePreview = ThemedExpander("Local ST preview", _source, new Thickness(10, 0, 10, 6));
            Grid.SetRow(sourcePreview, 1);
            grid.Children.Add(_browser);
            grid.Children.Add(sourcePreview);
            dock.Children.Add(grid);
            Content = dock;
            _chatHealth = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            _chatHealth.Tick += CheckChatHealth;
            RenderContext(null, null);
        }

        private Expander ThemedExpander(string header, object content, Thickness margin)
        {
            var expander = new Expander { Header = header, Content = content, Margin = margin };
            // Default WPF expander styles set their own foreground. Bind it to
            // the host surface so headers remain visible after a theme change.
            expander.SetBinding(ForegroundProperty, new Binding("Foreground") { Source = this });
            return expander;
        }

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

        public void Bind(DTE dte, IVsMonitorSelection selection, IVsRunningDocumentTable rdt, Func<string, bool?> dirty)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || dte == null) return;
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
            try { _tracker = new ActiveEditorTracker(dte, selection, rdt, dirty, RenderContext); }
            catch { RenderContext(null, "Native editor services are unavailable. Reopen the tool window to retry."); }
        }

        private void RenderContext(ActiveEditorSnapshot snapshot, string reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            var view = ActiveEditorPresentation.From(snapshot, reason);
            if (_objectName.Text != view.Title) _objectName.Text = view.Title;
            if (_scope.Text != view.Scope) _scope.Text = view.Scope;
            if (_evidence.Text != view.Evidence) _evidence.Text = view.Evidence;
            var notice = snapshot == null ? view.Detail : "Unsaved editor text is not verified.";
            if (_sourceNotice.Text != notice) _sourceNotice.Text = notice;
            _sourceNotice.ToolTip = view.Detail;
            if (_source.Text != view.Source) _source.Text = view.Source;
            _source.Visibility = snapshot == null ? Visibility.Collapsed : Visibility.Visible;
            _objectName.ToolTip = snapshot == null ? null : "IDE PID " + snapshot.Identity.IdeProcessId
                + Environment.NewLine + snapshot.Identity.SolutionFile + Environment.NewLine + snapshot.Identity.ProjectFile
                + Environment.NewLine + snapshot.Identity.TreePath;
            _evidence.ToolTip = snapshot == null ? null : snapshot.Identity.DocumentMoniker;
        }

        private void Invalidate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _reader?.Invalidate();
            _targets.ItemsSource = null;
            _diagnosticSource.Clear();
            _inventoryStatus.Text = "Inventory not loaded. Active editor following does not use this catalog.";
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
                    + " catalog targets. " + string.Join(" ", context.Warnings);
                _diagnosticStatus.Text = _tracker == null ? "Active editor tracker unavailable."
                    : "Active read: " + _tracker.LastReadMilliseconds.ToString("F1") + " ms; maximum: "
                        + _tracker.MaxReadMilliseconds.ToString("F1") + " ms. No source attached to chat.";
            }
            catch { _diagnosticStatus.Text = "Diagnostic inventory unavailable; refresh when the IDE is ready."; }
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
                _diagnosticStatus.Text = "Manual diagnostic read. Dirty state: " + read.Dirty.State + ". " + read.Dirty.Reason;
                _diagnosticSource.Text = read.Declaration + Environment.NewLine + read.Implementation;
            }
            catch { _diagnosticStatus.Text = "Diagnostic read rejected; refresh the exact solution context."; }
        }

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            if (_connecting || _disposed) return;
            int port;
            if (!int.TryParse(_port.Text, out port) || port < 1024 || port > 65535) { _chatStatus.Text = "Enter a loopback port between 1024 and 65535 in Diagnostics."; return; }
            _connecting = true;
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
                    try { config = NativeChatConfiguration.Load(); }
                    catch
                    {
                        _diagnosticStatus.Text = "Native chat setup: run scripts/Configure-NativeChat.ps1 with the repository, Node and .NET SDK paths. Config: " + NativeChatConfiguration.ConfigurationPath;
                        _chatConnection.Fail(generation, "Local chat setup is missing or invalid. See Diagnostics for setup instructions.");
                        ShowChatStatus();
                        return;
                    }
                    if (!_chatCredentials.Exists)
                    {
                        if (_chatSetup.Visibility != Visibility.Visible)
                        {
                            _chatSetup.Visibility = Visibility.Visible;
                            _chatConnection.Fail(generation, "Set up your local chat password below, then press Connect chat again.");
                            ShowChatStatus();
                            _chatPassword.Focus();
                            return;
                        }
                        var password = _chatPassword.Password;
                        if (password != _chatPasswordConfirmation.Password || password.Length < 12 || password.Length > 1024 || string.IsNullOrWhiteSpace(password))
                        {
                            _chatConnection.Fail(generation, "Enter the same password twice, using 12 to 1024 characters.");
                            ShowChatStatus();
                            return;
                        }
                        _chatCredentials.Save(password);
                        _chatPassword.Clear(); _chatPasswordConfirmation.Clear();
                    }
                    _chatSetup.Visibility = Visibility.Collapsed;
                    _chatStatus.Text = "Starting the protected local chat service...";
                    var session = new NativeChatSession();
                    _chatSession = session;
                    session.Stopped += () => OnChatServiceStopped(session);
                    await session.StartAsync(config, port, _chatCredentials.Load());
                    if (_disposed) { session.Dispose(); return; }
                }
                _chatSetup.Visibility = Visibility.Collapsed;
                _chatConnection.Loading(generation);
                ShowChatStatus();
                if (_browserNeedsReset)
                {
                    var oldBrowser = _browser;
                    var grid = (Grid)oldBrowser.Parent;
                    _browser = new WebView2();
                    grid.Children.Remove(oldBrowser);
                    oldBrowser.Dispose();
                    grid.Children.Insert(0, _browser);
                    _browserNeedsReset = false;
                }
                _browser.Visibility = Visibility.Visible;
                if (_browser.CoreWebView2 == null)
                {
                    var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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
                        _chatConnection.Fail(_chatConnection.Generation, "The chat browser stopped. Press Connect chat to recreate it.");
                        ShowChatStatus();
                    };
                    _browser.CoreWebView2.FrameNavigationStarting += (sender, args) => { if (!Allowed(args.Uri)) args.Cancel = true; };
                    _browser.CoreWebView2.NewWindowRequested += (sender, args) => { args.Handled = true; };
                    _browser.CoreWebView2.PermissionRequested += (sender, args) => { args.State = CoreWebView2PermissionState.Deny; };
                    _browser.CoreWebView2.DownloadStarting += (sender, args) => { args.Cancel = true; };
                    _browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    _browser.CoreWebView2.WebResourceRequested += (sender, args) => {
                        if (!AllowedResource(args.Request.Uri)) args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked external request", "");
                    };
                }
                _origin = origin;
                _navigationGeneration = generation;
                _browser.CoreWebView2.Navigate(origin.AbsoluteUri);
                _chatHealth.Start();
            }
            catch
            {
                if (!_disposed)
                {
                    _chatConnection.Fail(generation, "Chat connection failed. Check runtime setup, the port and any existing profile lock in Diagnostics, then retry.");
                    ShowChatStatus();
                }
            }
            finally { _connecting = false; }
        }

        private void OnChatNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || !ReferenceEquals(sender, _browser.CoreWebView2) || !Allowed(args.Uri)) { args.Cancel = true; return; }
            _navigationGeneration = _chatConnection.Generation;
            _chatConnection.Navigating(args.NavigationId);
            ShowChatStatus();
        }
        private void OnChatNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || !ReferenceEquals(sender, _browser.CoreWebView2) || _navigationGeneration != _chatConnection.Generation) return;
            _chatConnection.Completed(args.NavigationId, args.IsSuccess, args.HttpStatusCode, args.WebErrorStatus.ToString());
            ShowChatStatus();
        }
        private void ShowChatStatus()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            _chatStatus.Text = _chatConnection.Message;
            if (_chatConnection.Phase == "failed") _browser.Visibility = Visibility.Collapsed;
        }
#pragma warning disable VSTHRD100 // WPF timer callback; exceptions stay inside the UI boundary.
        private async void OnChatServiceStopped(NativeChatSession session)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed || _chatSession != session) return;
                _chatConnection.Fail(_chatConnection.Generation, "The local chat service stopped. Press Connect chat to restart it.");
                ShowChatStatus();
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
                        _chatConnection.Fail(generation, "The local chat service is no longer reachable. Press Connect chat to reconnect.");
                        ShowChatStatus();
                        _chatHealth.Stop();
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
            _chatPassword.Clear(); _chatPasswordConfirmation.Clear();
            _chatSession?.Dispose();
            _chatSession = null;
            _tracker?.Dispose();
            _tracker = null;
            if (_solutionEvents != null) { _solutionEvents.Opened -= Invalidate; _solutionEvents.AfterClosing -= Invalidate; }
            _reader = null;
            _boundDte = null;
            _source.Clear();
            _diagnosticSource.Clear();
            _browser.Dispose();
        }
    }
}
