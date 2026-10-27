using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TwinCatAssistant.Native
{
    internal sealed class NativeMcpWorkspace
    {
        public bool configured { get; set; }
        public string workspace { get; set; }
        public string project { get; set; }
        public string solution { get; set; }
        public string source { get; set; }
        public string corpus { get; set; }
    }
    internal static class NativeMcpProject
    {
        internal static async Task<NativeMcpWorkspace> PrepareAsync(NativeChatConfiguration config, NativeProjectIdentity project)
        {
            config.Validate();
            if (project == null || string.IsNullOrEmpty(config.McpSettingsPath)) throw new InvalidOperationException("Native MCP setup is missing.");
            var start = new ProcessStartInfo(config.NodePath, NativeChatSession.Quote(Path.Combine(config.RepositoryRoot, "src", "native-mcp.mjs"))) {
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
                if (process == null) throw new IOException("MCP project setup did not start.");
                var drain = NativeChatSession.DrainAsync(process.StandardError);
                var input = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new {
                    state = config.StateRoot, mcpConfig = config.McpSettingsPath,
                    solution = project.SolutionFile, project = project.ProjectFile
                }));
                await process.StandardInput.BaseStream.WriteAsync(input, 0, input.Length);
                process.StandardInput.Close();
                var read = NativeChatSession.ReadReadyAsync(process.StandardOutput, 12000);
                if (await Task.WhenAny(read, Task.Delay(30000)) != read)
                { try { process.Kill(); } catch { } throw new IOException("MCP project setup timed out."); }
                var result = new JavaScriptSerializer().Deserialize<NativeMcpWorkspace>(await read);
                var parent = Path.GetFullPath(Path.Combine(config.StateRoot, "projects")) + Path.DirectorySeparatorChar;
                if (result?.configured != true || string.IsNullOrEmpty(result.workspace)
                    || !Path.GetFullPath(result.workspace).StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(result.project, project.ProjectFile, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(result.solution, project.SolutionFile, StringComparison.OrdinalIgnoreCase)
                    || result.source != "saved-project-files") throw new IOException("MCP project setup could not be verified.");
                return result;
            }
        }
    }
}
