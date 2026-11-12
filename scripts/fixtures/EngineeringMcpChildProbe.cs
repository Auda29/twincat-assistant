using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using TwinCatAssistant.Native;
namespace TwinCatAssistant.Native
{
    internal sealed class EngineeringBinding
    {
        internal Guid SessionId = Guid.NewGuid(); internal int IdePid = 123; internal DateTime IdeStartedUtc = DateTime.UtcNow;
        internal string SolutionFile = @"C:\synthetic\solution.sln", ProjectFile = @"C:\synthetic\project.plcproj", ContextEpoch = "synthetic";
        internal bool Same(EngineeringBinding b) { return Object.ReferenceEquals(this, b); }
    }
    internal sealed class EngineeringProviderSession
    {
        internal EngineeringBinding Binding = new EngineeringBinding();
        internal string LastDescribeOperation;
        internal int Pings, Describes, Dispatches;
        internal Task<string> DispatchAsync(string value, CancellationToken token)
        {
            Interlocked.Increment(ref Dispatches);
            token.ThrowIfCancellationRequested();
            var json = new JavaScriptSerializer();
            var request = json.DeserializeObject(value) as Dictionary<string, object>;
            if (request == null || request.Count != 6 || !Equals(request["protocolVersion"], 1)
                || !Equals(request["sessionId"], Binding.SessionId.ToString("D"))) throw new InvalidOperationException("Invalid request envelope.");
            Guid operationId;
            var id = request["operationId"] as string;
            if (!Guid.TryParseExact(id, "D", out operationId) || operationId == Guid.Empty || id != operationId.ToString("D")) throw new InvalidOperationException("Invalid operation identity.");
            var expected = request["expected"] as Dictionary<string, object>;
            var arguments = request["arguments"] as Dictionary<string, object>;
            DateTime started;
            if (expected == null || expected.Count != 5 || arguments == null || arguments.Count != 0
                || !Equals(expected["idePid"], Binding.IdePid)
                || !DateTime.TryParse((string)expected["ideStartedUtc"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out started)
                || started != Binding.IdeStartedUtc || !Equals(expected["solutionFile"], Binding.SolutionFile)
                || !Equals(expected["projectFile"], Binding.ProjectFile) || !Equals(expected["contextEpoch"], Binding.ContextEpoch))
                throw new InvalidOperationException("Invalid context binding.");
            var operation = request["operation"] as string;
            if (operation == "session.ping") Interlocked.Increment(ref Pings);
            else if (operation == "session.describe") { LastDescribeOperation = id; Interlocked.Increment(ref Describes); }
            else throw new InvalidOperationException("Unexpected operation.");
            object result = operation == "session.ping"
                ? (object)new { contextValidated = true, capabilities = new { build = "unsupported" } }
                : new { contextValidated = true, capabilities = new { build = "unsupported" }, targetProjectFile = Binding.ProjectFile };
            return Task.FromResult(json.Serialize(new { protocolVersion = 1, sessionId = Binding.SessionId.ToString("D"), operationId = id,
                status = "succeeded", provenance = new { source = "synthetic" }, result = result }));
        }
    }
    internal static class NativeInstallationSelection
    {
        internal static string Root;
        internal static string SelectRoot(string path) { return Root; }
    }
}
internal static class EngineeringMcpChildProbe
{
    private static int Main(string[] args)
    {
        try { if (args.Length > 1 && args[1] == "real") RunReal(args[0]).GetAwaiter().GetResult(); else Run(args[0]).GetAwaiter().GetResult(); Console.WriteLine("Engineering child probe passed."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
    private static async Task RunReal(string root)
    {
        NativeInstallationSelection.Root = root;
        var session = new EngineeringProviderSession();
        EngineeringMcpChild child;
        using (var deadline = new CancellationTokenSource(15000))
        {
            try { child = await EngineeringMcpChild.StartAsync(root, session, deadline.Token); }
            catch { Console.Error.WriteLine("Synthetic dispatches observed: " + session.Dispatches + "; validated pings: " + session.Pings); throw; }
        }
        if (session.Pings != 1) throw new Exception("Authenticated bootstrap ping was not dispatched exactly once.");
        int pid = FindOwnedNode();
        if (pid <= 0) throw new Exception("Dedicated Node child not found.");
        string text = await child.CallToolAsync("tc_session_describe", "{}", CancellationToken.None);
        var response = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
        var structured = response != null && response.ContainsKey("structuredContent") ? response["structuredContent"] as Dictionary<string, object> : null;
        var result = structured != null && structured.ContainsKey("result") ? structured["result"] as Dictionary<string, object> : null;
        if (structured == null || result == null || !Equals(structured["status"], "succeeded")
            || !Equals(structured["sessionId"], session.Binding.SessionId.ToString("D"))
            || !Equals(structured["operationId"], session.LastDescribeOperation) || session.Describes != 1
            || !Equals(result["contextValidated"], true) || !Equals(result["targetProjectFile"], session.Binding.ProjectFile))
            throw new Exception("Live MCP result did not preserve session, operation, and context correlation.");
        child.Dispose();
        try { using (var process = Process.GetProcessById(pid)) if (!process.WaitForExit(5000)) throw new Exception("Packaged Node child survived disposal."); }
        catch (ArgumentException) { }
        try { await child.CallToolAsync("tc_session_describe", "{}", CancellationToken.None); throw new Exception("Disposed child accepted a call."); }
        catch (ObjectDisposedException) { }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        internal uint Size, Usage, ProcessId; internal UIntPtr DefaultHeapId; internal uint ModuleId, Threads, ParentProcessId;
        internal int BasePriority; internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Executable;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private static int FindOwnedNode()
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) throw new Exception("Cannot inspect synthetic child process.");
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
            if (Process32First(snapshot, ref entry)) do
            {
                if (entry.ParentProcessId == (uint)Process.GetCurrentProcess().Id && string.Equals(entry.Executable, "node.exe", StringComparison.OrdinalIgnoreCase)) return (int)entry.ProcessId;
            } while (Process32Next(snapshot, ref entry));
            return 0;
        }
        finally { CloseHandle(snapshot); }
    }
    private static async Task Run(string root)
    {
        NativeInstallationSelection.Root = root;
        var session = new EngineeringProviderSession();
        var child = await EngineeringMcpChild.StartAsync(root, session, CancellationToken.None);
        var response = new JavaScriptSerializer().Deserialize<dynamic>(await child.CallToolAsync("probe", "{\"number\":7}", CancellationToken.None));
        if ((string)response["content"][0]["text"] != "7") throw new Exception("Wrong tool response.");
        int pid = (int)response["pid"];
        child.Dispose();
        await Task.Delay(500);
        try { using (var process = System.Diagnostics.Process.GetProcessById(pid)) if (!process.HasExited) throw new Exception("Child was not reaped."); }
        catch (ArgumentException) { }
        child = await EngineeringMcpChild.StartAsync(root, session, CancellationToken.None);
        using (var stop = new CancellationTokenSource(500))
        {
            try { await child.CallToolAsync("hang", "{}", stop.Token); throw new Exception("Call did not cancel."); }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
        child.Dispose();
        string script = Path.Combine(root, "runtime", "mcp", "engineering", "packages", "mcp", "dist", "index.js");
        File.AppendAllText(script, "\n// synthetic tamper");
        try { await EngineeringMcpChild.StartAsync(root, session, CancellationToken.None); throw new Exception("Tampered package accepted."); }
        catch (IOException) { }
    }
}
