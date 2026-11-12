using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private Guid _engineeringNavigationRequest, _engineeringNavigationBuild;
        private string _engineeringNavigationDiagnostic;

        private bool ConsumeEngineeringNavigationClick(string request, string build, string diagnostic)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var accepted = !_disposed && !_engineeringProjectSuspended && _engineering != null
                && _engineeringNavigationRequest != Guid.Empty
                && request == _engineeringNavigationRequest.ToString("D")
                && build == _engineeringNavigationBuild.ToString("D")
                && diagnostic == _engineeringNavigationDiagnostic;
            ClearEngineeringNavigationClick();
            return accepted;
        }
        private void ClearEngineeringNavigationClick()
        { _engineeringNavigationRequest = _engineeringNavigationBuild = Guid.Empty; _engineeringNavigationDiagnostic = null; }

        private async Task NavigateEngineeringDiagnosticAsync(EngineeringProviderSession session, Guid build, string diagnostic)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || session == null || session != _engineering || session.Binding == null
                || _engineeringProjectSuspended || _engineeringBusy || EngineeringBuildGuard.HasUnresolved
                || EngineeringSourceGuard.HasUnresolved || _sourceBusy || !SourceDigest(diagnostic)
                || _engineeringNavigationRequest != Guid.Empty) return;
            var operation = Guid.NewGuid();
            _engineeringNavigationRequest = operation; _engineeringNavigationBuild = build;
            _engineeringNavigationDiagnostic = diagnostic;
            try
            {
                var response = await EngineeringDispatchBoundedAsync(session,
                    session.CreateRequest("diagnostics.navigate", operation, new { targetOperationId = build.ToString("D"), diagnosticId = diagnostic }),
                    _engineeringLifetime.Token);
                if (_disposed || session != _engineering) return;
                var envelope = EngineeringProviderSession.Json().DeserializeObject(response) as Dictionary<string, object>;
                object protocol;
                if (envelope == null || !envelope.TryGetValue("protocolVersion", out protocol) || !Equals(protocol, 1)
                    || EngineeringProviderSession.Text(envelope, "sessionId") != session.Binding?.SessionId.ToString("D")
                    || EngineeringProviderSession.Text(envelope, "operationId") != operation.ToString("D")) throw new InvalidOperationException();
                if (EngineeringProviderSession.Text(envelope, "status") != "succeeded") SetEngineeringStatus("EngineeringNavigationUnavailable");
            }
            catch { if (!_disposed && session == _engineering) SetEngineeringStatus("EngineeringNavigationUnavailable"); }
            finally { ClearEngineeringNavigationClick(); }
        }
    }
}
