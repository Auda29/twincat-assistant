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
        public ChatBindingStore(string directory = null)
        {
            _directory = directory ?? Path.Combine(NativeChatConfiguration.CredentialDirectory, "threads");
        }
        private string FileName(string origin, string session)
        {
            if (!ChatSelection.ValidSession(session)) throw new InvalidOperationException("Invalid chat session.");
            return Path.Combine(_directory, ChatContextPolicy.Hash(origin + "\0" + session) + ".json");
        }
        public ChatContextBinding Read(string origin, string session)
        {
            var file = FileName(origin, session);
            if (!Directory.Exists(_directory)) return null;
            VerifyDirectory();
            if (!File.Exists(file)) return null;
            var result = new JavaScriptSerializer().Deserialize<ChatContextBinding>(Encoding.UTF8.GetString(NativeChatFiles.Read(file)));
            if (result == null || result.Version != 1 || result.Origin != origin || result.SessionId != session
                || result.IdeProcessId <= 0 || string.IsNullOrEmpty(result.IdeInstanceId)
                || (!result.IsGeneral && (string.IsNullOrEmpty(result.SolutionFile) || string.IsNullOrEmpty(result.ProjectFile)))) throw new IOException("Stored chat binding is invalid.");
            return result;
        }
        public void Bind(ChatContextBinding binding)
        {
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
                        if (prior == null) File.Move(temporary, destination);
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
