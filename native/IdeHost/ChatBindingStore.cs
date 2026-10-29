using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TwinCatAssistant.Native
{
    internal sealed class ChatBindingStore
    {
        private readonly string _directory;
        private readonly string _stateRoot;
        private string StateRoot { get { return _stateRoot ?? Path.GetFullPath(NativeChatConfiguration.Load().StateRoot).TrimEnd('\\', '/'); } }
        public ChatBindingStore(string directory = null) : this(directory, null) { }
        public ChatBindingStore(string directory, string stateRoot)
        {
            _directory = directory ?? Path.Combine(NativeChatConfiguration.CredentialDirectory, "threads");
            if (stateRoot != null || directory != null) _stateRoot = Path.GetFullPath(stateRoot ?? Path.GetDirectoryName(directory)).TrimEnd('\\', '/');
        }
        private string FileName(string origin, string session)
        {
            if (!ChatSelection.ValidSession(session)) throw new InvalidOperationException("Invalid chat session.");
            return Path.Combine(_directory, ChatContextPolicy.Hash(StateRoot.ToLowerInvariant() + "\0" + session) + ".json");
        }
        public ChatContextBinding Read(string origin, string session)
        {
            var file = FileName(origin, session);
            if (!Directory.Exists(_directory)) return null;
            VerifyDirectory();
            if (File.Exists(file))
            {
                var value = ReadBinding(file);
                if (!ScopeMatches(value)) throw new IOException("Stored chat profile is invalid.");
                return value;
            }
            // 0.1.9 keyed bindings by the listener's port. Adopt the same session
            // in this protected state root even after the listener moves.
            foreach (var candidate in Directory.EnumerateFiles(_directory, "*.json"))
            {
                try
                {
                    var result = ReadBinding(candidate);
                    if (result.SessionId == session && ScopeMatches(result)) return result;
                }
                catch { /* An unrelated malformed legacy file cannot become authoritative. */ }
            }
            return null;
        }
        private bool ScopeMatches(ChatContextBinding value) { return value.StateRoot == null || string.Equals(value.StateRoot, StateRoot, StringComparison.OrdinalIgnoreCase); }
        private ChatContextBinding ReadBinding(string file)
        {
            var value = new JavaScriptSerializer().Deserialize<ChatContextBinding>(Encoding.UTF8.GetString(NativeChatFiles.Read(file)));
            if (value == null || value.Version != 1 || !ChatSelection.ValidSession(value.SessionId) || value.IdeProcessId <= 0
                || string.IsNullOrEmpty(value.IdeInstanceId) || !Uri.TryCreate(value.Origin, UriKind.Absolute, out var uri)
                || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != ""
                || (!value.IsGeneral && (string.IsNullOrEmpty(value.SolutionFile) || string.IsNullOrEmpty(value.ProjectFile))))
                throw new IOException("Stored chat binding is invalid.");
            var legacy = ChatContextPolicy.Hash(value.Origin + "\0" + value.SessionId) + ".json";
            if (!string.Equals(Path.GetFileName(file), legacy, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(file, FileName(value.Origin, value.SessionId), StringComparison.OrdinalIgnoreCase)) throw new IOException("Stored chat identity is invalid.");
            return value;
        }
        // Sessions previously bound to exactly this solution/PLC project on this
        // chat origin. Invalid or foreign files are skipped, never repaired.
        public System.Collections.Generic.HashSet<string> SessionsForProject(string origin, NativeProjectIdentity identity)
        {
            var result = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            if (identity == null || !Directory.Exists(_directory)) return result;
            VerifyDirectory();
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                try
                {
                    var binding = ReadBinding(file);
                    if (ScopeMatches(binding) && binding.SameProjectContext(identity)) result.Add(binding.SessionId);
                }
                catch { /* Skip unreadable bindings; Read() still rejects them for prompts. */ }
            }
            return result;
        }
        public void Bind(ChatContextBinding binding)
        {
            binding.StateRoot = StateRoot;
            EnsureDirectory();
            var sid = WindowsIdentity.GetCurrent().User.Value;
            using (var gate = new Mutex(false, "Local\\TwinCatAssistant.ChatBinding." + sid))
            {
                bool acquired;
                try { acquired = gate.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Another IDE is binding a chat. Retry when it finishes.");
                try
                {
                    var prior = Read(binding.Origin, binding.SessionId);
                    if (prior != null)
                    {
                        if (!prior.IsGeneral && (binding.IsGeneral || !string.Equals(prior.SolutionFile, binding.SolutionFile, StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(prior.ProjectFile, binding.ProjectFile, StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException("This chat belongs to another project. Start a new project chat.");
                        if (prior.IdeInstanceId != binding.IdeInstanceId)
                        {
                            bool live;
                            try { using (var process = Process.GetProcessById(prior.IdeProcessId)) live = !process.HasExited; }
                            catch (ArgumentException) { live = false; }
                            if (live && prior.IdeProcessId != binding.IdeProcessId) throw new InvalidOperationException("This chat belongs to another live IDE instance. Start a new project chat.");
                        }
                    }
                    var destination = FileName(binding.Origin, binding.SessionId);
                    var temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        var bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(binding));
                        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                        if (!File.Exists(destination)) File.Move(temporary, destination);
                        else File.Replace(temporary, destination, null);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                finally { gate.ReleaseMutex(); }
            }
        }
        private void VerifyDirectory()
        {
            if ((new DirectoryInfo(_directory).Attributes & FileAttributes.ReparsePoint) != 0
                || (new DirectoryInfo(Path.GetDirectoryName(_directory)).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Redirected chat binding directory rejected.");
        }
        private void EnsureDirectory()
        {
            var parent = new DirectoryInfo(Path.GetDirectoryName(_directory));
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Redirected chat binding parent rejected.");
            Directory.CreateDirectory(_directory);
            VerifyDirectory();
            var sid = WindowsIdentity.GetCurrent().User;
            var acl = new DirectorySecurity();
            acl.SetOwner(sid); acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(_directory).SetAccessControl(acl);
        }
    }
}
