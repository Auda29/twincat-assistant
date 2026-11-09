using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private readonly Grid _splash = new Grid();
        private readonly TranslateTransform _splashRun = new TranslateTransform(50, 0);
        private readonly DispatcherTimer _chatReadyTimeout = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromSeconds(60) };
        private bool _splashRunning, _chatPresented;

        private UIElement BuildSplash()
        {
            _chatReadyTimeout.Tick += OnChatReadinessTimeout;
            _splash.SetResourceReference(Panel.BackgroundProperty, "Tc.Background");
            var mark = new TextBlock { FontFamily = new FontFamily("Bahnschrift, Segoe UI"), FontSize = 30 };
            var twinCat = new Run("TwinCAT") { FontWeight = FontWeights.SemiBold };
            twinCat.SetResourceReference(TextElement.ForegroundProperty, "Tc.Foreground");
            var assistant = new Run(" Assistant") { FontWeight = FontWeights.Light };
            assistant.SetResourceReference(TextElement.ForegroundProperty, "Tc.Muted");
            mark.Inlines.Add(twinCat); mark.Inlines.Add(assistant);
            var wordmark = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = mark };
            var run = new Rectangle { Width = 40, Height = 2, RadiusX = 1, RadiusY = 1, RenderTransform = _splashRun };
            run.SetResourceReference(Shape.FillProperty, "Tc.Primary");
            var track = new Canvas { Width = 140, Height = 2, ClipToBounds = true, Margin = new Thickness(0, 14, 0, 12), Children = { run } };
            track.SetResourceReference(Panel.BackgroundProperty, "Tc.Border");
            var status = new TextBlock { Text = "Chat wird geladen …", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
            status.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            _splash.Children.Add(new StackPanel { Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Children = { wordmark, track, status } });
            return _splash;
        }

        private void ResetLoading()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            CloseProjectChats();
            _chatPresented = false;
            _chatReadyTimeout.Stop();
            _menuPopup.IsOpen = false;
            _previewPopup.IsOpen = false;
        }

        // WebView2 keeps loading and running the adapter while hidden. The
        // native wordmark remains visible until this document is ready;
        // OpenChamber's startup screen stays hidden during that handoff.
        private void RenderLoading()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            var failed = _setupMissing || _chatConnection.Phase == "failed" || ProjectSetupFailed;
            if (failed) _chatPresented = false;
            else if (_chatConnection.PageLoaded && _chatAdapterReady && CanPresentChat())
                _chatPresented = true;
            var loading = !_chatPresented && !failed;
            _strip.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
            _splash.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            _browser.Visibility = failed ? Visibility.Collapsed : loading ? Visibility.Hidden : Visibility.Visible;
            if (!loading) _chatReadyTimeout.Stop();
            var animate = loading && SystemParameters.ClientAreaAnimation;
            if (animate != _splashRunning)
            {
                _splashRunning = animate;
                _splashRun.BeginAnimation(TranslateTransform.XProperty, animate ? new DoubleAnimation(-40, 140, TimeSpan.FromMilliseconds(1200))
                    { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } } : null);
            }
        }

        private bool CanPresentChat()
        {
            // After automatic sign-in retries, allow manual login. Do not wait
            // for authenticated history on the sign-in screen itself.
            if (_chatSelection?.authenticated != true) return _signInAttempts >= 3;
            if (!_chatSelection.presentationReady || _chatActionPending) return false;
            if (_chatProject == null) return !_chatSelection.IsSelected || CanPromptCurrentChat();
            var key = _chatProject.SolutionFile + "\0" + _chatProject.ProjectFile + "\0" + _chatDocumentGeneration;
            return _autoProjectKey == key && (HasCurrentChatBinding()
                || ((!_mcpEnabled || _explicitGeneralRequested) && _chatBinding?.IsGeneral == true && CanPromptCurrentChat()));
        }

        private void OnChatReadinessTimeout(object sender, EventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _chatReadyTimeout.Stop();
            if (_disposed || _chatPresented || _chatConnection.Phase == "failed" || _projectSetupPending || ProjectSetupFailed) return;
            _chatConnection.Fail(_chatConnection.Generation, "Der Chat wurde nicht rechtzeitig bereit. Verbindung und Chat-Integration unter Diagnose prüfen und neu verbinden.");
            ShowChatStatus();
        }
    }
}
