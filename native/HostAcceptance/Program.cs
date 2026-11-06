using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Web.Script.Serialization;
using EnvDTE;
using TCatSysManagerLib;
using TwinCatAssistant.Native;
using Process = System.Diagnostics.Process;
using Thread = System.Threading.Thread;

// Deliberately does not use RunningIde.Enumerate: even reading DTE.MainWindow
// on an unrelated operator instance is outside this acceptance runner's remit.
internal static class Program
{
    [ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMessageFilter
    {
        [PreserveSig] int HandleInComingCall(int type, IntPtr caller, int ticks, IntPtr info);
        [PreserveSig] int RetryRejectedCall(IntPtr callee, int ticks, int rejectType);
        [PreserveSig] int MessagePending(IntPtr callee, int ticks, int pendingType);
    }
    sealed class MessageFilter : IMessageFilter
    {
        public int HandleInComingCall(int type, IntPtr caller, int ticks, IntPtr info) => 0;
        public int RetryRejectedCall(IntPtr callee, int ticks, int rejectType) => ticks < 15000 && (rejectType == 1 || rejectType == 2) ? 100 : -1;
        public int MessagePending(IntPtr callee, int ticks, int pendingType) => 2;
    }
    [DllImport("ole32.dll")] static extern int CoRegisterMessageFilter(IMessageFilter filter, out IMessageFilter previous);
    [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
    [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved, out IBindCtx context);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static int failures;
    static void Record(string gate, string expected, object observed) => Console.WriteLine(Json.Serialize(new { gate, expected, observed, utc = DateTime.UtcNow }));
    static void Case(string gate, Action action)
    {
        Record("case-start", "begin bounded-scope direct-host observation", new { gate });
        try { action(); }
        catch (Exception error) { failures++; Record(gate, "successful direct-host check", new { passed = false, type = error.GetType().Name, hresult = error.HResult, message = error.Message }); }
    }
    static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Acceptance expectation mismatch"); }
    static DTE GetOwned(int pid)
    {
        GetRunningObjectTable(0, out var table); CreateBindCtx(0, out var context);
        try {
            table.EnumRunning(out var iterator);
            try {
                var monikers = new IMoniker[1];
                while (iterator.Next(1, monikers, IntPtr.Zero) == 0) {
                    try {
                        monikers[0].GetDisplayName(context, null, out var name);
                        if (!name.StartsWith("!TcXaeShell.DTE.", StringComparison.OrdinalIgnoreCase)
                            || !name.EndsWith(":" + pid, StringComparison.Ordinal)) continue;
                        table.GetObject(monikers[0], out var value);
                        var dte = (DTE)value;
                        GetWindowThreadProcessId(new IntPtr(dte.MainWindow.HWnd), out var actual);
                        if (actual != pid) throw new InvalidOperationException("Owned PID mismatch");
                        return dte;
                    } finally { Marshal.ReleaseComObject(monikers[0]); }
                }
            } finally { Marshal.ReleaseComObject(iterator); }
        } finally { Marshal.ReleaseComObject(context); Marshal.ReleaseComObject(table); }
        return null;
    }

    [STAThread]
    static int Main(string[] args)
    {
        var filter = new MessageFilter();
        Marshal.ThrowExceptionForHR(CoRegisterMessageFilter(filter, out var previous));
        try { return Run(args); }
        catch (Exception error) { Record("runner-failed", "valid owned-run inputs and successful execution", new { type = error.GetType().Name, hresult = error.HResult }); return 2; }
        finally { CoRegisterMessageFilter(previous, out _); GC.KeepAlive(filter); }
    }

    static int Run(string[] args)
    {
        if (args.Length == 2 && args[0] == "--close-owned") return CloseRecorded(args[1]);
        // Accept no existing PID or solution. Each run creates its own host and data.
        if ((args.Length != 2 && !(args.Length == 3 && args[2] == "--include-member-editors")) || !File.Exists(args[0]) || Directory.Exists(args[1])) return 2;
        var root = Path.GetFullPath(args[1]);
        if (!root.StartsWith(Path.Combine(Environment.CurrentDirectory, "local-data") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 2;
        Directory.CreateDirectory(root);
        var suffix = "AssistantAcceptance" + Guid.NewGuid().ToString("N");
        Process process = null; DTE dte = null;
        var result = 1; var stage = "launch";
        try {
            process = Process.Start(new ProcessStartInfo(args[0], "/RootSuffix " + suffix + " /Log \"" + Path.Combine(root, "ActivityLog.xml") + "\"") {
                UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
            });
            Record("launch", "new process, private registry suffix; no operator DTE access", new { pid = process.Id, suffix });
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 60 && !process.HasExited && dte == null) {
                try { dte = GetOwned(process.Id); } catch (COMException) { }
                if (dte == null) Thread.Sleep(250);
            }
            if (dte == null) {
                Record("isolation", "owned ROT registration within 60 seconds", new { passed = false, exited = process.HasExited, elapsedMs = timer.ElapsedMilliseconds });
                return 3;
            }
            var registry = dte.RegistryRoot;
            Record("isolation", "DTE.RegistryRoot includes exact unique suffix before any solution operation", new { registry, passed = registry.IndexOf(suffix, StringComparison.Ordinal) >= 0 });
            if (registry.IndexOf(suffix, StringComparison.Ordinal) < 0) return 3;
            if (!string.IsNullOrEmpty(dte.Solution.FullName) || dte.Documents.Count != 0) throw new InvalidOperationException("Private host restored unexpected documents");
            dte.SuppressUI = true;
            stage = "create-solution";
            var solutionPath = Path.Combine(root, "ContextChecks.sln");
            dte.Solution.Create(root, "ContextChecks");
            stage = "add-system-project";
            var project = dte.Solution.AddFromTemplate(@"C:\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj", Path.Combine(root, "TwinCAT"), "ContextChecks", false);
            var manager = (ITcSysManager)project.Object;
            stage = "create-plc-a";
            manager.LookupTreeItem("TIPC").CreateChild("PLC_A", 0, "", "Standard PLC Template");
            stage = "create-plc-b";
            manager.LookupTreeItem("TIPC").CreateChild("PLC_B", 0, "", "Standard PLC Template");
            foreach (var plc in new[] { "PLC_A", "PLC_B" }) {
                stage = "create-pou-" + plc;
                var plcRoot = manager.LookupTreeItem("TIPC^" + plc);
                var nested = ((ITcProjectRoot)plcRoot).NestedProject;
                Record("synthetic-project", "resolve nested project without localized name assumption", new { plc, path = nested.PathName });
                var item = nested.LookupChild("POUs").CreateChild("FB_Check", 604, "", 1);
                ((ITcPlcDeclaration)item).DeclarationText = "FUNCTION_BLOCK FB_Check\nVAR\n bRun : BOOL;\nEND_VAR";
                ((ITcPlcImplementation)item).ImplementationText = "bRun := " + (plc == "PLC_A" ? "FALSE" : "TRUE") + ";";
            }
            // Saving is confined to the freshly generated solution in this owned host.
            stage = "save-synthetic";
            dte.ExecuteCommand("File.SaveAll", ""); dte.Solution.SaveAs(solutionPath);
            var reader = new NativeActiveEditorReader(dte, process.Id, null);
            ActiveEditorSnapshot first = null;
            foreach (var plc in new[] { "PLC_A", "PLC_B" }) {
                stage = "open-read-" + plc;
                var node = ((ITcProjectRoot)manager.LookupTreeItem("TIPC^" + plc)).NestedProject.LookupChild("POUs^FB_Check");
                var file = ((ProjectItem)((ITcSmTreeItem8)node).VSProjectItem).FileNames[1];
                dte.ExecuteCommand("File.OpenFile", "\"" + file + "\"");
                var snapshot = reader.Read(reader.Capture());
                var correct = snapshot.Implementation.Contains(plc == "PLC_A" ? "FALSE" : "TRUE");
                Record("active-pou-direct-reader", plc + " exact body and distinct identity", new { passed = correct && (first == null || !first.Identity.Equals(snapshot.Identity)), snapshot.ObjectName, snapshot.Dirty.State, snapshot.SourceKind, snapshot.ReadMilliseconds });
                if (!correct || (first != null && first.Identity.Equals(snapshot.Identity))) throw new InvalidOperationException("Wrong project source");
                first = snapshot;
            }
            var activeNode = ((ITcProjectRoot)manager.LookupTreeItem("TIPC^PLC_B")).NestedProject.LookupChild("POUs^FB_Check");
            Case("dirty-saved-snapshot", () => {
                var document = dte.ActiveDocument;
                ((ITcPlcImplementation)activeNode).ImplementationText = "bRun := NOT bRun; // synthetic AI edit";
                var dirty = reader.Read(reader.Capture());
                Require(dirty.Implementation.Contains("NOT bRun") && dirty.Dirty.State != "known-clean" && dirty.SourceNotice.Contains("not verified"));
                Record("dirty-saved-snapshot", "AI edit changes snapshot without claiming keystroke buffer equivalence", new { passed = true, document.Saved, dirty.Dirty.State, dirty.SourceKind, dirty.SourceNotice });
                document.Save();
                var saved = reader.Read(reader.Capture());
                Require(document.Saved && saved.Implementation == dirty.Implementation);
                Record("saved-snapshot", "explicit synthetic document save preserves snapshot content", new { passed = true, document.Saved, saved.Dirty.State });
            });
            Case("large-pou-saved-reference", () => {
                var sentinel = "SYNTHETIC_LARGE_BODY_ONLY";
                ((ITcPlcImplementation)activeNode).ImplementationText = "(*" + sentinel + new string('x', 20000) + "*)\nbRun := TRUE;";
                dte.ActiveDocument.Save();
                var large = reader.Read(reader.Capture()); var attachment = ChatContextPolicy.CreateAttachment(large);
                Require(attachment.IsSavedReference && !attachment.Text.Contains(sentinel) && attachment.Text.Contains(large.Identity.TreePath));
                Record("large-pou-saved-reference", "real host snapshot yields exact saved-object reference without source body", new { passed = true, characters = large.Implementation.Length, attachment.IsSavedReference, large.ReadMilliseconds, scope = "direct policy on real reader output; no installed composer or MCP call" });
            });
            // A subdocument File.OpenFile call blocked in the 2026-11-05 host.
            // Opt in only with a working operator/UI route to dismiss its dialog.
            if (args.Length == 3) foreach (var member in new[] { new { Name = "Reset", Type = 608, Kind = "Action" }, new { Name = "Evaluate", Type = 609, Kind = "Method" }, new { Name = "Speed", Type = 611, Kind = "Property" } }) {
                Case("member-" + member.Kind, () => {
                    var node = activeNode.CreateChild(member.Name, member.Type, "", 1);
                    if (member.Kind == "Method") ((ITcPlcDeclaration)node).DeclarationText = "METHOD Evaluate : BOOL";
                    if (member.Kind == "Property") ((ITcPlcDeclaration)node).DeclarationText = "PROPERTY Speed : INT";
                    if (node is ITcPlcImplementation body) body.ImplementationText = member.Kind == "Method" ? "Evaluate := TRUE;" : "bRun := FALSE;";
                    dte.ExecuteCommand("File.SaveAll", "");
                    var moniker = ((ProjectItem)((ITcSmTreeItem8)node).VSProjectItem).FileNames[1];
                    dte.ExecuteCommand("File.OpenFile", "\"" + moniker + "\"");
                    var snapshot = reader.Read(reader.Capture());
                    Require(snapshot.ObjectKind == member.Kind && snapshot.MemberPath == member.Name && snapshot.Identity.TreePath == node.PathName);
                    Record("member-" + member.Kind, "exact active member identity without parent substitution", new { passed = true, snapshot.ObjectKind, snapshot.MemberPath, snapshot.SourceKind });
                });
            }
            Case("non-st-editor", () => {
                var note = Path.Combine(root, "unsupported.txt"); File.WriteAllText(note, "Owned synthetic non-ST document");
                dte.ExecuteCommand("File.OpenFile", "\"" + note + "\"");
                try { reader.Capture(); } catch (InvalidOperationException) { Record("non-st-editor", "unbound", new { passed = true }); return; }
                throw new InvalidOperationException("Stale ST context");
            });
            dte.Documents.CloseAll(vsSaveChanges.vsSaveChangesNo);
            try { reader.Capture(); throw new Exception("Stale active context"); }
            catch (InvalidOperationException) { Record("no-editor-direct-reader", "unbound", new { passed = true }); }
            Case("solution-reopen", () => {
                dte.Solution.Close(false);
                try { reader.Capture(); throw new Exception("Stale solution context"); }
                catch (InvalidOperationException) { }
                dte.Solution.Open(solutionPath);
                manager = (ITcSysManager)dte.Solution.Projects.Item(1).Object;
                var node = ((ITcProjectRoot)manager.LookupTreeItem("TIPC^PLC_A")).NestedProject.LookupChild("POUs^FB_Check");
                var file = ((ProjectItem)((ITcSmTreeItem8)node).VSProjectItem).FileNames[1];
                dte.ExecuteCommand("File.OpenFile", "\"" + file + "\"");
                var snapshot = reader.Read(reader.Capture());
                Require(snapshot.Implementation.Contains("FALSE") && snapshot.Identity.IdeProcessId == process.Id);
                Record("solution-reopen", "fresh same-path snapshot after solution close and reopen; warm only", new { passed = true, snapshot.SourceKind });
            });
            Case("multi-ide-direct-reader", () => {
                var before = reader.Read(reader.Capture());
                var otherSuffix = "AssistantAcceptance" + Guid.NewGuid().ToString("N");
                var secondary = Process.Start(new ProcessStartInfo(args[0], "/RootSuffix " + otherSuffix) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
                DTE other = null;
                var receipt = Json.Serialize(new { gate = "launch", expected = "owned secondary", observed = new { pid = secondary.Id, suffix = otherSuffix }, utc = DateTime.UtcNow });
                File.WriteAllText(Path.Combine(root, "secondary-launch.jsonl"), receipt + Environment.NewLine);
                Console.WriteLine(receipt);
                try {
                    var wait = Stopwatch.StartNew();
                    while (other == null && !secondary.HasExited && wait.Elapsed.TotalSeconds < 60) {
                        try { other = GetOwned(secondary.Id); } catch (COMException) { }
                        if (other == null) Thread.Sleep(250);
                    }
                    Require(other != null && other.RegistryRoot.Contains(otherSuffix));
                    Require(string.IsNullOrEmpty(other.Solution.FullName) && other.Documents.Count == 0);
                    other.SuppressUI = true; other.Solution.Open(solutionPath);
                    var otherManager = (ITcSysManager)other.Solution.Projects.Item(1).Object;
                    var node = ((ITcProjectRoot)otherManager.LookupTreeItem("TIPC^PLC_B")).NestedProject.LookupChild("POUs^FB_Check");
                    var file = ((ProjectItem)((ITcSmTreeItem8)node).VSProjectItem).FileNames[1];
                    other.ExecuteCommand("File.OpenFile", "\"" + file + "\"");
                    var otherReader = new NativeActiveEditorReader(other, secondary.Id, null);
                    var snapshot = otherReader.Read(otherReader.Capture());
                    var after = reader.Read(reader.Capture());
                    Require(!snapshot.Identity.Equals(before.Identity) && snapshot.Identity.IdeProcessId == secondary.Id
                        && before.Identity.Equals(after.Identity) && before.Implementation == after.Implementation);
                    Record("multi-ide-direct-reader", "distinct owned PIDs; second PLC_B does not replace first PLC_A snapshot", new { passed = true, firstPid = process.Id, secondPid = secondary.Id, scope = "direct reader, not installed pane/chat isolation" });
                } finally {
                    if (other != null) { try { other.Documents.CloseAll(vsSaveChanges.vsSaveChangesNo); other.Solution.Close(false); other.Quit(); } catch (COMException) { secondary.CloseMainWindow(); } }
                    else if (!secondary.HasExited) secondary.CloseMainWindow();
                    var exited = secondary.WaitForExit(15000);
                    Record("owned-secondary-shutdown", "normal close of owned secondary shell", new { pid = secondary.Id, passed = exited });
                    if (!exited) failures++;
                }
            });
            result = failures == 0 ? 0 : 1;
        } catch (Exception error) {
            // Avoid arbitrary COM error text that might contain user paths or data.
            Record("runner-error", "no unhandled host errors", new { stage, type = error.GetType().Name, hresult = error.HResult, message = error.Message });
        } finally {
            if (dte != null) {
                try { dte.Documents.CloseAll(vsSaveChanges.vsSaveChangesNo); dte.Solution.Close(false); dte.Quit(); }
                catch (COMException) { process.CloseMainWindow(); }
            } else if (process != null && !process.HasExited) {
                // Only the Process returned by our own launch. Never kill a hung host.
                process.CloseMainWindow();
            }
            if (process != null) {
                var exited = process.WaitForExit(10000);
                Record("owned-host-shutdown", "normal exit of owned shell", new { pid = process.Id, passed = exited, exitCode = exited ? (int?)process.ExitCode : null });
                if (!exited) result = 3;
            }
        }
        return result;
    }

    static int CloseRecorded(string evidence)
    {
        // Recovery accepts only our recorded launch, never an arbitrary PID.
        var full = Path.GetFullPath(evidence);
        if (!full.StartsWith(Path.Combine(Environment.CurrentDirectory, "local-data") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 2;
        string line;
        using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream)) line = reader.ReadLine();
        var launch = Json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(line);
        if ((string)launch["gate"] != "launch") return 2;
        var observed = (System.Collections.Generic.Dictionary<string, object>)launch["observed"];
        var pid = (int)observed["pid"]; var suffix = (string)observed["suffix"];
        var launched = (DateTime)launch["utc"];
        Process process;
        try { process = Process.GetProcessById(pid); } catch (ArgumentException) { return 0; }
        if (process.ProcessName != "TcXaeShell" || Math.Abs((process.StartTime.ToUniversalTime() - launched.ToUniversalTime()).TotalSeconds) > 3) {
            Record("recovery-refused", "same recorded process and start time", new { pid, launched, actualStart = process.StartTime.ToUniversalTime() }); return 2;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(suffix, "^AssistantAcceptance[0-9a-f]{32}$")) return 2;
        // A pending editor COM call may block further COM forever. Use only
        // normal WM_CLOSE to this PID/start-time verified process, never kill.
        var signaled = process.CloseMainWindow();
        var closed = process.WaitForExit(15000);
        Record("owned-host-recovery-close", "normal WM_CLOSE to recorded PID/start time", new { pid, signaled, passed = closed });
        return closed ? 0 : 3;
    }
}
