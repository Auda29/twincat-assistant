using System;
using Process = System.Diagnostics.Process;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TwinCatAssistant.Native
{
    // OpenChamber owns chat/auth/session/model behavior. This host never injects
    // source into chat, adds a generic COM bridge, or handles model credentials.
    public sealed class AssistantControl : UserControl, IDisposable
    {
        private readonly TextBlock _status = new TextBlock { Text = "Read-only. No native IDE context bound.", TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _port = new TextBox { Text = "3123", Width = 70, Margin = new Thickness(4) };
        private readonly ComboBox _targets = new ComboBox { MinWidth = 180, Margin = new Thickness(4), DisplayMemberPath = "Name" };
        private readonly TextBox _source = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
        private readonly WebView2 _browser = new WebView2();
        private readonly StackPanel _toolbar = new StackPanel();
        private NativeProjectReader _reader;
        private Uri _origin;
        private bool _connecting, _disposed;
        private Events _events;
        private SolutionEvents _solutionEvents;

        public AssistantControl()
        {
            var dock = new DockPanel();
            DockPanel.SetDock(_toolbar, Dock.Top);
            _toolbar.Children.Add(_status);
            var row = new WrapPanel();
            row.Children.Add(new TextBlock { Text = "OpenChamber port:", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(_port);
            row.Children.Add(AsyncButton("Connect chat", ConnectAsync));
            row.Children.Add(Button("Refresh IDE context", Refresh));
            row.Children.Add(_targets);
            row.Children.Add(Button("Read locally", Read));
            row.Children.Add(Button("Hide source", () => { _source.Clear(); _source.Visibility = Visibility.Collapsed; }));
            _toolbar.Children.Add(row);
            dock.Children.Add(_toolbar);
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _source.MaxHeight = 250;
            Grid.SetRow(_source, 1);
            grid.Children.Add(_browser);
            grid.Children.Add(_source);
            dock.Children.Add(grid);
            Content = dock;
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

        public void Bind(DTE dte, Func<string, bool?> dirty)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed || dte == null) return;
            if (_solutionEvents != null) { _solutionEvents.Opened -= Invalidate; _solutionEvents.AfterClosing -= Invalidate; }
            _events = dte.Events; // retain event source COM wrappers
            _solutionEvents = _events.SolutionEvents;
            _solutionEvents.Opened += Invalidate;
            _solutionEvents.AfterClosing += Invalidate;
            _reader = new NativeProjectReader(dte, Process.GetCurrentProcess().Id, dirty);
            Refresh();
        }

        private void Invalidate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _reader?.Invalidate();
            _targets.ItemsSource = null;
            _source.Clear();
            _source.Visibility = Visibility.Collapsed;
            _status.Text = "Solution changed. Refresh context. Chat is not automatically bound to the IDE solution.";
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
                _status.Text = "Read-only IDE PID " + context.IdeProcessId + ": " + context.Targets.Count
                    + " ST targets. " + string.Join(" ", context.Warnings) + " Chat has no automatic native source attachment.";
            }
            catch { _status.Text = "Native context unavailable; refresh when the IDE is ready."; }
        }

        private void Read()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _source.Clear();
            try
            {
                var target = _targets.SelectedItem as NativeTarget;
                if (target == null || _reader == null) return;
                var read = _reader.ReadTarget(target.Id);
                _status.Text = "Local read only. Dirty state: " + read.Dirty.State + ". " + read.Dirty.Reason + " No source sent to chat.";
                _source.Text = read.Declaration + Environment.NewLine + read.Implementation;
                _source.Visibility = Visibility.Visible;
            }
            catch { _status.Text = "Native read rejected; refresh the exact solution context."; }
        }

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            if (_connecting || _disposed) return;
            int port;
            if (!int.TryParse(_port.Text, out port) || port < 1024 || port > 65535) { _status.Text = "Enter a loopback port between 1024 and 65535."; return; }
            _connecting = true;
            try
            {
                var origin = new Uri("http://127.0.0.1:" + port + "/");
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
                    _browser.CoreWebView2.NavigationStarting += (sender, args) => { if (!Allowed(args.Uri)) args.Cancel = true; };
                    _browser.CoreWebView2.FrameNavigationStarting += (sender, args) => { if (!Allowed(args.Uri)) args.Cancel = true; };
                    _browser.CoreWebView2.NewWindowRequested += (sender, args) => { args.Handled = true; };
                    _browser.CoreWebView2.PermissionRequested += (sender, args) => { args.State = CoreWebView2PermissionState.Deny; };
                    _browser.CoreWebView2.DownloadStarting += (sender, args) => { args.Cancel = true; };
                    _browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    _browser.CoreWebView2.WebResourceRequested += (sender, args) => {
                        if (!Allowed(args.Request.Uri)) args.Response = environment.CreateWebResourceResponse(null, 403, "Blocked external request", "");
                    };
                }
                _origin = origin;
                _browser.CoreWebView2.Navigate(origin.AbsoluteUri);
                _status.Text = "OpenChamber loopback chat. Sign in using its own login. Native source remains local; provider use requires explicit opt-in in the launcher profile.";
            }
            catch { if (!_disposed) _status.Text = "WebView2 initialization failed. Check the runtime, host architecture and supervised OpenChamber service."; }
            finally { _connecting = false; }
        }

        private bool Allowed(string url)
        {
            Uri uri;
            return _origin != null && Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == _origin.Scheme
                && uri.Host == _origin.Host && uri.Port == _origin.Port && string.IsNullOrEmpty(uri.UserInfo);
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            _disposed = true;
            if (_solutionEvents != null) { _solutionEvents.Opened -= Invalidate; _solutionEvents.AfterClosing -= Invalidate; }
            _reader = null;
            _source.Clear();
            _browser.Dispose();
        }
    }
}
