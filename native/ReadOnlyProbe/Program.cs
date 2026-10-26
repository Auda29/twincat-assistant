using System;
using System.Linq;
using System.Web.Script.Serialization;
using TwinCatAssistant.Native;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 1048576 };
            var running = RunningIde.Enumerate().ToList();
            if (args.Length == 1 && args[0] == "--list")
            {
                Console.WriteLine(json.Serialize(running.Select(ide => new { ide.ProcessId, ide.Registration, version = ide.Dte.Version, solution = ide.Dte.Solution.FullName })));
                return 0;
            }
            int pid;
            if (args.Length != 2 || (args[0] != "--pid" && args[0] != "--projects" && args[0] != "--editor") || !int.TryParse(args[1], out pid)) return 2;
            var selected = running.SingleOrDefault(ide => ide.ProcessId == pid);
            if (selected == null) throw new InvalidOperationException("The selected IDE is not accessible in this user/elevation context.");
            if (args[0] == "--editor")
            {
                var document = selected.Dte.ActiveDocument;
                var projectItem = document?.ProjectItem;
                var item = projectItem?.Object as TCatSysManagerLib.ITcSmTreeItem;
                object text = null;
                try { text = document?.Object("TextDocument"); } catch { }
                ActiveEditorSnapshot snapshot = null;
                string activeError = null;
                var activeReader = new NativeActiveEditorReader(selected.Dte, pid, null);
                try { snapshot = activeReader.Read(activeReader.Capture()); }
                catch (InvalidOperationException error) { activeError = error.Message; }
                Console.WriteLine(json.Serialize(new {
                    selected.ProcessId, hasDocument = document != null,
                    document = document?.FullName, project = projectItem?.ContainingProject?.FullName,
                    hasTreeItem = item != null, treePath = item?.PathName,
                    hasTextDocument = text is EnvDTE.TextDocument,
                    documentSaved = document?.Saved,
                    activeRead = snapshot == null ? null : new {
                        snapshot.Identity, snapshot.ObjectName, snapshot.SourceKind, snapshot.SourceNotice,
                        declarationCharacters = snapshot.Declaration.Length, implementationCharacters = snapshot.Implementation.Length,
                        snapshot.Dirty, snapshot.ReadMilliseconds
                    }, activeError
                }));
                return 0;
            }
            if (args[0] == "--projects")
            {
                var projects = new System.Collections.Generic.List<object>();
                foreach (EnvDTE.Project project in selected.Dte.Solution.Projects)
                {
                    var manager = project.Object as TCatSysManagerLib.ITcSysManager;
                    projects.Add(new { kind = project.Kind, extension = System.IO.Path.GetExtension(project.FullName),
                        hasAutomation = manager != null, plcRootChildren = manager?.LookupTreeItem("TIPC").ChildCount,
                        tree = manager == null ? null : InspectTree(manager.LookupTreeItem("TIPC"), 0) });
                }
                Console.WriteLine(json.Serialize(new { projectCount = selected.Dte.Solution.Projects.Count, projects }));
                return 0;
            }
            var reader = new NativeProjectReader(selected.Dte, selected.ProcessId);
            var context = reader.GetContext();
            // Metadata only: never put real project source in captured logs or Git.
            var target = context.Targets.FirstOrDefault();
            var read = target == null ? null : reader.ReadTarget(target.Id);
            Console.WriteLine(json.Serialize(new {
                context, correlatedSourceCount = context.Targets.Count(item => !string.IsNullOrEmpty(item.SourceFile)), read = read == null ? null : new {
                    read.Target, declarationCharacters = read.Declaration.Length,
                    implementationCharacters = read.Implementation.Length, read.Revision, read.Dirty, read.ReadOnly
                }
            }));
            return context.Targets.Count == 0 ? 3 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Read-only native probe failed: " + error.GetType().Name);
            return 2;
        }
    }

    private static object InspectTree(TCatSysManagerLib.ITcSmTreeItem item, int depth)
    {
        var implementation = item as TCatSysManagerLib.ITcPlcImplementation;
        var children = new System.Collections.Generic.List<object>();
        if (depth < 8)
        {
            var root = item as TCatSysManagerLib.ITcProjectRoot;
            if (root != null) children.Add(InspectTree(root.NestedProject, depth + 1));
            else foreach (TCatSysManagerLib.ITcSmTreeItem child in item)
            {
                if (children.Count >= 20) break;
                children.Add(InspectTree(child, depth + 1));
            }
        }
        return new { itemType = item.ItemType, itemSubType = item.ItemSubType,
            hasDeclaration = item is TCatSysManagerLib.ITcPlcDeclaration, language = implementation == null ? (int?)null : (int)implementation.Language,
            count = item.ChildCount, children };
    }
}
