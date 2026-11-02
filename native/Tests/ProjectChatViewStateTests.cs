using System.Text;
using TwinCatAssistant.Native;

// Stored open state of the project chat bar: per stable PLC project and
// operator profile, robust against foreign, malformed and redirected files.
// Synthetic paths in a temporary directory only; no operator profile.
internal static class ProjectChatViewStateTests
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool result, string name) { if (!result) throw new InvalidOperationException("View state: " + name); count++; Console.WriteLine("PASS " + name); }
        var root = Path.Combine(Path.GetTempPath(), "twincat-view-state-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "profile");
        var threads = Path.Combine(profile, "threads");
        try
        {
            var lineA = new NativeProjectIdentity(10, "C:/Werk/Linie3/Linie3.sln", "C:/Werk/Linie3/Steuerung/Steuerung.plcproj");
            // Same project name in another solution and folder: a different project.
            var lineB = new NativeProjectIdentity(10, "C:/Werk/Linie4/Linie4.sln", "C:/Werk/Linie4/Steuerung/Steuerung.plcproj");
            var restartedA = new NativeProjectIdentity(4242, lineA.SolutionFile.Replace('/', '\\'), lineA.ProjectFile.ToUpperInvariant());
            var state = new ProjectChatViewState(threads, profile);
            Check(state.SidebarOpen(lineA) == null && !state.RememberSidebar(lineA, false) && !Directory.Exists(threads), "a missing binding directory is neither created nor repaired");
            Directory.CreateDirectory(threads);
            Check(state.SidebarOpen(lineA) == null, "no stored preference yields the caller's default");
            Check(state.RememberSidebar(lineA, false) && state.SidebarOpen(lineA) == false, "a closed bar is remembered");
            Check(state.SidebarOpen(lineB) == null, "a same-named PLC project in another solution keeps its own state");
            Check(new ProjectChatViewState(threads, profile).SidebarOpen(restartedA) == false, "the state survives an IDE restart, PID change and path spelling");
            Check(new ProjectChatViewState(threads, profile + "-other").SidebarOpen(lineA) == null, "another operator profile does not inherit the state");
            Check(state.RememberSidebar(lineB, true) && state.SidebarOpen(lineB) == true && state.SidebarOpen(lineA) == false, "two projects keep independent states");
            Check(state.RememberSidebar(lineA, true) && state.SidebarOpen(lineA) == true, "reopening replaces the stored state");
            Check(Directory.GetFiles(threads, "*.tmp").Length == 0 && Directory.GetFiles(threads, "*.view").Length == 2, "writes leave no temporary files");
            var fileA = Directory.GetFiles(threads, "project-" + ChatContextPolicy.ProjectSelectionKey(profile, lineA) + ".view").Single();
            var fileB = Directory.GetFiles(threads, "project-" + ChatContextPolicy.ProjectSelectionKey(profile, lineB) + ".view").Single();
            foreach (var (label, content) in new[] {
                ("empty file", ""),
                ("malformed JSON", "{\"version\":1,"),
                ("wrong version", File.ReadAllText(fileA).Replace("\"version\":1", "\"version\":2")),
                ("non-boolean value", File.ReadAllText(fileA).Replace("true", "\"true\"")),
                ("extra member", File.ReadAllText(fileA).Replace("}", ",\"sessionId\":\"ses_x\"}")),
                ("oversized file", "{\"version\":1,\"key\":\"" + new string('a', 2000) + "\",\"sidebarOpen\":true}") })
            {
                File.WriteAllText(fileA, content, new UTF8Encoding(false));
                Check(state.SidebarOpen(lineA) == null, "invalid stored state is ignored: " + label);
            }
            // A file copied from another project carries that project's key.
            File.Copy(fileB, fileA, true);
            Check(state.SidebarOpen(lineA) == null && state.SidebarOpen(lineB) == true, "a state file copied from another project is not adopted");
            Check(state.RememberSidebar(lineA, false) && state.SidebarOpen(lineA) == false, "an invalid file is replaced by the next explicit change");
            File.Delete(fileA);
            Directory.CreateDirectory(fileA);
            Check(state.SidebarOpen(lineA) == null && !state.RememberSidebar(lineA, true), "a directory in place of the state file is rejected");
            Directory.Delete(fileA);
            Check(state.SidebarOpen(null) == null && !state.RememberSidebar(null, true), "no project, no stored state");
            var redirected = Path.Combine(root, "redirected");
            Directory.CreateDirectory(redirected);
            var link = Path.Combine(root, "link-profile");
            Directory.CreateDirectory(link);
            var linkThreads = Path.Combine(link, "threads");
            try { Directory.CreateSymbolicLink(linkThreads, redirected); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // Without the symbolic-link privilege a junction is the same redirection.
                using (var junction = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + linkThreads + "\" \"" + redirected + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
                { junction.StandardOutput.ReadToEnd(); junction.WaitForExit(); }
                if (!Directory.Exists(linkThreads)) linkThreads = null;
            }
            if (linkThreads != null)
            {
                var linked = new ProjectChatViewState(linkThreads, link);
                Check(!linked.RememberSidebar(lineA, true) && linked.SidebarOpen(lineA) == null && Directory.GetFiles(redirected).Length == 0, "a redirected state directory is neither read nor written");
            }
            else Console.WriteLine("SKIP redirected directory (symbolic links unavailable for this account)");
        }
        finally
        {
            // Remove the link itself first, never what it points to.
            var link = Path.Combine(root, "link-profile", "threads");
            try { if (Directory.Exists(link)) Directory.Delete(link); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
        return count;
    }
}
