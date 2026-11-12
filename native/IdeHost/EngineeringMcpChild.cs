using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace TwinCatAssistant.Native
{
    // Owns exactly one dedicated engineering child and the private host transport.
    internal sealed class EngineeringMcpChild : IDisposable
    {
        private const int BootstrapLimit = 4096, MessageLimit = 512 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly SemaphoreSlim calls = new SemaphoreSlim(1, 1);
        private readonly object sync = new object();
        private EngineeringHostTransport transport;
        private SafeFileHandle job;
        private FileStream input, output;
        private bool disposed;
        private int nextId;

        internal static async Task<EngineeringMcpChild> StartAsync(string root, EngineeringProviderSession session, CancellationToken cancellationToken)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var binding = session.Binding ?? throw new ObjectDisposedException(nameof(session));
            var child = new EngineeringMcpChild();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(60));
                using (deadline.Token.Register(child.Dispose))
                {
                    try
                    {
                        await Task.Run(() => VerifyPackage(root, deadline.Token), deadline.Token).ConfigureAwait(false);
                        deadline.Token.ThrowIfCancellationRequested();
                        if (!binding.Same(session.Binding)) throw new InvalidOperationException("Engineering context changed.");
                        var ownedTransport = new EngineeringHostTransport(binding.SessionId, session.DispatchAsync);
                        lock (child.sync)
                        {
                            if (child.disposed) { ownedTransport.Dispose(); throw new OperationCanceledException(deadline.Token); }
                            child.transport = ownedTransport;
                        }
                        child.Launch(root);
                        var bootstrap = Json().Serialize(new { kind = "twincat-assistant-engineering-bootstrap", protocolVersion = 1,
                            pipeName = @"\\.\pipe\" + ownedTransport.PipeName, sessionId = ownedTransport.SessionId,
                            capability = ownedTransport.Capability,
                            expected = new { idePid = binding.IdePid, ideStartedUtc = binding.IdeStartedUtc.ToString("o", CultureInfo.InvariantCulture),
                                solutionFile = binding.SolutionFile, projectFile = binding.ProjectFile, contextEpoch = binding.ContextEpoch } });
                        await child.WriteLineAsync(bootstrap, BootstrapLimit - 1, deadline.Token).ConfigureAwait(false);
                        string ready = await child.ReadLineAsync(BootstrapLimit, deadline.Token).ConfigureAwait(false);
                        string expected = "{\"kind\":\"twincat-assistant-engineering-ready\",\"protocolVersion\":1,\"sessionId\":\"" + binding.SessionId.ToString("D") + "\"}";
                        if (!string.Equals(ready, expected, StringComparison.Ordinal)) throw new IOException("Invalid engineering readiness response.");
                        var initialize = await child.RequestAsync("initialize", new { protocolVersion = "2024-11-05", capabilities = new { },
                            clientInfo = new { name = "twincat-assistant", version = "1.0.0" } }, deadline.Token).ConfigureAwait(false);
                        var info = initialize as Dictionary<string, object>;
                        object version, capabilities;
                        if (info == null || !info.TryGetValue("protocolVersion", out version) || !string.Equals(version as string, "2024-11-05", StringComparison.Ordinal)
                            || !info.TryGetValue("capabilities", out capabilities) || !(capabilities is Dictionary<string, object>))
                            throw new IOException("Invalid engineering MCP initialization.");
                        await child.SendAsync(new { jsonrpc = "2.0", method = "notifications/initialized" }, deadline.Token).ConfigureAwait(false);
                        if (!binding.Same(session.Binding)) throw new InvalidOperationException("Engineering context changed.");
                        deadline.Token.ThrowIfCancellationRequested();
                        return child;
                    }
                    catch { child.Dispose(); throw; }
                }
            }
        }

        internal async Task<string> CallToolAsync(string name, string argumentsJson, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 256 || argumentsJson == null || Utf8.GetByteCount(argumentsJson) > MessageLimit / 2)
                throw new ArgumentException("Invalid engineering tool call.");
            object arguments = Json().DeserializeObject(argumentsJson);
            if (!(arguments is Dictionary<string, object>)) throw new ArgumentException("Tool arguments must be an object.");
            await calls.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(60));
                    // Framing cannot be safely resumed after a canceled or partial exchange.
                    using (deadline.Token.Register(Dispose))
                    {
                        var result = await RequestAsync("tools/call", new { name = name, arguments = arguments }, deadline.Token).ConfigureAwait(false);
                        deadline.Token.ThrowIfCancellationRequested();
                        return Json().Serialize(result);
                    }
                }
            }
            catch { Dispose(); throw; }
            finally { calls.Release(); }
        }

        private async Task<object> RequestAsync(string method, object parameters, CancellationToken cancellation)
        {
            int id = Interlocked.Increment(ref nextId);
            await SendAsync(new { jsonrpc = "2.0", id = id, method = method, @params = parameters }, cancellation).ConfigureAwait(false);
            string line = await ReadLineAsync(MessageLimit, cancellation).ConfigureAwait(false);
            var response = Json().DeserializeObject(line) as Dictionary<string, object>;
            object protocol, actualId, result;
            if (response == null || response.Count != 3 || !response.TryGetValue("jsonrpc", out protocol) || (protocol as string) != "2.0"
                || !response.TryGetValue("id", out actualId) || !(actualId is int) || (int)actualId != id
                || !response.TryGetValue("result", out result) || result == null) throw new IOException("Invalid engineering MCP response.");
            return result;
        }
        private Task SendAsync(object message, CancellationToken cancellation) { return WriteLineAsync(Json().Serialize(message), MessageLimit, cancellation); }
        private async Task WriteLineAsync(string line, int limit, CancellationToken cancellation)
        {
            byte[] bytes = Utf8.GetBytes(line + "\n");
            try
            {
                if (bytes.Length - 1 > limit) throw new IOException("Engineering message too large.");
                FileStream stream; lock (sync) { if (disposed || input == null) throw new ObjectDisposedException(nameof(EngineeringMcpChild)); stream = input; }
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellation).ConfigureAwait(false);
                await stream.FlushAsync(cancellation).ConfigureAwait(false);
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        private async Task<string> ReadLineAsync(int limit, CancellationToken cancellation)
        {
            FileStream stream; lock (sync) { if (disposed || output == null) throw new ObjectDisposedException(nameof(EngineeringMcpChild)); stream = output; }
            using (var buffer = new MemoryStream())
            {
                var one = new byte[1];
                for (;;)
                {
                    if (await stream.ReadAsync(one, 0, 1, cancellation).ConfigureAwait(false) == 0) throw new EndOfStreamException("Engineering child exited.");
                    if (one[0] == 10) return Utf8.GetString(buffer.ToArray());
                    if (buffer.Length == limit) throw new IOException("Engineering message too large.");
                    buffer.WriteByte(one[0]);
                }
            }
        }
        private static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = MessageLimit + 4096, RecursionLimit = 32 }; }

        // Verify the selected launcher and the full package inventory, including unlisted files.
        private static void VerifyPackage(string root, CancellationToken cancellation)
        {
            var hostPath = new Uri(typeof(EngineeringMcpChild).Assembly.CodeBase).LocalPath;
            string selected = NativeInstallationSelection.SelectRoot(Path.Combine(Path.GetDirectoryName(hostPath), "assistant-installation.json"));
            if (selected == null || !string.Equals(selected, root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Engineering installation selection changed.");
            string manifestPath = Path.Combine(root, "application-manifest.json");
            var info = new FileInfo(manifestPath);
            if (!info.Exists || info.Length > 32 * 1024 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Invalid package manifest.");
            Dictionary<string, object> manifest;
            using (var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream, Utf8, false)) manifest = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
            object entries;
            if (manifest == null || !manifest.TryGetValue("files", out entries) || !(entries is object[])) throw new IOException("Invalid package manifest.");
            const string prefix = "runtime/mcp/engineering/";
            var expected = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in (object[])entries)
            {
                var entry = item as Dictionary<string, object>; object pathValue;
                if (entry == null || !entry.TryGetValue("path", out pathValue) || !(pathValue is string)) throw new IOException("Invalid package entry.");
                string path = (string)pathValue;
                if (entry.Count != 3 || !entry.ContainsKey("size") || !(entry.ContainsKey("sha256") && entry["sha256"] is string)
                    || path.Split('/').Any(part => part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(new[] { '\\', ':', '\0' }) >= 0)
                    || expected.ContainsKey(path)) throw new IOException("Invalid engineering package entry.");
                expected.Add(path, entry);
            }
            if (!expected.ContainsKey(prefix + "packages/mcp/dist/index.js")) throw new IOException("Engineering entry is missing.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            VerifyTree(root, "", expected, seen, cancellation);
            if (seen.Count != expected.Count) throw new IOException("Incomplete engineering package.");
        }
        private static void VerifyTree(string directory, string relative, Dictionary<string, Dictionary<string, object>> expected, HashSet<string> seen, CancellationToken cancellation)
        {
            foreach (string path in Directory.GetFileSystemEntries(directory))
            {
                cancellation.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected engineering package.");
                string name = (relative.Length == 0 ? "" : relative + "/") + Path.GetFileName(path);
                if (name == "application-manifest.json" && (attributes & FileAttributes.Directory) == 0) continue;
                if ((attributes & FileAttributes.Directory) != 0) { VerifyTree(path, name, expected, seen, cancellation); continue; }
                Dictionary<string, object> entry;
                if (!seen.Add(name) || !expected.TryGetValue(name, out entry)) throw new IOException("Unlisted engineering package file.");
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                {
                    if (file.Length != Convert.ToInt64(entry["size"], CultureInfo.InvariantCulture)) throw new IOException("Engineering package size changed.");
                    string hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(hash, (string)entry["sha256"], StringComparison.Ordinal)) throw new IOException("Engineering package hash changed.");
                }
            }
        }
        private void Launch(string root)
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(EngineeringMcpChild));
                SafeFileHandle childRead = null, parentWrite = null, parentRead = null, childWrite = null, nul = null;
                IntPtr attributeList = IntPtr.Zero, handleArray = IntPtr.Zero, environment = IntPtr.Zero;
                ProcessInformation process = new ProcessInformation(); bool created = false, resumed = false, attributesInitialized = false;
                try
                {
                    var security = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)), Inherit = true };
                    if (!CreatePipe(out childRead, out parentWrite, ref security, 0) || !CreatePipe(out parentRead, out childWrite, ref security, 0)) throw Win32();
                    if (!SetHandleInformation(parentWrite, 1, 0) || !SetHandleInformation(parentRead, 1, 0)) throw Win32();
                    nul = CreateFile("NUL", 0x40000000, 3, ref security, 3, 0, IntPtr.Zero);
                    if (nul.IsInvalid) throw Win32();
                    job = CreateJobObject(IntPtr.Zero, null);
                    if (job.IsInvalid) throw Win32();
                    var limits = new ExtendedLimits(); limits.Basic.LimitFlags = 0x2000;
                    int size = Marshal.SizeOf(typeof(ExtendedLimits)); IntPtr memory = Marshal.AllocHGlobal(size);
                    try { Marshal.StructureToPtr(limits, memory, false); if (!SetInformationJobObject(job, 9, memory, (uint)size)) throw Win32(); }
                    finally { Marshal.FreeHGlobal(memory); }
                    IntPtr listSize = IntPtr.Zero;
                    InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
                    if (listSize == IntPtr.Zero) throw Win32();
                    attributeList = Marshal.AllocHGlobal(listSize);
                    if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref listSize)) throw Win32();
                    attributesInitialized = true;
                    handleArray = Marshal.AllocHGlobal(IntPtr.Size * 3);
                    Marshal.WriteIntPtr(handleArray, 0, childRead.DangerousGetHandle());
                    Marshal.WriteIntPtr(handleArray, IntPtr.Size, childWrite.DangerousGetHandle());
                    Marshal.WriteIntPtr(handleArray, IntPtr.Size * 2, nul.DangerousGetHandle());
                    if (!UpdateProcThreadAttribute(attributeList, 0, (IntPtr)0x00020002, handleArray, (IntPtr)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero)) throw Win32();
                    var startup = new StartupInfoEx { Startup = new StartupInfo { Size = (uint)Marshal.SizeOf(typeof(StartupInfoEx)), Flags = 0x100,
                        StdInput = childRead.DangerousGetHandle(), StdOutput = childWrite.DangerousGetHandle(), StdError = nul.DangerousGetHandle() }, Attributes = attributeList };
                    string node = Path.Combine(root, "runtime", "node", "node.exe");
                    string script = Path.Combine(root, "runtime", "mcp", "engineering", "packages", "mcp", "dist", "index.js");
                    var command = new StringBuilder(Quote(node) + " " + Quote(script) + " --host-bootstrap-stdin");
                    var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string key in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP" })
                    { string value = Environment.GetEnvironmentVariable(key); if (!string.IsNullOrEmpty(value) && value.IndexOf('\0') < 0) variables[key] = value; }
                    string block = string.Join("\0", variables.Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
                    environment = Marshal.StringToHGlobalUni(block);
                    created = CreateProcess(node, command, IntPtr.Zero, IntPtr.Zero, true, 0x4 | 0x400 | 0x08000000 | 0x00080000,
                        environment, root, ref startup, out process);
                    if (!created) throw Win32();
                    if (!AssignProcessToJobObject(job, process.Process)) throw Win32();
                    input = new FileStream(parentWrite, FileAccess.Write, 4096, false); parentWrite = null;
                    output = new FileStream(parentRead, FileAccess.Read, 4096, false); parentRead = null;
                    if (ResumeThread(process.Thread) == uint.MaxValue) throw Win32();
                    resumed = true;
                }
                finally
                {
                    if (created && !resumed) TerminateProcess(process.Process, 1);
                    if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
                    if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
                    if (attributeList != IntPtr.Zero) { if (attributesInitialized) DeleteProcThreadAttributeList(attributeList); Marshal.FreeHGlobal(attributeList); }
                    if (handleArray != IntPtr.Zero) Marshal.FreeHGlobal(handleArray);
                    if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
                    if (childRead != null) childRead.Dispose(); if (childWrite != null) childWrite.Dispose(); if (nul != null) nul.Dispose();
                    if (parentRead != null) parentRead.Dispose(); if (parentWrite != null) parentWrite.Dispose();
                }
            }
        }
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                // Killing the job also closes the child's pipe ends and unblocks pending reads.
                if (job != null) { job.Dispose(); job = null; }
                if (input != null) { input.Dispose(); input = null; }
                if (output != null) { output.Dispose(); output = null; }
                if (transport != null) { transport.Dispose(); transport = null; }
            }
        }
        private static string Quote(string value)
        {
            if (value.IndexOf('\0') >= 0) throw new IOException("Invalid process path.");
            var text = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); text.Append(c); slashes = 0;
            }
            return text.Append('\\', slashes * 2).Append('"').ToString();
        }
        private static Win32Exception Win32() { return new Win32Exception(Marshal.GetLastWin32Error()); }
        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
        {
            public uint Size; public string Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
            public ushort Show, ReservedCount; public IntPtr ReservedPointer, StdInput, StdOutput, StdError;
        }
        [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Startup; public IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes security, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, ref SecurityAttributes security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int type, IntPtr info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnedSize);
        [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    }
}
