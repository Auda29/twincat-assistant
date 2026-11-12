using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TwinCatAssistant.Native
{
    // Engineering-only observation. Never opens, saves, or guesses that an
    // unregistered document is clean. All COM access stays on the IDE thread.
    internal static class EngineeringDirtyState
    {
        private const int MaxProjects = 512, MaxItems = 4096;
        private const int SolutionDirtyProperty = -8004; // VSPROPID_IsSolutionDirty

        internal static bool? GetDirty(IVsRunningDocumentTable rdt, DTE injectedDte, Func<Type, object> getService, string exactPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (rdt == null || getService == null || !Canonical(exactPath)) return null;
                bool absent; var observed = ReadRdt(rdt, exactPath, out absent);
                if (!absent) return observed;
                var extension = Path.GetExtension(exactPath);
                if (!extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".plcproj", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".tsproj", StringComparison.OrdinalIgnoreCase)) return null;
                var solution = getService(typeof(SVsSolution)) as IVsSolution;
                if (solution == null) return null;
                string before;
                if (!SolutionFile(solution, out before)) return null;
                if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    if (!SamePath(before, exactPath)) return null;
                    object value;
                    if (solution.GetProperty(SolutionDirtyProperty, out value) != VSConstants.S_OK || !(value is bool dirty)) return null;
                    string after;
                    bool absentAfter;
                    if (!SolutionFile(solution, out after) || !SamePath(before, after)) return null;
                    var later = ReadRdt(rdt, exactPath, out absentAfter);
                    return absentAfter ? dirty : later == true ? true : (bool?)null;
                }
                IVsHierarchy hierarchy; string uniqueName;
                if (!FindHierarchy(solution, injectedDte, exactPath, out hierarchy, out uniqueName)) return null;
                var state = ReadProject(hierarchy, exactPath);
                if (!state.HasValue) return null;
                string afterSolution; IVsHierarchy afterHierarchy;
                if (!SolutionFile(solution, out afterSolution) || !SamePath(before, afterSolution)
                    || solution.GetProjectOfUniqueName(uniqueName, out afterHierarchy) != VSConstants.S_OK
                    || !SameCom(hierarchy, afterHierarchy) || !ReadProject(hierarchy, exactPath).Equals(state)) return null;
                bool absentLater;
                var rdtLater = ReadRdt(rdt, exactPath, out absentLater);
                return absentLater ? state : rdtLater == true ? true : (bool?)null;
            }
            catch { return null; }
        }

        // 'absent' is set only by a successful lookup returning neither docdata
        // nor cookie. A failure or partial RDT entry is not evidence of absence.
        private static bool? ReadRdt(IVsRunningDocumentTable rdt, string path, out bool absent)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            absent = false;
            IVsHierarchy hierarchy; uint item, cookie; IntPtr data;
            var hr = rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, path, out hierarchy, out item, out data, out cookie);
            try
            {
                if (ErrorHandler.Failed(hr)) return null;
                if (data == IntPtr.Zero && cookie == 0) { absent = true; return null; }
                if (data == IntPtr.Zero || cookie == 0) return null;
                var document = Marshal.GetObjectForIUnknown(data) as IVsPersistDocData; int dirty;
                if (document == null || document.IsDocDataDirty(out dirty) != VSConstants.S_OK || (dirty != 0 && dirty != 1)) return null;
                uint flags, readLocks, editLocks, currentItem; string currentPath; IVsHierarchy currentHierarchy; IntPtr currentData;
                if (rdt.GetDocumentInfo(cookie, out flags, out readLocks, out editLocks, out currentPath, out currentHierarchy, out currentItem, out currentData) != VSConstants.S_OK)
                { if (currentData != IntPtr.Zero) Marshal.Release(currentData); return null; }
                try { return currentData == data && currentItem == item && SamePath(currentPath, path) && SameCom(hierarchy, currentHierarchy) ? dirty == 1 : (bool?)null; }
                finally { if (currentData != IntPtr.Zero) Marshal.Release(currentData); }
            }
            finally { if (data != IntPtr.Zero) Marshal.Release(data); }
        }

        private static bool FindHierarchy(IVsSolution solution, DTE dte, string path, out IVsHierarchy hierarchy, out string uniqueName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            hierarchy = null; uniqueName = null;
            IVsHierarchy direct;
            if (solution.GetProjectOfUniqueName(path, out direct) == VSConstants.S_OK && direct != null && ProjectIdentity(direct, path))
            { hierarchy = direct; uniqueName = path; return true; }
            if (dte == null || dte.Solution == null || dte.Solution.Projects == null) return false;
            var found = new List<Tuple<Project, string>>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int projects = 0, items = 0;
            foreach (Project project in dte.Solution.Projects)
                if (!Visit(project, path, found, seen, ref projects, ref items)) return false;
            if (found.Count != 1) return false;
            var match = found[0];
            if (!SamePath(match.Item1.FullName, path) || match.Item1.UniqueName != match.Item2 || string.IsNullOrEmpty(match.Item2)
                || solution.GetProjectOfUniqueName(match.Item2, out direct) != VSConstants.S_OK || direct == null || !ProjectIdentity(direct, path)) return false;
            hierarchy = direct; uniqueName = match.Item2; return true;
        }
        private static bool Visit(Project project, string path, List<Tuple<Project, string>> found, HashSet<string> seen, ref int projects, ref int items)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null || ++projects > MaxProjects) return false;
            var unique = project.UniqueName;
            if (string.IsNullOrEmpty(unique) || unique.Length > 2048 || !seen.Add(unique)) return false;
            var full = project.FullName;
            if (!string.IsNullOrEmpty(full) && SamePath(full, path)) found.Add(Tuple.Create(project, unique));
            var children = project.ProjectItems;
            if (children == null) return true;
            foreach (ProjectItem item in children)
            {
                if (item == null || ++items > MaxItems) return false;
                var nested = item.SubProject;
                if (nested != null && !Visit(nested, path, found, seen, ref projects, ref items)) return false;
            }
            return true;
        }
        private static bool? ReadProject(IVsHierarchy hierarchy, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!ProjectIdentity(hierarchy, path)) return null;
            var persist = hierarchy as IPersistFileFormat; int dirty;
            if (persist != null)
            {
                string file; uint format;
                if (persist.GetCurFile(out file, out format) != VSConstants.S_OK || !SamePath(file, path)
                    || persist.IsDirty(out dirty) != VSConstants.S_OK || (dirty != 0 && dirty != 1)) return null;
                string after; uint afterFormat;
                return persist.GetCurFile(out after, out afterFormat) == VSConstants.S_OK && SamePath(after, path) && format == afterFormat ? dirty == 1 : (bool?)null;
            }
            var item = hierarchy as IVsPersistHierarchyItem2;
            if (item == null || item.IsItemDirty(VSConstants.VSITEMID_ROOT, IntPtr.Zero, out dirty) != VSConstants.S_OK || (dirty != 0 && dirty != 1)) return null;
            return ProjectIdentity(hierarchy, path) ? dirty == 1 : (bool?)null;
        }
        private static bool ProjectIdentity(IVsHierarchy hierarchy, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (hierarchy == null) return false;
            var persist = hierarchy as IPersistFileFormat;
            if (persist != null)
            { string file; uint format; return persist.GetCurFile(out file, out format) == VSConstants.S_OK && SamePath(file, path); }
            string canonical;
            return hierarchy.GetCanonicalName(VSConstants.VSITEMID_ROOT, out canonical) == VSConstants.S_OK && SamePath(canonical, path);
        }
        private static bool SolutionFile(IVsSolution solution, out string file)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string directory, options;
            if (solution.GetSolutionInfo(out directory, out file, out options) != VSConstants.S_OK || !Canonical(file)) { file = null; return false; }
            return true;
        }
        private static bool Canonical(string value)
        { return !string.IsNullOrEmpty(value) && value.Length <= 32767 && Path.IsPathRooted(value) && string.Equals(Path.GetFullPath(value), value, StringComparison.OrdinalIgnoreCase); }
        private static bool SamePath(string left, string right)
        { return Canonical(left) && Canonical(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        private static bool SameCom(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (ReferenceEquals(a, b)) return true;
            if (!Marshal.IsComObject(a) || !Marshal.IsComObject(b)) return false;
            var first = Marshal.GetIUnknownForObject(a); var second = Marshal.GetIUnknownForObject(b);
            try { return first == second; } finally { Marshal.Release(first); Marshal.Release(second); }
        }
    }
}
