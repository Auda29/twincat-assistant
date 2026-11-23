using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private sealed class DebugLogin
        {
            internal string AmsNetId, MappingDigest, ProjectFile;
            internal int? AdsPort;
        }

        private sealed class DebugGrant
        {
            internal readonly Guid Id = Guid.NewGuid();
            internal DebugLogin Login;
            internal int Port;
            internal bool AllowAllSymbols;
            internal string[] Symbols;
            internal bool Matches(DebugLogin value) => value != null && value.AmsNetId == Login.AmsNetId
                && value.MappingDigest == Login.MappingDigest && value.ProjectFile == Login.ProjectFile
                && value.AdsPort == Login.AdsPort;
        }

        private async System.Threading.Tasks.Task<(DebugLogin Login, string Notice)> ObserveDebugLoginAsync(EngineeringProviderSession session, CancellationToken cancellation)
        {
            try
            {
                var binding = session.Binding;
                if (binding == null) return (null, "EngineeringDebugVerificationUnavailable");
                var id = Guid.NewGuid();
                var raw = await EngineeringDispatchBoundedAsync(session, session.CreateRequest("project.online", id, new { }), cancellation);
                return ParseDebugLogin(EditorParseResponse(raw, binding, id), binding.ProjectFile);
            }
            catch { return (null, "EngineeringDebugVerificationUnavailable"); }
        }

        private static (DebugLogin Login, string Notice) ParseDebugLogin(Dictionary<string, object> envelope, string projectFile)
        {
            if (EditorText(envelope, "status", 32) != "succeeded")
            {
                var error = EditorText(envelope, "error", 128);
                return (null, error?.StartsWith("association_", StringComparison.Ordinal) == true
                    ? "EngineeringDebugAssociationUnavailable" : "EngineeringDebugVerificationUnavailable");
            }

            var result = EditorObject(envelope, "result");
            if (result == null || !result.TryGetValue("loggedIn", out var state) || !(state is bool loggedIn))
                return (null, "EngineeringDebugVerificationUnavailable");
            if (!loggedIn) return (null, "EngineeringDebugLoginRequired");
            var netId = EditorText(result, "configuredAmsNetId", 23);
            var digest = EditorText(result, "mappingDigest", 64);
            if (!SourceDigest(digest)) return (null, "EngineeringDebugVerificationUnavailable");
            if (!ValidDebugNetId(netId)) return (null, "EngineeringDebugTargetUnavailable");
            int? adsPort = null;
            if (result.TryGetValue("configuredAdsPort", out var portValue) && portValue != null)
            {
                if (!(portValue is int port) || port < 851 || port > 899)
                    return (null, "EngineeringDebugVerificationUnavailable");
                adsPort = port;
            }
            return (new DebugLogin { AmsNetId = netId, MappingDigest = digest, ProjectFile = projectFile, AdsPort = adsPort }, null);
        }

        private static DebugGrant CreateAutomaticDebugGrant(DebugLogin login)
        {
            if (login == null || !ValidDebugNetId(login.AmsNetId) || !login.AdsPort.HasValue
                || login.AdsPort.Value < 851 || login.AdsPort.Value > 899) return null;
            return new DebugGrant { Login = login, Port = login.AdsPort.Value, AllowAllSymbols = true };
        }

        private async System.Threading.Tasks.Task<string> DescribeDebugGrantAsync(EngineeringProviderSession session, EngineeringBinding binding,
            EditorRequest request, long generation, CancellationToken cancellation)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellation);
            var grant = _debugGrant;
            if (_activeEngineeringProfile != "debug" || grant == null || request.Arguments.Count != 0)
                throw new InvalidOperationException("Debug grant is not active.");
            var current = await ObserveDebugLoginAsync(session, cancellation);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellation);
            if (!await VerifyEngineeringEditorViewAsync(generation, cancellation) || !ReferenceEquals(grant, _debugGrant)
                || !EditorProfileIsCurrent() || !binding.Same(session.Binding))
                throw new InvalidOperationException("Debug grant changed.");
            if (!grant.Matches(current.Login))
            {
                RevokeEngineeringEditor();
                Notify(HostText.Get(current.Notice ?? "EngineeringDebugTargetChanged"), "warning");
                throw new InvalidOperationException("Debug login changed.");
            }
            object payload = grant.AllowAllSymbols
                ? (object)new { loggedIn = true, grantId = grant.Id.ToString("D"), amsNetId = grant.Login.AmsNetId,
                    adsPort = grant.Port, symbolScope = "all" }
                : new { loggedIn = true, grantId = grant.Id.ToString("D"), amsNetId = grant.Login.AmsNetId,
                    adsPort = grant.Port, symbols = grant.Symbols };
            return EngineeringProviderSession.Json().Serialize(new { protocolVersion = 1, sessionId = binding.SessionId.ToString("D"),
                operationId = request.Id.ToString("D"), status = "succeeded", provenance = new { }, result = payload });
        }

        private static bool ValidDebugNetId(string value)
        {
            if (value == null || value == "0.0.0.0.0.0") return false;
            var parts = value.Split('.');
            return parts.Length == 6 && parts.All(part => byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var octet)
                && part == octet.ToString(CultureInfo.InvariantCulture));
        }

        private static DebugGrant ConfirmDebugGrant(DebugLogin login)
        {
            var dialog = new Window { Title = HostText.Get("EngineeringDebugEnable"), Width = 480,
                SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize };
            var body = new StackPanel { Margin = new Thickness(16) };
            body.Children.Add(new TextBlock { Text = HostText.Format("EngineeringDebugConsent", Path.GetFileName(login.ProjectFile), login.AmsNetId),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            var port = new TextBox { MaxLength = 3, Width = 70, HorizontalAlignment = HorizontalAlignment.Left,
                Text = login.AdsPort?.ToString(CultureInfo.InvariantCulture) ?? "" };
            body.Children.Add(new Label { Content = HostText.Get("EngineeringDebugPort"), Target = port });
            body.Children.Add(port);
            var names = new TextBox { AcceptsReturn = true, Height = 96, MaxLength = 5152, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            body.Children.Add(new Label { Content = HostText.Get("EngineeringDebugSymbols"), Target = names, Margin = new Thickness(0, 8, 0, 0) });
            body.Children.Add(names);
            var evidence = new CheckBox { Content = HostText.Get("EngineeringDebugVerify"), Margin = new Thickness(0, 12, 0, 12) };
            body.Children.Add(evidence);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = HostText.Get("EngineeringSourceCancel"), IsCancel = true, MinWidth = 96 };
            var allow = new Button { Content = HostText.Get("EngineeringDebugEnable"), IsDefault = true, IsEnabled = false, MinWidth = 96,
                Margin = new Thickness(6, 0, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(allow);
            body.Children.Add(buttons);
            dialog.Content = body;
            DebugGrant selected = null;
            Action update = () =>
            {
                selected = ParseDebugGrant(login, port.Text, names.Text, evidence.IsChecked == true);
                allow.IsEnabled = selected != null;
            };
            port.TextChanged += (sender, args) => update();
            names.TextChanged += (sender, args) => update();
            evidence.Checked += (sender, args) => update();
            evidence.Unchecked += (sender, args) => update();
            allow.Click += (sender, args) => { if (selected != null) dialog.DialogResult = true; };
            return dialog.ShowDialog() == true ? selected : null;
        }

        private static DebugGrant ParseDebugGrant(DebugLogin login, string portText, string symbolsText, bool confirmed)
        {
            if (!confirmed || !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                || port < 851 || port > 899 || symbolsText.Length > 5152) return null;
            var symbols = symbolsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()).Where(line => line.Length != 0).ToArray();
            if (symbols.Length < 1 || symbols.Length > 32 || symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Length
                || symbols.Any(name => name.Length > 160 || !Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*|\[[0-9]{1,6}\])*\z",
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))) return null;
            return new DebugGrant { Login = login, Port = port, Symbols = symbols };
        }
    }
}
