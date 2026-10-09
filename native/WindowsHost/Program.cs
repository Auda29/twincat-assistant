using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace TwinCatAssistant.WindowsHost;

internal static class Program
{
    private const int MaxJsonBytes = 65536;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return 2;
            if (args is ["--version"]) { Console.WriteLine("1.0.0"); return 0; }
            if (args is ["read-json", var path])
            {
                var bytes = ReadRegularFile(path);
                // Validate before emitting. Never print a parser exception containing operator data.
                using var document = JsonDocument.Parse(bytes);
                await Console.OpenStandardOutput().WriteAsync(bytes);
                return 0;
            }
            if (args is ["secure-directory", var directory])
            {
                if (!Path.IsPathFullyQualified(directory)) throw new IOException();
                var info = new DirectoryInfo(directory);
                if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException();
                var sid = WindowsIdentity.GetCurrent().User ?? throw new IOException();
                var acl = new DirectorySecurity();
                acl.SetOwner(sid);
                acl.SetAccessRuleProtection(true, false);
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                info.SetAccessControl(acl);
                return 0;
            }
            if (args is ["supervise"]) return await JobSupervisor.RunAsync();
            return 2;
        }
        catch
        {
            Console.Error.WriteLine("Windows helper rejected the operation.");
            return 2;
        }
    }

    private static byte[] ReadRegularFile(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException();
        using var handle = Native.CreateFile(path, Native.GenericRead, Native.ShareRead, IntPtr.Zero,
            Native.OpenExisting, Native.OpenReparsePoint | Native.BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid || !Native.GetFileInformationByHandle(handle, out var info) ||
            Native.GetFileType(handle) != 1 || (info.Attributes & (0x10u | 0x400u)) != 0 ||
            info.SizeHigh != 0 || info.SizeLow > MaxJsonBytes) throw new IOException();
        // No write/delete sharing: the opened object cannot be replaced while it is read.
        using var stream = new FileStream(handle, FileAccess.Read);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            if (output.Length + count > MaxJsonBytes) throw new IOException();
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}

internal sealed record LaunchPlan(string Command, string[] Args, string Cwd, Dictionary<string, string> Env);

internal static class JobSupervisor
{
    public static async Task<int> RunAsync()
    {
        // The launch plan (including credentials) travels over stdin, never a command line or file.
        var line = await ReadControlAsync();
        var plan = JsonSerializer.Deserialize<LaunchPlan>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new IOException();
        if (!Path.IsPathFullyQualified(plan.Command) || !File.Exists(plan.Command) ||
            !Path.IsPathFullyQualified(plan.Cwd) || !Directory.Exists(plan.Cwd) ||
            plan.Args is null || plan.Env is null) throw new IOException();
        using var job = Native.CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new IOException();
        var limits = new Native.ExtendedLimits();
        limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        var length = Marshal.SizeOf<Native.ExtendedLimits>();
        var memory = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(limits, memory, false);
            if (!Native.SetInformationJobObject(job, 9, memory, (uint)length)) throw new IOException();
        }
        finally { Marshal.FreeHGlobal(memory); }

        var command = new StringBuilder(string.Join(" ", new[] { plan.Command }.Concat(plan.Args).Select(Quote)));
        if (command.Length >= 32767) throw new IOException();
        var environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in plan.Env)
        {
            if (pair.Key.Length == 0 || pair.Key.Contains('=') || pair.Key.Contains('\0') || pair.Value.Contains('\0')) throw new IOException();
            environment[pair.Key] = pair.Value;
        }
        var block = string.Join("\0", environment.Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
        var environmentMemory = Marshal.StringToHGlobalUni(block);
        Native.ProcessInformation process = default;
        var created = false;
        try
        {
            var startup = new Native.StartupInfo { Size = (uint)Marshal.SizeOf<Native.StartupInfo>(), Flags = 0x100 };
            // stdout is reserved for readiness. Upstream output is forwarded only to stderr.
            startup.StdOutput = Native.GetStdHandle(-12);
            startup.StdError = Native.GetStdHandle(-12);
            startup.StdInput = IntPtr.Zero;
            created = Native.CreateProcess(plan.Command, command, IntPtr.Zero, IntPtr.Zero, true,
                0x4 | 0x400 | 0x08000000, environmentMemory, plan.Cwd, ref startup, out process);
            if (!created) throw new IOException();
            // Assign before resuming: there is no child-spawn race outside the owned job.
            if (!Native.AssignProcessToJobObject(job, process.Process)) throw new IOException();
            if (Native.ResumeThread(process.Thread) == uint.MaxValue) throw new IOException();
            Console.WriteLine(JsonSerializer.Serialize(new { started = true, pid = process.ProcessId }));
            await Console.Out.FlushAsync();
            var exit = Task.Run(() =>
            {
                if (Native.WaitForSingleObject(process.Process, uint.MaxValue) != 0) throw new IOException();
                if (!Native.GetExitCodeProcess(process.Process, out var code)) throw new IOException();
                return unchecked((int)code);
            });
            // Console.In is a synchronized TextReader whose ReadAsync can block synchronously.
            // Keep that read off the exit-monitor path so normal child exit is observed.
            var control = Task.Run(ReadControlAsync);
            var completed = await Task.WhenAny(exit, control);
            if (completed == control)
            {
                // A stop command or stdin EOF (dead Node owner) both revoke ownership.
                try { await control; } catch (EndOfStreamException) { }
                if (!Native.TerminateJobObject(job, 1)) throw new IOException();
                await exit;
                return 0;
            }
            var result = await exit;
            // Also terminate descendants after a normally exiting root process.
            if (!Native.TerminateJobObject(job, 1)) throw new IOException();
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(environmentMemory);
            if (created)
            {
                Native.TerminateProcess(process.Process, 1); // covers assign/resume failures
                Native.CloseHandle(process.Thread);
                Native.CloseHandle(process.Process);
            }
        }
    }

    private static async Task<string> ReadControlAsync()
    {
        var input = Console.In;
        var result = new StringBuilder();
        var one = new char[1];
        while (await input.ReadAsync(one) != 0)
        {
            if (one[0] == '\n') return result.ToString().TrimEnd('\r');
            if (result.Length >= 1024 * 1024) throw new IOException();
            result.Append(one[0]);
        }
        throw new EndOfStreamException();
    }

    private static string Quote(string argument)
    {
        if (argument.Contains('\0')) throw new IOException();
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') result.Append('\\', slashes * 2 + 1);
            else result.Append('\\', slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}

internal static class Native
{
    internal const uint GenericRead = 0x80000000, ShareRead = 1, OpenExisting = 3,
        OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
    [StructLayout(LayoutKind.Sequential)] internal struct FileInformation
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        internal uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits
    {
        internal long ProcessTime, JobTime;
        internal uint LimitFlags;
        internal UIntPtr MinWorkingSet, MaxWorkingSet;
        internal uint ActiveProcesses;
        internal UIntPtr Affinity;
        internal uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct IoCounters
    {
        internal ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits
    {
        internal BasicLimits Basic;
        internal IoCounters Io;
        internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct StartupInfo
    {
        internal uint Size;
        internal string? Reserved, Desktop, Title;
        internal uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        internal ushort Show, ReservedCount;
        internal IntPtr ReservedPointer, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ProcessInformation
    {
        internal IntPtr Process, Thread;
        internal uint ProcessId, ThreadId;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll")] internal static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(SafeFileHandle job, int type, IntPtr information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateJobObject(SafeFileHandle job, uint code);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
        bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] internal static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] internal static extern IntPtr GetStdHandle(int id);
}
