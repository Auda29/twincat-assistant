using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace TwinCatAssistant.Native
{
    public sealed class NativeChatConfiguration
    {
        public string NodePath { get; set; }
        public string RepositoryRoot { get; set; }
        public string StateRoot { get; set; }
        public string DotNetRoot { get; set; }
        public static string ConfigurationPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinCatAssistant", "native-chat.json"); } }
        public static string CredentialDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinCatAssistant", "native-chat"); } }

        public static NativeChatConfiguration Load()
        {
            var result = new JavaScriptSerializer().Deserialize<NativeChatConfiguration>(Encoding.UTF8.GetString(NativeChatFiles.Read(ConfigurationPath)));
            result.Validate();
            return result;
        }
        public void Validate()
        {
            foreach (var value in new[] { NodePath, RepositoryRoot, StateRoot, DotNetRoot })
                if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || !Path.IsPathRooted(value)
                    || !string.Equals(Path.GetFullPath(value), value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Configure absolute native chat paths first.");
            if (!File.Exists(NodePath) || !File.Exists(Path.Combine(RepositoryRoot, "src", "native-chat.mjs"))
                || !File.Exists(Path.Combine(RepositoryRoot, "native", "WindowsHost", "bin", "Release", "net8.0-windows", "WindowsHost.exe"))
                || !File.Exists(Path.Combine(DotNetRoot, "dotnet.exe")))
                throw new InvalidOperationException("Native chat prerequisites are missing. Run Configure-NativeChat.ps1 after building.");
        }
    }

    public sealed class NativeChatCredentials
    {
        private readonly string _directory;
        public NativeChatCredentials(string directory = null) { _directory = directory ?? NativeChatConfiguration.CredentialDirectory; }
        private string FilePath { get { return Path.Combine(_directory, "ui-password.bin"); } }
        public bool Exists { get { return File.Exists(FilePath); } }
        public string Load()
        {
            var bytes = ProtectedData.Unprotect(NativeChatFiles.Read(FilePath), null, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        public void Save(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || password.Length > 1024 || password.IndexOf('\0') >= 0)
                throw new ArgumentException("Choose a local chat password with 12 to 1024 characters.");
            var directory = new DirectoryInfo(_directory);
            directory.Create();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected credential directory rejected.");
            var sid = WindowsIdentity.GetCurrent().User;
            var acl = new DirectorySecurity();
            acl.SetOwner(sid);
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
            var bytes = Encoding.UTF8.GetBytes(password);
            try
            {
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                using (var file = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(encrypted, 0, encrypted.Length); file.Flush(true); }
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
    }

    public sealed class NativeChatSession : IDisposable
    {
        private Process _process;
        private bool _stopping;
        public event Action Stopped;
        public int Port { get; private set; }
        public bool IsRunning { get { try { return _process != null && !_process.HasExited; } catch { return false; } } }

        public async Task StartAsync(NativeChatConfiguration config, int port, string password)
        {
            config.Validate();
            Port = port;
            var start = new ProcessStartInfo {
                FileName = config.NodePath,
                Arguments = Quote(Path.Combine(config.RepositoryRoot, "src", "native-chat.mjs")),
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
            _process = new Process { StartInfo = start };
            _process.Exited += OnExited;
            if (!_process.Start()) throw new IOException("The local chat launcher could not start.");
            _process.EnableRaisingEvents = true;
            // Upstream logs are deliberately drained without displaying them or
            // retaining credentials/URLs in an IDE error message.
            var drain = DrainAsync(_process.StandardError);
            var request = new { state = config.StateRoot, port, password };
            try
            {
                // A GUI .NET Framework process may use an ANSI/OEM encoding for
                // StandardInput. Send UTF-8 bytes explicitly so Unicode passwords
                // and state paths reach the Node protocol without substitution.
                var input = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(request) + "\n");
                try
                {
                    await _process.StandardInput.BaseStream.WriteAsync(input, 0, input.Length);
                    await _process.StandardInput.BaseStream.FlushAsync();
                }
                finally { Array.Clear(input, 0, input.Length); }
                var response = ReadReadyAsync(_process.StandardOutput);
                if (await Task.WhenAny(response, Task.Delay(80000)) != response)
                    throw new IOException("Local chat startup timed out.");
                var value = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(await response);
                if (!value.ContainsKey("status") || (string)value["status"] != "ready"
                    || !value.ContainsKey("port") || Convert.ToInt32(value["port"]) != port)
                    throw new IOException("Local chat startup failed. Check the configured runtime, port and profile lock.");
                if (!IsRunning || !await IsProtectedServiceReadyAsync(port))
                    throw new IOException("The protected local chat service did not become ready.");
            }
            catch { await StopAsync(); throw; }
        }

        public static async Task<bool> IsProtectedServiceReadyAsync(int port)
        {
            if (port < 1024 || port > 65535) return false;
            try
            {
                using (var cancel = new CancellationTokenSource(1500))
                using (var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
                using (var client = new HttpClient(handler))
                using (var response = await client.GetAsync("http://127.0.0.1:" + port + "/auth/session", HttpCompletionOption.ResponseHeadersRead, cancel.Token))
                {
                    if (response.StatusCode != HttpStatusCode.Unauthorized) return false;
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var data = new MemoryStream())
                    {
                        var buffer = new byte[1024];
                        int length;
                        while ((length = await stream.ReadAsync(buffer, 0, buffer.Length, cancel.Token)) != 0)
                        { if (data.Length + length > 4096) return false; await data.WriteAsync(buffer, 0, length, cancel.Token); }
                        var value = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(data.ToArray()));
                        return value.ContainsKey("authenticated") && value["authenticated"] is bool authenticated && !authenticated
                            && value.ContainsKey("locked") && value["locked"] is bool locked && locked;
                    }
                }
            }
            catch { return false; }
        }

        public async Task StopAsync()
        {
            var process = _process;
            if (process == null) return;
            _stopping = true;
            try { process.StandardInput.Close(); } catch { }
            await Task.Run(() => {
                try { if (!process.WaitForExit(5000)) { process.Kill(); process.WaitForExit(2000); } } catch { }
            });
            process.Exited -= OnExited;
            process.Dispose();
            if (_process == process) _process = null;
        }
        public void Dispose() { var stop = StopAsync(); }
        private void OnExited(object sender, EventArgs args) { if (!_stopping) Stopped?.Invoke(); }
        internal static async Task DrainAsync(StreamReader reader)
        { try { var buffer = new char[1024]; while (await reader.ReadAsync(buffer, 0, buffer.Length) > 0) { } } catch { } }
        internal static async Task<string> ReadReadyAsync(StreamReader reader)
        {
            var result = new StringBuilder();
            var one = new char[1];
            while (await reader.ReadAsync(one, 0, 1) > 0)
            {
                if (one[0] == '\n') return result.ToString();
                if (result.Length >= 4096) throw new IOException("Invalid local chat response.");
                result.Append(one[0]);
            }
            throw new IOException("Local chat launcher exited before readiness.");
        }
        internal static string Quote(string argument)
        {
            if (argument.IndexOf('"') >= 0 || argument.IndexOf('\0') >= 0 || argument.EndsWith("\\", StringComparison.Ordinal))
                throw new ArgumentException("Invalid launcher path.");
            return "\"" + argument + "\"";
        }
    }

    // Same no-follow, bounded leaf read contract as the existing Windows helper.
    internal static class NativeChatFiles
    {
        internal static byte[] Read(string path)
        {
            using (var handle = CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero))
            {
                FileInformation info;
                if (handle.IsInvalid || !GetFileInformationByHandle(handle, out info) || GetFileType(handle) != 1
                    || (info.Attributes & (0x10u | 0x400u)) != 0 || info.SizeHigh != 0 || info.SizeLow > 65536)
                    throw new IOException("Native chat file is missing, redirected or oversized.");
                using (var stream = new FileStream(handle, FileAccess.Read))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[4096]; int length;
                    while ((length = stream.Read(buffer, 0, buffer.Length)) > 0)
                    { if (output.Length + length > 65536) throw new IOException(); output.Write(buffer, 0, length); }
                    return output.ToArray();
                }
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct FileInformation
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            internal uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string file, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation info);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle file);
    }
}
