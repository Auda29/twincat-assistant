using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
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
    // One private capability per instance. The owner must only deliver it over a trusted channel.
    internal sealed class EngineeringHostTransport : IDisposable
    {
        private const int MaxFrame = 256 * 1024;
        private const int MaxResponse = 512 * 1024;
        private const int MaxConnections = 4;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly Func<string, CancellationToken, Task<string>> handler;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly SemaphoreSlim slots = new SemaphoreSlim(MaxConnections, MaxConnections);
        private readonly SemaphoreSlim handlerSlots = new SemaphoreSlim(MaxConnections, MaxConnections);
        private readonly HashSet<NamedPipeServerStream> pipes = new HashSet<NamedPipeServerStream>();
        private readonly object gate = new object();
        private readonly byte[] capability;
        private readonly byte[] securityDescriptor;
        private bool disposed;

        public string PipeName { get; private set; }
        public string Capability { get; private set; }
        public string SessionId { get; private set; }

        public EngineeringHostTransport(Guid sessionId, Func<string, CancellationToken, Task<string>> handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            if (sessionId == Guid.Empty) throw new ArgumentException("Session ID must not be empty.", "sessionId");
            SessionId = sessionId.ToString("D");
            this.handler = handler;
            capability = RandomBytes(32);
            Capability = Hex(capability);
            PipeName = "twincat-assistant-engineering-" + Hex(RandomBytes(32));
            var sid = WindowsIdentity.GetCurrent().User;
            if (sid == null) throw new InvalidOperationException("Current user SID is unavailable.");
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
            securityDescriptor = security.GetSecurityDescriptorBinaryForm();
            // Create the first listener synchronously so initialization errors reach the owner.
            var first = CreateListener();
            lock (gate) pipes.Add(first);
            Task.Run(() => AcceptLoop(first));
        }

        private async Task AcceptLoop(NamedPipeServerStream first)
        {
            NamedPipeServerStream next = first;
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await slots.WaitAsync(stop.Token).ConfigureAwait(false);
                    if (next == null)
                    {
                        try
                        {
                            next = CreateListener();
                            lock (gate)
                            {
                                if (disposed) { next.Dispose(); slots.Release(); break; }
                                pipes.Add(next);
                            }
                        }
                        catch { slots.Release(); throw; }
                    }
                    try { await next.WaitForConnectionAsync(stop.Token).ConfigureAwait(false); }
                    catch { Remove(next); slots.Release(); next = null; throw; }
                    var connected = next;
                    next = null;
                    ProcessConnection(connected);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            finally { if (next != null) Remove(next); }
        }

        private async Task ProcessConnection(NamedPipeServerStream pipe)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using (timeout.Token.Register(() => { try { pipe.Dispose(); } catch (ObjectDisposedException) { } }))
                {
                    try
                    {
                        string serverNonce = Hex(RandomBytes(32));
                        var serializer = new JavaScriptSerializer { MaxJsonLength = MaxResponse + 4096 };
                        await WriteFrame(pipe, serializer.Serialize(new { protocolVersion = 1, sessionId = SessionId, serverNonce = serverNonce }), 4096, timeout.Token).ConfigureAwait(false);
                        var hello = ParseObject(await ReadFrame(pipe, 4096, timeout.Token).ConfigureAwait(false), "clientNonce", "proof");
                        if (hello == null || !IsHex(hello["clientNonce"]) || !CheckProof(hello["proof"], Transcript("client", serverNonce, hello["clientNonce"], null))) return;
                        string clientNonce = hello["clientNonce"];
                        await WriteFrame(pipe, serializer.Serialize(new { proof = Sign(Transcript("server", serverNonce, clientNonce, null)) }), 4096, timeout.Token).ConfigureAwait(false);
                        var message = ParseObject(await ReadFrame(pipe, MaxFrame, timeout.Token).ConfigureAwait(false), "protocolVersion", "request", "proof");
                        if (message == null || message["protocolVersion"] != "1" || !CheckProof(message["proof"], Transcript("request", serverNonce, clientNonce, Hash(message["request"])))) return;
                        await handlerSlots.WaitAsync(timeout.Token).ConfigureAwait(false);
                        Task<string> work;
                        try { work = handler(message["request"], timeout.Token); if (work == null) throw new InvalidOperationException("Handler returned no task."); }
                        catch { handlerSlots.Release(); throw; }
                        work.ContinueWith(t => { var ignored = t.Exception; handlerSlots.Release(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                        if (await Task.WhenAny(work, Task.Delay(Timeout.Infinite, timeout.Token)).ConfigureAwait(false) != work) return;
                        var response = await work.ConfigureAwait(false);
                        timeout.Token.ThrowIfCancellationRequested();
                        if (response == null || StrictUtf8.GetByteCount(response) > MaxResponse) return;
                        string encoded = serializer.Serialize(new { response = response, proof = Sign(Transcript("response", serverNonce, clientNonce, Hash(response))) });
                        await WriteFrame(pipe, encoded, MaxResponse, timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception) { /* Do not disclose handler errors or capability material to clients. */ }
                    finally { Remove(pipe); slots.Release(); }
                }
            }
        }

        private static async Task<byte[]> ReadFrame(Stream pipe, int maximum, CancellationToken token)
        {
            using (var data = new MemoryStream())
            {
                // Reading one byte prevents accepting a second frame accidentally.
                var one = new byte[1];
                while (true)
                {
                    int n = await pipe.ReadAsync(one, 0, 1, token).ConfigureAwait(false);
                    if (n == 0) throw new IOException("Incomplete frame.");
                    if (one[0] == 10) return data.ToArray();
                    if (data.Length == maximum) throw new IOException("Frame too large.");
                    data.WriteByte(one[0]);
                }
            }
        }

        private static async Task WriteFrame(Stream pipe, string frame, int maximum, CancellationToken token)
        {
            byte[] bytes = StrictUtf8.GetBytes(frame + "\n");
            if (bytes.Length - 1 > maximum) throw new IOException("Frame too large.");
            await pipe.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
            await pipe.FlushAsync(token).ConfigureAwait(false);
        }
        private static Dictionary<string, string> ParseObject(byte[] data, params string[] keys)
        {
            try
            {
                string json = StrictUtf8.GetString(data); int i = 0;
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                White(json, ref i); Take(json, ref i, '{');
                for (int item = 0; item < keys.Length; item++)
                {
                    if (item != 0) Take(json, ref i, ',');
                    string key = JsonString(json, ref i);
                    if (Array.IndexOf(keys, key) < 0 || result.ContainsKey(key)) return null;
                    Take(json, ref i, ':');
                    string value;
                    if (key == "protocolVersion")
                    {
                        White(json, ref i);
                        if (i >= json.Length || json[i++] != '1') return null;
                        value = "1";
                    }
                    else value = JsonString(json, ref i);
                    result.Add(key, value);
                }
                Take(json, ref i, '}'); White(json, ref i);
                return i == json.Length && result.Count == keys.Length ? result : null;
            }
            catch (Exception) { return null; }
        }
        private string Transcript(string domain, string serverNonce, string clientNonce, string hash)
        {
            return "TCMCP1\n" + domain + "\n" + SessionId + "\n" + serverNonce + "\n" + clientNonce + (hash == null ? "" : "\n" + hash);
        }
        private string Sign(string transcript) { using (var hmac = new HMACSHA256(capability)) return Hex(hmac.ComputeHash(StrictUtf8.GetBytes(transcript))); }
        private static string Hash(string value) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(StrictUtf8.GetBytes(value))); }
        private static bool IsHex(string value)
        {
            if (value == null || value.Length != 64) return false;
            int invalid = 0; for (int i = 0; i < value.Length; i++) invalid |= HexDigit(value[i]) >> 31;
            return invalid == 0;
        }
        private bool CheckProof(string supplied, string transcript)
        {
            string expected = Sign(transcript);
            if (supplied == null || supplied.Length != 64) return false;
            int difference = 0;
            for (int i = 0; i < 64; i++) difference |= supplied[i] ^ expected[i];
            return difference == 0;
        }

        private static string JsonString(string s, ref int i)
        {
            White(s, ref i);
            if (i >= s.Length || s[i] != '"') throw new ArgumentException();
            int start = i++;
            bool escaped = false;
            while (i < s.Length)
            {
                char c = s[i++];
                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                if (c == '"')
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = MaxFrame };
                    return serializer.Deserialize<string>(s.Substring(start, i - start));
                }
                if (c < 0x20) throw new ArgumentException();
            }
            throw new ArgumentException();
        }
        private static void White(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++; }
        private static void Take(string s, ref int i, char c) { White(s, ref i); if (i >= s.Length || s[i++] != c) throw new ArgumentException(); }
        private static int HexDigit(char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1; }
        private static byte[] RandomBytes(int count) { var bytes = new byte[count]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return bytes; }
        private static string Hex(byte[] bytes) { var b = new StringBuilder(bytes.Length * 2); foreach (byte value in bytes) b.Append(value.ToString("x2")); return b.ToString(); }
        private void Remove(NamedPipeServerStream pipe) { lock (gate) pipes.Remove(pipe); pipe.Dispose(); }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                stop.Cancel();
                foreach (var pipe in pipes) pipe.Dispose();
                pipes.Clear();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes { public int Length; public IntPtr SecurityDescriptor; public int InheritHandle; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBufferSize, uint inBufferSize, uint defaultTimeout, ref SecurityAttributes attributes);
        private NamedPipeServerStream CreateListener()
        {
            var pin = GCHandle.Alloc(securityDescriptor, GCHandleType.Pinned);
            try
            {
                var attributes = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)), SecurityDescriptor = pin.AddrOfPinnedObject(), InheritHandle = 0 };
                // FILE_FLAG_OVERLAPPED and PIPE_REJECT_REMOTE_CLIENTS.
                var handle = CreateNamedPipe(@"\\.\pipe\" + PipeName, 0x40000003, 0x00000008, MaxConnections, 4096, 4096, 0, ref attributes);
                if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
                try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
                catch { handle.Dispose(); throw; }
            }
            finally { pin.Free(); }
        }
    }
}



