using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TwinCatAssistant.Native
{
    // Scoped to one frozen engineering session. It never opens a document and
    // never retains a buffer. A replaced RDT entry cannot reuse this capability.
    internal sealed class EngineeringEditorAccess
    {
        private readonly IVsRunningDocumentTable rdt;
        private readonly IVsMonitorSelection selection;
        private readonly DTE dte;
        private readonly EngineeringBinding binding;
        private readonly Func<bool> current;
        private readonly Func<Type, object> getService;
        private readonly Dictionary<string, Tuple<uint, IntPtr>> identities = new Dictionary<string, Tuple<uint, IntPtr>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> invalidated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal EngineeringEditorAccess(IVsRunningDocumentTable rdt, IVsMonitorSelection selection, DTE dte,
            EngineeringBinding binding, Func<bool> current, Func<Type, object> getService)
        { this.rdt = rdt; this.selection = selection; this.dte = dte; this.binding = binding; this.current = current; this.getService = getService; }

        internal object GetTextBuffer(string moniker)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (rdt == null || selection == null || dte == null || getService == null || current == null || !current()
                    || string.IsNullOrEmpty(moniker) || !Path.IsPathRooted(moniker) || moniker.IndexOf(';') >= 0
                    || !string.Equals(Path.GetFullPath(moniker), moniker, StringComparison.OrdinalIgnoreCase)
                    || !moniker.EndsWith(".TcPOU", StringComparison.OrdinalIgnoreCase) || invalidated.Contains(moniker)) return null;
                var before = Capture(moniker);
                if (before == null) return null;
                uint cookie, item; IVsHierarchy hierarchy; IntPtr data;
                if (ErrorHandler.Failed(rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, moniker, out hierarchy, out item, out data, out cookie)))
                { if (data != IntPtr.Zero) Marshal.Release(data); InvalidateKnown(moniker); return null; }
                if (data == IntPtr.Zero || cookie == 0) { if (data != IntPtr.Zero) Marshal.Release(data); InvalidateKnown(moniker); return null; }
                try
                {
                    var doc = Marshal.GetObjectForIUnknown(data);
                    var persist = doc as IVsPersistDocData;
                    int dirty;
                    if (persist == null || ErrorHandler.Failed(persist.IsDocDataDirty(out dirty))) return null;
                    Tuple<uint, IntPtr> known;
                    if (identities.TryGetValue(moniker, out known) && (known.Item1 != cookie || known.Item2 != data))
                    { invalidated.Add(moniker); return null; }
                    // Verify the cookie's moniker and docdata, including a second RDT lookup after MEF calls.
                    if (!MatchesRdt(moniker, cookie, data, hierarchy, item)) { InvalidateKnown(moniker); return null; }
                    var result = ConvertBuffer(doc);
                    if (result == null || !current()) return null;
                    if (!MatchesRdt(moniker, cookie, data, hierarchy, item)) { InvalidateKnown(moniker); return null; }
                    var after = Capture(moniker);
                    if (after == null || !SameCom(before.Document, after.Document)
                        || !SameCom(before.Item, after.Item) || !SameCom(before.Project, after.Project)) return null;
                    if (!identities.ContainsKey(moniker)) identities.Add(moniker, Tuple.Create(cookie, data));
                    return result;
                }
                finally { Marshal.Release(data); }
            }
            catch { return null; }
        }

        private void InvalidateKnown(string moniker) { if (identities.ContainsKey(moniker)) invalidated.Add(moniker); }

        private sealed class ActiveXmlDocument
        {
            internal Document Document;
            internal ProjectItem Item;
            internal Project Project;
        }

        private ActiveXmlDocument Capture(string moniker)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            object frameValue, frameMoniker;
            if (!current() || ErrorHandler.Failed(selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out frameValue))) return null;
            var frame = frameValue as IVsWindowFrame;
            if (frame == null || ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out frameMoniker))
                || !string.Equals(frameMoniker as string, moniker, StringComparison.OrdinalIgnoreCase)) return null;
            // An ordinary XML editor need not expose TwinCAT's custom ST
            // automation object. Bind its physical file and project metadata;
            // the provider separately proves Compile membership and XML target.
            var document = dte.ActiveDocument;
            var item = document?.ProjectItem;
            var project = item?.ContainingProject;
            if (document == null || item == null || project == null || item.FileCount != 1
                || !string.Equals(Path.GetFullPath(dte.Solution.FullName), binding.SolutionFile, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(project.FullName), binding.ProjectFile, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(document.FullName, moniker, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(item.FileNames[1], moniker, StringComparison.OrdinalIgnoreCase)) return null;
            return new ActiveXmlDocument { Document = document, Item = item, Project = project };
        }

        private bool MatchesRdt(string moniker, uint expectedCookie, IntPtr expectedData, IVsHierarchy expectedHierarchy, uint expectedItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            uint flags, readLocks, editLocks, item; string actual; IVsHierarchy hierarchy; IntPtr data;
            if (ErrorHandler.Failed(rdt.GetDocumentInfo(expectedCookie, out flags, out readLocks, out editLocks, out actual, out hierarchy, out item, out data)))
            { if (data != IntPtr.Zero) Marshal.Release(data); return false; }
            try
            {
                if (data == IntPtr.Zero || data != expectedData || item != expectedItem || !SameCom(hierarchy, expectedHierarchy)
                    || !string.Equals(actual, moniker, StringComparison.OrdinalIgnoreCase)) return false;
                uint foundItem, foundCookie; IVsHierarchy foundHierarchy; IntPtr foundData;
                var hr = rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, moniker, out foundHierarchy, out foundItem, out foundData, out foundCookie);
                try { return !ErrorHandler.Failed(hr) && foundData == expectedData && foundCookie == expectedCookie
                        && foundItem == expectedItem && SameCom(foundHierarchy, expectedHierarchy); }
                finally { if (foundData != IntPtr.Zero) Marshal.Release(foundData); }
            }
            finally { if (data != IntPtr.Zero) Marshal.Release(data); }
        }

        private object ConvertBuffer(object doc)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            // TwinCAT's synthetic searchable-block MEF adapter is not the XML
            // document. Never ask that docdata for an ordinary text-buffer
            // bridge; the separate scoped section accessor handles live reads.
            var customDocData = assemblies.Where(a => a.GetName().Name == "TwinCATPlcControl")
                .Select(a => a.GetType("Beckhoff.TwinCAT.VS.TextImage.PlcDocDataObject", false)).FirstOrDefault(t => t != null);
            if (customDocData != null && customDocData.IsInstanceOfType(doc)) return null;
            var component = Find(assemblies, "Microsoft.VisualStudio.ComponentModelHost", "Microsoft.VisualStudio.ComponentModelHost.SComponentModel");
            var componentModel = Find(assemblies, "Microsoft.VisualStudio.ComponentModelHost", "Microsoft.VisualStudio.ComponentModelHost.IComponentModel");
            var adapter = Find(assemblies, "Microsoft.VisualStudio.Editor", "Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService");
            var textBuffer = Find(assemblies, "Microsoft.VisualStudio.Text.Data", "Microsoft.VisualStudio.Text.ITextBuffer");
            var projection = Find(assemblies, "Microsoft.VisualStudio.Text.Data", "Microsoft.VisualStudio.Text.Projection.IProjectionBufferBase");
            if (component == null || componentModel == null || adapter == null || textBuffer == null || projection == null) return null;
            var documentMethod = adapter.GetMethods().SingleOrDefault(m => m.Name == "GetDocumentBuffer" && m.GetParameters().Length == 1);
            var textAdapter = documentMethod?.GetParameters()[0].ParameterType;
            if (textAdapter == null || !textAdapter.IsInterface || textAdapter.FullName != "Microsoft.VisualStudio.TextManager.Interop.IVsTextBuffer"
                || !textAdapter.IsInstanceOfType(doc)) return null;
            var model = getService(component);
            if (model == null || !componentModel.IsInstanceOfType(model)) return null;
            var serviceMethod = componentModel.GetMethods().SingleOrDefault(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            var factory = serviceMethod?.MakeGenericMethod(adapter).Invoke(model, null);
            if (factory == null || !adapter.IsInstanceOfType(factory)) return null;
            var dataMethod = adapter.GetMethod("GetDataBuffer", new[] { textAdapter });
            var documentBuffer = documentMethod?.Invoke(factory, new[] { doc });
            var dataBuffer = dataMethod?.Invoke(factory, new[] { doc });
            return documentBuffer != null && textBuffer.IsInstanceOfType(documentBuffer) && !projection.IsInstanceOfType(documentBuffer)
                && ReferenceEquals(documentBuffer, dataBuffer) ? documentBuffer : null;
        }
        private static Type Find(Assembly[] assemblies, string assembly, string name)
        { return assemblies.Where(a => a.GetName().Name == assembly).Select(a => a.GetType(name, false)).SingleOrDefault(t => t != null && t.IsInterface); }
        private static bool SameCom(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            if (!Marshal.IsComObject(a) || !Marshal.IsComObject(b)) return false;
            var x = Marshal.GetIUnknownForObject(a); var y = Marshal.GetIUnknownForObject(b);
            try { return x == y; } finally { Marshal.Release(x); Marshal.Release(y); }
        }
    }
}
