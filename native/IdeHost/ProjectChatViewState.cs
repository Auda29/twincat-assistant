using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TwinCatAssistant.Native
{
    // Presentation preference of the project chat bar: whether it is open
    // beside the chat for one PLC project in one operator profile (state
    // root). The key is the stable project selection key, so it survives IDE
    // restarts and PID changes, and same-named projects in other solutions or
    // folders stay separate. The file never authorizes anything; unreadable,
    // foreign, redirected or oversized files count as "no preference".
    internal sealed class ProjectChatViewState
    {
        private const int MaxBytes = 1024;
        private static readonly Regex Format = new Regex(@"\A\{""version"":1,""key"":""([0-9a-f]{64})"",""sidebarOpen"":(true|false)(?:,""sidebarPinned"":(true|false))?\}\z", RegexOptions.CultureInvariant);
        private readonly string _directory, _stateRoot;

        public ProjectChatViewState(string directory, string stateRoot)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stateRoot)) throw new ArgumentException("Exact directory and profile required.");
            _directory = Path.GetFullPath(directory);
            _stateRoot = stateRoot;
        }

        private string Key(NativeProjectIdentity project) { return ChatContextPolicy.ProjectSelectionKey(_stateRoot, project); }
        private string FileFor(string key) { return Path.Combine(_directory, "project-" + key + ".view"); }

        // null: nothing usable stored; the caller applies its default.
        public bool? SidebarOpen(NativeProjectIdentity project) { return Read(project, false); }
        public bool? SidebarPinned(NativeProjectIdentity project) { return Read(project, true); }

        private bool? Read(NativeProjectIdentity project, bool pinned)
        {
            try
            {
                if (project == null || !SafeDirectory()) return null;
                var key = Key(project);
                var file = new FileInfo(FileFor(key));
                if (!file.Exists || (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || file.Length > MaxBytes) return null;
                var match = Format.Match(Encoding.UTF8.GetString(File.ReadAllBytes(file.FullName)).Trim());
                if (!match.Success || match.Groups[1].Value != key) return null;
                return pinned ? match.Groups[3].Value == "true" : match.Groups[2].Value == "true";
            }
            catch { return null; }
        }

        // Writes only into the existing protected binding directory; returns
        // false instead of creating or repairing anything.
        public bool RememberSidebar(NativeProjectIdentity project, bool open) { return Remember(project, open, SidebarPinned(project) ?? false); }
        public bool RememberPinned(NativeProjectIdentity project, bool pinned) { return Remember(project, SidebarOpen(project) ?? true, pinned); }

        private bool Remember(NativeProjectIdentity project, bool open, bool pinned)
        {
            if (project == null || !SafeDirectory()) return false;
            var key = Key(project);
            var destination = FileFor(key);
            var existing = new FileInfo(destination);
            if (existing.Exists && (existing.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (Directory.Exists(destination)) return false;
            var temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                var bytes = Encoding.UTF8.GetBytes("{\"version\":1,\"key\":\"" + key + "\",\"sidebarOpen\":" + (open ? "true" : "false") + ",\"sidebarPinned\":" + (pinned ? "true" : "false") + "}");
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
                return true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private bool SafeDirectory()
        {
            var directory = new DirectoryInfo(_directory);
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            var parent = directory.Parent;
            return parent == null || (parent.Attributes & FileAttributes.ReparsePoint) == 0;
        }
    }
}
