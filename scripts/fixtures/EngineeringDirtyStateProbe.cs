using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using TwinCatAssistant.Native;

internal static class EngineeringDirtyStateProbe
{
    private sealed class ProjectHierarchy : IVsHierarchy, IVsProject, IPersistFileFormat, IVsPersistHierarchyItem2
    {
        internal readonly string ProjectPath, SourcePath;
        internal Func<int> HResult = () => VSConstants.E_POINTER;
        internal int Dirty;
        internal uint SourceItem = 7;
        internal ProjectHierarchy(string path)
        {
            SourcePath = path;
            ProjectPath = Path.Combine(Path.GetDirectoryName(path), "Fixture.plcproj");
        }
        public int GetCurFile(out string file, out uint format) { file = ProjectPath; format = 0; return 0; }
        public int IsDirty(out int dirty) { dirty = Dirty; return 0; }
        public int IsItemDirty(uint item, IntPtr data, out int dirty) { dirty = Dirty; return HResult(); }
        public int GetCanonicalName(uint item, out string path) { path = item == VSConstants.VSITEMID_ROOT ? ProjectPath : SourcePath; return 0; }
        public int ParseCanonicalName(string path, out uint item) { item = SourceItem; return 0; }
        public int GetMkDocument(uint item, out string path) { path = SourcePath; return 0; }
        public int IsDocumentInProject(string path, out int found, VSDOCUMENTPRIORITY[] priority, out uint item)
        { found = path == SourcePath ? 1 : 0; item = SourceItem; return 0; }
    }

    private sealed class Solution : IVsSolution
    {
        internal ProjectHierarchy Hierarchy;
        public int GetProjectOfUniqueName(string name, out IVsHierarchy hierarchy)
        { hierarchy = name == Hierarchy.ProjectPath ? Hierarchy : null; return hierarchy == null ? VSConstants.S_FALSE : 0; }
        public int GetSolutionInfo(out string directory, out string file, out string options)
        { directory = Path.GetDirectoryName(Hierarchy.ProjectPath); file = Path.Combine(directory, "Fixture.sln"); options = null; return 0; }
        public int GetProperty(int id, out object value) { value = false; return 0; }
    }

    private sealed class RunningDocuments : IVsRunningDocumentTable, IVsPersistDocData
    {
        internal string Moniker;
        internal IVsHierarchy Hierarchy;
        internal int ScanCount;
        internal int AppearOnScan = 1;
        private sealed class Enumerator : IEnumRunningDocuments
        {
            internal bool HasEntry;
            public int Next(uint count, uint[] entries, out uint fetched)
            { fetched = HasEntry ? 1u : 0; entries[0] = 1; HasEntry = false; return fetched == 0 ? VSConstants.S_FALSE : 0; }
        }
        public int IsDocDataDirty(out int dirty) { dirty = 1; return 0; }
        public int GetRunningDocumentsEnum(out IEnumRunningDocuments entries)
        { entries = new Enumerator { HasEntry = ++ScanCount >= AppearOnScan && Moniker != null }; return 0; }
        public int GetDocumentInfo(uint cookie, out uint flags, out uint reads, out uint edits, out string moniker,
            out IVsHierarchy hierarchy, out uint item, out IntPtr data)
        {
            flags = reads = edits = 0; moniker = Moniker; hierarchy = Hierarchy; item = 7;
            data = Marshal.GetIUnknownForObject(this); return 0;
        }
        public int FindAndLockDocument(uint flags, string moniker, out IVsHierarchy hierarchy, out uint item, out IntPtr data, out uint cookie)
        {
            hierarchy = Hierarchy; item = 7; cookie = Moniker == moniker ? 1u : 0u;
            data = cookie == 1 ? Marshal.GetIUnknownForObject(this) : IntPtr.Zero; return 0;
        }
    }

    public static int Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EngineeringDirtyState-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(directory, "Plant;Line.TcIO");
        var member = source + ";Check";
        Directory.CreateDirectory(directory);
        try
        {
            var hierarchy = new ProjectHierarchy(source);
            var solution = new Solution { Hierarchy = hierarchy };
            var rdt = new RunningDocuments { Hierarchy = hierarchy };
            var dte = new DTE { Documents = new Documents() };
            int checks = 0;
            bool? State(DTE observedDte) { rdt.ScanCount = 0; return EngineeringDirtyState.GetDirty(rdt, observedDte, _ => solution, source, hierarchy.ProjectPath); }
            void Check(bool? expected, bool? actual, string name)
            {
                if (actual != expected) throw new InvalidOperationException(name + ": got " + (actual?.ToString() ?? "unknown"));
                checks++;
            }

            Check(false, State(dte), "Closed TcIO E_POINTER uses bounded DTE scan");
            Check(null, State(null), "Unavailable DTE fails closed");
            Check(null, State(new DTE()), "Unavailable Documents fails closed");
            dte.Documents.Items.Add(new Document { FullName = source, Saved = false });
            Check(true, State(dte), "Exact moniker with a semicolon is dirty");
            dte.Documents.Items[0].FullName = member;
            Check(true, State(dte), "Member moniker with a semicolon in the source name is dirty");
            File.WriteAllText(member, "");
            Check(false, State(dte), "Separate physical file is excluded from DTE scan");
            rdt.Moniker = member;
            Check(false, State(dte), "Separate physical file is excluded from RDT scan");
            File.Delete(member);
            Check(true, State(dte), "RDT matches member moniker with a semicolon in the source name");
            rdt.Moniker = source;
            Check(true, State(dte), "RDT matches exact moniker with a semicolon");
            rdt.AppearOnScan = 2;
            dte.Documents.Items.Clear();
            Check(null, State(dte), "RDT match appearing on recheck invalidates closed proof");
            rdt.AppearOnScan = 3;
            Check(null, State(dte), "RDT match appearing on final scan invalidates closed proof");
            rdt.Moniker = null;
            dte.Documents.ReadCount = () => 513;
            Check(null, State(dte), "Oversized DTE collection fails closed");
            dte.Documents.ReadCount = () => 512;
            dte.Documents.ReadItem = _ => new Document { FullName = source, Saved = true };
            Check(false, State(dte), "Complete DTE scan accepts 512 documents");
            dte.Documents.ReadItem = _ => throw new COMException();
            Check(null, State(dte), "COM failure in DTE scan fails closed");
            dte.Documents.ReadItem = _ => new Document { FullName = source, Saved = rdt.ScanCount == 1 };
            Check(true, State(dte), "Second DTE scan observes newly unsaved text");
            hierarchy.HResult = () => VSConstants.S_OK;
            Check(false, State(null), "S_OK clean preserves existing closed-source route");
            hierarchy.Dirty = 1;
            Check(true, State(null), "S_OK dirty preserves existing closed-source route");
            hierarchy.Dirty = 2;
            Check(null, State(dte), "Invalid S_OK dirty output fails closed");
            hierarchy.HResult = () => VSConstants.S_FALSE;
            Check(null, State(dte), "Other HRESULT never takes DTE fallback");
            hierarchy.HResult = () => { if (rdt.ScanCount == 2) hierarchy.SourceItem++; return VSConstants.E_POINTER; };
            Check(null, State(new DTE { Documents = new Documents() }), "Changed source item identity fails closed");
            Console.WriteLine("PASS engineering dirty-state observations: " + checks);
            return 0;
        }
        finally
        {
            if (File.Exists(member)) File.Delete(member);
            Directory.Delete(directory);
        }
    }
}

// Shape-only fixtures for the COM contracts exercised by EngineeringDirtyState.
namespace EnvDTE
{
    internal class DTE { public Documents Documents; public DteSolution Solution = new DteSolution(); }
    internal class DteSolution { public List<Project> Projects = new List<Project>(); }
    internal class Project { public string FullName { get; set; } public string UniqueName { get; set; } public List<ProjectItem> ProjectItems { get; set; } }
    internal class ProjectItem { public Project SubProject { get; set; } }
    internal class Document { public string FullName; public bool Saved; }
    internal class Documents
    {
        internal readonly List<Document> Items = new List<Document>();
        internal Func<int> ReadCount;
        internal Func<int, Document> ReadItem;
        public int Count { get { return ReadCount == null ? Items.Count : ReadCount(); } }
        public Document Item(int index) { return ReadItem == null ? Items[index - 1] : ReadItem(index); }
    }
}

namespace Microsoft.VisualStudio
{
    internal static class VSConstants
    {
        internal const int S_OK = 0, S_FALSE = 1, E_POINTER = unchecked((int)0x80004003);
        internal const uint VSITEMID_NIL = 0xffffffff, VSITEMID_ROOT = 0xfffffffe;
    }
    internal static class ErrorHandler { internal static bool Failed(int hr) { return hr < 0; } }
}

namespace Microsoft.VisualStudio.Shell
{
    internal static class ThreadHelper { internal static void ThrowIfNotOnUIThread() { } }
}

namespace Microsoft.VisualStudio.Shell.Interop
{
    internal interface SVsSolution { }
    internal interface IVsHierarchy { int GetCanonicalName(uint item, out string path); int ParseCanonicalName(string path, out uint item); }
    internal interface IVsProject { int IsDocumentInProject(string path, out int found, VSDOCUMENTPRIORITY[] priority, out uint item); int GetMkDocument(uint item, out string path); }
    internal interface IVsPersistHierarchyItem2 { int IsItemDirty(uint item, IntPtr data, out int dirty); }
    internal interface IPersistFileFormat { int GetCurFile(out string file, out uint format); int IsDirty(out int dirty); }
    internal interface IVsPersistDocData { int IsDocDataDirty(out int dirty); }
    internal enum VSDOCUMENTPRIORITY { None }
    internal enum _VSRDTFLAGS { RDT_NoLock }
    internal interface IVsSolution
    {
        int GetSolutionInfo(out string directory, out string file, out string options);
        int GetProperty(int id, out object value);
        int GetProjectOfUniqueName(string name, out IVsHierarchy hierarchy);
    }
    internal interface IEnumRunningDocuments { int Next(uint count, uint[] entries, out uint fetched); }
    internal interface IVsRunningDocumentTable
    {
        int GetRunningDocumentsEnum(out IEnumRunningDocuments entries);
        int GetDocumentInfo(uint cookie, out uint flags, out uint reads, out uint edits, out string moniker, out IVsHierarchy hierarchy, out uint item, out IntPtr data);
        int FindAndLockDocument(uint flags, string moniker, out IVsHierarchy hierarchy, out uint item, out IntPtr data, out uint cookie);
    }
}
