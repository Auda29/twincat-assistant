using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace TwinCatAssistant.Native
{
    internal static class NativeModelConsent
    {
        internal static async Task ConfigureAsync(NativeChatConfiguration config, string provider, string model)
        {
            config.Validate();
            if (string.IsNullOrWhiteSpace(provider) || !System.Text.RegularExpressions.Regex.IsMatch(provider, @"\A[a-zA-Z0-9_-]{1,80}\z"))
                throw new InvalidOperationException("Zuerst einen angemeldeten Anbieter auswählen.");
            var script = Path.Combine(config.RepositoryRoot, "src", "native-chat-model.mjs");
            if (!File.Exists(script)) throw new IOException("Model setup entrypoint is missing.");
            var start = new ProcessStartInfo(config.NodePath, NativeChatSession.Quote(script)) {
                WorkingDirectory = config.RepositoryRoot, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.EnvironmentVariables.Clear();
            foreach (var key in new[] { "PATH", "Path", "SystemRoot", "WINDIR", "COMSPEC", "PATHEXT", "TEMP", "TMP", "LANG", "LC_ALL" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (value != null) start.EnvironmentVariables[key] = value;
            }
            start.EnvironmentVariables["DOTNET_ROOT"] = config.DotNetRoot;
            using (var process = Process.Start(start))
            {
                if (process == null) throw new IOException("Model setup did not start.");
                var drain = NativeChatSession.DrainAsync(process.StandardError);
                var consent = new Dictionary<string, object> {
                    { "state", config.StateRoot }, { "provider", provider }, { "acceptSourceDisclosure", true }
                };
                if (!string.IsNullOrEmpty(model)) consent.Add("model", model);
                var request = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(consent));
                await process.StandardInput.BaseStream.WriteAsync(request, 0, request.Length);
                process.StandardInput.Close();
                var response = NativeChatSession.ReadReadyAsync(process.StandardOutput);
                if (await Task.WhenAny(response, Task.Delay(15000)) != response)
                {
                    try { process.Kill(); } catch { }
                    throw new IOException("Model setup timed out. No automatic retry.");
                }
                if ((await response).Trim() != "{\"configured\":true}") throw new IOException("Model policy could not be verified.");
            }
        }
    }
}
