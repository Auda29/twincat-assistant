using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;

namespace TwinCatAssistant.Native
{
    // Diagnose panel: one status line per component with a colored dot and a
    // short value. The full explanatory texts stay available as tooltips and in
    // the copied report; developer tools live under "Erweitert".
    public sealed partial class AssistantControl
    {
        private sealed class DiagnosticRow
        {
            public string Label, Detail;
            public Action Command;
            public readonly Grid Root = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent };
            public readonly Ellipse Dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            public readonly TextBlock Value = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            public readonly Button Action = new Button { Visibility = Visibility.Collapsed };
        }

        private readonly StackPanel _diagnosticRows = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        private readonly StackPanel _diagnosticAdvanced = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };
        private readonly ShapePath _advancedChevron = Icon("chevron", 16);
        private DiagnosticRow _chatRow, _modelRow, _toolsRow, _knowledgeRow, _helpRow;

        private void BuildDiagnostics()
        {
            _diagnostics.SetResourceReference(Border.BackgroundProperty, "Tc.Surface");
            _diagnostics.SetResourceReference(Border.BorderBrushProperty, "Tc.Border");
            _diagnostics.SetResourceReference(TextElement.ForegroundProperty, "Tc.Foreground");
            _port.SetResourceReference(StyleProperty, "Tc.TextBox");
            _diagnosticSource.SetResourceReference(StyleProperty, "Tc.TextBox");
            var title = new TextBlock { Text = "Diagnose", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var copy = Secondary(Button("Bericht kopieren", CopyDiagnosticReport));
            copy.Margin = new Thickness(0, 0, 4, 0); copy.Height = 26; copy.FontSize = 11.5;
            copy.ToolTip = "Kopiert den Status als Text, z. B. für eine Fehlermeldung. Enthält keinen Quelltext und keine Zugangsdaten.";
            var close = IconButton("close", "Diagnose ausblenden", () => { _diagnostics.Visibility = Visibility.Collapsed; return System.Threading.Tasks.Task.CompletedTask; });
            var head = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(close, Dock.Right); DockPanel.SetDock(copy, Dock.Right);
            head.Children.Add(close); head.Children.Add(copy); head.Children.Add(title);
            _chatRow = DiagnosticLine("Chat");
            _modelRow = DiagnosticLine("Modell");
            _toolsRow = DiagnosticLine("Projektwerkzeuge");
            _knowledgeRow = DiagnosticLine("Wissenssuche");
            _helpRow = DiagnosticLine("Beckhoff-Hilfe");
            // Developer tools: manual connection and local catalog reads.
            _advancedChevron.Margin = new Thickness(0, 0, 4, 0);
            var advancedLabel = new TextBlock { Text = "Erweitert", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var toggle = Button(null, () => ShowAdvancedDiagnostics(_diagnosticAdvanced.Visibility != Visibility.Visible));
            toggle.SetResourceReference(StyleProperty, "Tc.Button");
            toggle.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _advancedChevron, advancedLabel } };
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            toggle.Margin = new Thickness(-8, 4, 0, 0);
            toggle.SetResourceReference(ForegroundProperty, "Tc.Muted");
            advancedLabel.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = toggle });
            _advancedChevron.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = toggle });
            var hint = new TextBlock { Text = "Werkzeuge zur Fehlersuche. Gelesener Quelltext bleibt in diesem Fenster und geht nicht an den Chat.",
                FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            var connection = new WrapPanel { Margin = new Thickness(-4, 0, 0, 0) };
            connection.Children.Add(new TextBlock { Text = "Port", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            connection.Children.Add(_port);
            connection.Children.Add(Secondary(AsyncButton("Verbinden", ConnectAsync)));
            var catalog = new WrapPanel { Margin = new Thickness(-4, 0, 0, 0) };
            catalog.Children.Add(Secondary(Button("IDE-Kontext lesen", Refresh)));
            catalog.Children.Add(_targets);
            catalog.Children.Add(Secondary(Button("Lokal lesen", Read)));
            foreach (var text in new[] { _inventoryStatus, _diagnosticStatus })
            {
                text.FontSize = 11.5; text.Margin = new Thickness(0, 2, 0, 0);
                text.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            }
            _diagnosticSource.Margin = new Thickness(0, 4, 0, 0);
            foreach (UIElement element in new UIElement[] { hint, connection, catalog, _inventoryStatus, _diagnosticStatus, _diagnosticSource })
                _diagnosticAdvanced.Children.Add(element);
            ShowAdvancedDiagnostics(false);
            var panel = new StackPanel();
            foreach (UIElement element in new UIElement[] { head, _diagnosticRows, toggle, _diagnosticAdvanced })
                panel.Children.Add(element);
            _diagnostics.Child = panel;
            _diagnostics.IsVisibleChanged += (sender, args) => { if (_diagnostics.IsVisible) RenderDiagnostics(); };
        }

        private DiagnosticRow DiagnosticLine(string label)
        {
            var row = new DiagnosticRow { Label = label };
            foreach (var width in new[] { new GridLength(128), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            var name = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Muted");
            row.Value.SetResourceReference(TextBlock.ForegroundProperty, "Tc.Foreground");
            row.Action.SetResourceReference(StyleProperty, "Tc.SecondaryButton");
            row.Action.Height = 24; row.Action.Padding = new Thickness(8, 0, 8, 0); row.Action.FontSize = 11.5; row.Action.Margin = new Thickness(8, 0, 0, 0);
            row.Action.Click += (sender, args) => row.Command?.Invoke();
            Grid.SetColumn(row.Dot, 1); Grid.SetColumn(row.Value, 2); Grid.SetColumn(row.Action, 3);
            row.Root.Children.Add(name); row.Root.Children.Add(row.Dot); row.Root.Children.Add(row.Value); row.Root.Children.Add(row.Action);
            _diagnosticRows.Children.Add(row.Root);
            return row;
        }

        private void ShowAdvancedDiagnostics(bool visible)
        {
            _diagnosticAdvanced.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _advancedChevron.RenderTransformOrigin = new Point(0.5, 0.5);
            _advancedChevron.RenderTransform = new RotateTransform(visible ? 0 : -90);
        }

        private static void ShowRow(DiagnosticRow row, string value, string tone, string detail, string action = null, Action command = null)
        {
            if (row.Value.Text != value) row.Value.Text = value;
            row.Dot.SetResourceReference(Shape.FillProperty, "Tc." + tone);
            row.Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
            row.Root.ToolTip = row.Detail == null ? null : new TextBlock { Text = row.Detail, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
            row.Action.Content = action;
            row.Action.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
            row.Command = command;
        }

        // Live connection state that OpenChamber reports for a reviewed MCP server.
        private string McpState(string name)
        {
            return _chatSelection?.mcp?.FirstOrDefault(server => server?.name == name)?.status;
        }
        private static bool McpFailed(string status) { return status == "failed" || status == "needs_auth" || status == "disabled"; }
        private static void McpLabel(string status, out string value, out string tone)
        {
            switch (status)
            {
                case "connected": value = "Verbunden"; tone = "Success"; break;
                case "pending": value = "Verbindet …"; tone = "Neutral"; break;
                case "disabled": value = "Deaktiviert"; tone = "Warning"; break;
                case "needs_auth": value = "Anmeldung nötig"; tone = "Warning"; break;
                default: value = "Nicht verfügbar"; tone = "Error"; break;
            }
        }

        private void RenderDiagnostics()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_chatRow == null || _disposed) return;
            var ready = _chatAdapterReady && _chatSelection?.authenticated == true;
            string value, tone;
            // Chat service and sign-in.
            if (_setupMissing) ShowRow(_chatRow, "Nicht eingerichtet", "Error", _chatStatus.Text + " " + _diagnosticStatus.Text);
            else if (_chatConnection.Phase == "failed") ShowRow(_chatRow, "Getrennt", "Error", _chatStatus.Text, "Neu verbinden", Reconnect);
            else if (ready) ShowRow(_chatRow, "Verbunden", "Success", _chatStatus.Text);
            else if (_chatAdapterReady) ShowRow(_chatRow, "Anmeldung läuft …", "Neutral", _chatStatus.Text);
            else if (_chatConnection.Phase == "idle") ShowRow(_chatRow, "Nicht verbunden", "Neutral", _chatStatus.Text, "Verbinden", Reconnect);
            else ShowRow(_chatRow, "Verbindet …", "Neutral", _chatStatus.Text);
            // Model provider and model.
            var providers = _chatSelection?.providers ?? new ChatProviderChoice[0];
            var enabled = providers.FirstOrDefault(provider => _chatSelection.allowedProviders?.Contains(provider.id) == true);
            var model = _chatSelection?.model;
            if (!ready) ShowRow(_modelRow, "–", "Neutral", _modelStatus.Text);
            else if (providers.Length == 0) ShowRow(_modelRow, "Kein Anbieter verbunden", "Warning", _modelStatus.Text, "Verbinden", () => RunAction(OpenProviderSettingsAsync));
            else if (enabled == null) ShowRow(_modelRow, "Anbieter nicht freigegeben", "Warning", _modelStatus.Text);
            else ShowRow(_modelRow, enabled.name + (string.IsNullOrEmpty(model) ? " · Modell im Chat wählen" : " · " + model.Substring(model.IndexOf('/') + 1)), "Success", _modelStatus.Text);
            // Project tools (engineering MCP): saved project files only.
            var tools = McpState("engineering");
            if (!ready) ShowRow(_toolsRow, "–", "Neutral", _mcpStatus.Text);
            else if (tools != null) { McpLabel(tools, out value, out tone); ShowRow(_toolsRow, value, tone, _mcpStatus.Text); }
            else if (_chatProject == null || _chatBinding?.IsGeneral == true) ShowRow(_toolsRow, "Nur im Projektchat", "Neutral", "Projektwerkzeuge lesen gespeicherte Dateien des aktiven PLC-Projekts. Im allgemeinen Chat sind sie nicht verbunden.");
            else if (!_mcpEnabled) ShowRow(_toolsRow, "Nicht eingerichtet", "Neutral", _mcpStatus.Text);
            else ShowRow(_toolsRow, "Verbindet …", "Neutral", _mcpStatus.Text);
            // Knowledge search: the live MCP connection, not only the last reload.
            var knowledge = McpState("knowledge");
            Action reconnectKnowledge = () => { _knowledgeConnectionKey = null; BeginKnowledgeReconnect(); };
            if (_knowledgeBusy) ShowRow(_knowledgeRow, "Wird vorbereitet …", "Neutral", _knowledgeSummary.Text, "Fortschritt", ShowKnowledgeSetup);
            else if (_knowledgeState?.ready != true) ShowRow(_knowledgeRow, "Nicht eingerichtet", "Warning", _knowledgeSummary.Text, "Einrichten", ShowKnowledgeSetup);
            else if (!ready) ShowRow(_knowledgeRow, "–", "Neutral", _knowledgeSummary.Text);
            else if (knowledge == "failed" && KnowledgeRetrying()) ShowRow(_knowledgeRow, "Verbindet …", "Neutral", "Der Index wird geladen. Die Wissenssuche verbindet sich automatisch neu.");
            else if (McpFailed(knowledge)) { McpLabel(knowledge, out value, out tone); ShowRow(_knowledgeRow, value, tone, "OpenChamber meldet die Knowledge-Verbindung als " + knowledge + ".", "Neu verbinden", reconnectKnowledge); }
            else if (knowledge == "connected" && KnowledgeConnected()) ShowRow(_knowledgeRow, "Bereit", "Success", _knowledgeSummary.Text);
            else if (knowledge == null && !_mcpEnabled) ShowRow(_knowledgeRow, "Nicht konfiguriert", "Warning", _mcpStatus.Text);
            else ShowRow(_knowledgeRow, "Verbindet …", "Neutral", _knowledgeSummary.Text);
            // Installed Beckhoff help behind the search index.
            var state = _knowledgeState;
            var language = state?.language == "de" ? "Deutsch" : "Englisch";
            if (state?.ready == true) ShowRow(_helpRow, language + " · " + state.topics.ToString("N0") + " Artikel" + (state.phase == "update" ? " · Update verfügbar" : ""),
                state.phase == "update" ? "Warning" : "Success", _knowledgeSummary.Text, state.phase == "update" ? "Aktualisieren" : null, state.phase == "update" ? (Action)ShowKnowledgeSetup : null);
            else if (state?.phase == "missing") ShowRow(_helpRow, "Keine installierte Hilfe gefunden", "Warning", _knowledgeSummary.Text);
            else if (state?.phase == "external") ShowRow(_helpRow, "Eigene Dokumentationsquelle", "Neutral", _knowledgeSummary.Text);
            else ShowRow(_helpRow, state == null ? "Wird geprüft …" : "Nicht eingerichtet", "Neutral", _knowledgeSummary.Text);
        }

        private void CopyDiagnosticReport()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RenderDiagnostics();
            var report = new StringBuilder("TwinCAT Assistant – Diagnose " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")).AppendLine();
            foreach (var row in new[] { _chatRow, _modelRow, _toolsRow, _knowledgeRow, _helpRow })
                report.AppendLine(row.Label + ": " + row.Value.Text + (row.Detail == null ? "" : " — " + row.Detail));
            report.AppendLine("Verbindung: " + _chatConnection.Phase + " · Port " + _port.Text);
            report.AppendLine("Inventar: " + _inventoryStatus.Text);
            if (!string.IsNullOrWhiteSpace(_diagnosticStatus.Text)) report.AppendLine("Hinweis: " + _diagnosticStatus.Text);
            try { Clipboard.SetText(report.ToString()); Notify("Diagnosebericht kopiert. Er enthält keinen Quelltext und keine Zugangsdaten."); }
            catch { Notify("Die Zwischenablage ist gerade nicht verfügbar.", "warning"); }
        }
    }
}
