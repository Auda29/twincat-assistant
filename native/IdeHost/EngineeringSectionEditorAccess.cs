using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TwinCatAssistant.Native
{
    // One instance belongs to one frozen host binding. It never opens a view,
    // creates an editor, reads source or retains a previously returned view.
    internal sealed class EngineeringSectionEditorAccess : IDisposable, IVsRunningDocTableEvents
    {
        private readonly IVsRunningDocumentTable rdt;
        private readonly IVsMonitorSelection selection;
        private readonly DTE dte;
        private readonly EngineeringBinding binding;
        private readonly Func<bool> current;
        private int? projectHandle;
        private uint eventCookie;
        private bool disposed;
        private readonly Dictionary<string, Tuple<uint, IntPtr>> identities = new Dictionary<string, Tuple<uint, IntPtr>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> invalidated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal EngineeringSectionEditorAccess(IVsRunningDocumentTable rdt, IVsMonitorSelection selection, object injectedDte,
            EngineeringBinding binding, Func<bool> current, Func<Type, object> getService)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            this.rdt = rdt; this.selection = selection; dte = injectedDte as DTE; this.binding = binding; this.current = current;
            if (rdt != null && ErrorHandler.Failed(rdt.AdviseRunningDocTableEvents(this, out eventCookie))) eventCookie = 0;
        }

        internal object GetSectionEditor(string objectId, string section)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                string physical, qualified; Guid guid;
                if (!ParseObjectId(objectId, out physical, out guid, out qualified)
                    || (section != "declaration" && section != "implementation") || rdt == null || selection == null
                    || dte == null || binding == null || current == null || disposed || eventCookie == 0 || !current()) return null;
                var types = Types.TryGet();
                if (types == null) return null;
                var active = Capture(physical, qualified);
                if (active == null || invalidated.Contains(active.Moniker)) return null;
                uint cookie, item; IVsHierarchy hierarchy; IntPtr data;
                var hr = rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, active.Moniker, out hierarchy, out item, out data, out cookie);
                if (ErrorHandler.Failed(hr) || data == IntPtr.Zero || cookie == 0)
                { if (data != IntPtr.Zero) Marshal.Release(data); InvalidateKnown(active.Moniker); return null; }
                try
                {
                    Tuple<uint, IntPtr> known;
                    if (identities.TryGetValue(active.Moniker, out known) && (known.Item1 != cookie || known.Item2 != data))
                    { invalidated.Add(active.Moniker); return null; }
                    if (!identities.ContainsKey(active.Moniker)) identities.Add(active.Moniker, Tuple.Create(cookie, data));
                    if (!MatchesRdt(active.Moniker, cookie, data, hierarchy, item)) { InvalidateKnown(active.Moniker); return null; }
                    var raw = Marshal.GetObjectForIUnknown(data);
                    var persist = raw as IVsPersistDocData; int dirty;
                    if (persist == null || ErrorHandler.Failed(persist.IsDocDataDirty(out dirty))) return null;
                    var adapter = types.AdapterFromBuffer.Invoke(null, new[] { raw });
                    if (adapter == null || !types.DocData.IsInstanceOfType(adapter)) return null;
                    var handle = (int)types.DocData.GetProperty("ProjectHandle").GetValue(adapter, null);
                    if (handle <= 0 || (projectHandle.HasValue && projectHandle.Value != handle)
                        || (Guid)types.DocData.GetProperty("ObjectGuid").GetValue(adapter, null) != guid) return null;
                    var view = Resolve(types, active.Frame, adapter, guid, handle, section);
                    if (view == null || !current() || !MatchesRdt(active.Moniker, cookie, data, hierarchy, item)) return null;
                    var editor = types.EditorView.GetProperty("Editor").GetValue(view, null);
                    var document = TextDocument(types, view, editor);
                    if (editor == null || document == null) return null;
                    var after = Capture(physical, qualified);
                    if (after == null || !Same(active.Frame, after.Frame) || !Same(active.Document, after.Document)
                        || !Same(active.Item, after.Item) || !Same(active.Owner, after.Owner)
                        || !string.Equals(active.Moniker, after.Moniker, StringComparison.OrdinalIgnoreCase)) return null;
                    if (!Same(view, Resolve(types, after.Frame, adapter, guid, handle, section))
                        || !Same(editor, types.EditorView.GetProperty("Editor").GetValue(view, null))
                        || !Same(document, TextDocument(types, view, editor))) return null;
                    if ((Guid)types.DocData.GetProperty("ObjectGuid").GetValue(adapter, null) != guid
                        || (int)types.DocData.GetProperty("ProjectHandle").GetValue(adapter, null) != handle || !current()) return null;
                    if (!MatchesRdt(active.Moniker, cookie, data, hierarchy, item)) { InvalidateKnown(active.Moniker); return null; }
                    if (disposed || invalidated.Contains(active.Moniker)) return null;
                    projectHandle = handle;
                    return view;
                }
                finally { Marshal.Release(data); }
            }
            catch { return null; }
        }

        // Authoritative fallback only for the currently active TwinCAT source.
        // A physical path may map to an RDT moniker with a member suffix.
        internal bool? GetDirtyForPhysicalFile(string physical)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (rdt == null || selection == null || dte == null || binding == null || current == null || !current()
                    || disposed || eventCookie == 0 || string.IsNullOrEmpty(physical) || !Path.IsPathRooted(physical) || physical.IndexOf(';') >= 0
                    || !physical.EndsWith(".TcPOU", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetFullPath(physical), physical, StringComparison.OrdinalIgnoreCase)) return null;
                var active = Capture(physical, null);
                if (active == null || invalidated.Contains(active.Moniker)) return null;
                uint cookie, item; IVsHierarchy hierarchy; IntPtr data;
                var hr = rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, active.Moniker, out hierarchy, out item, out data, out cookie);
                if (ErrorHandler.Failed(hr) || data == IntPtr.Zero || cookie == 0)
                { if (data != IntPtr.Zero) Marshal.Release(data); InvalidateKnown(active.Moniker); return null; }
                try
                {
                    Tuple<uint, IntPtr> known;
                    if (identities.TryGetValue(active.Moniker, out known) && (known.Item1 != cookie || known.Item2 != data))
                    { invalidated.Add(active.Moniker); return null; }
                    if (!identities.ContainsKey(active.Moniker)) identities.Add(active.Moniker, Tuple.Create(cookie, data));
                    if (!MatchesRdt(active.Moniker, cookie, data, hierarchy, item)) { InvalidateKnown(active.Moniker); return null; }
                    var persist = Marshal.GetObjectForIUnknown(data) as IVsPersistDocData;
                    int first, second;
                    if (persist == null || ErrorHandler.Failed(persist.IsDocDataDirty(out first))) return null;
                    var after = Capture(physical, null);
                    if (after == null || !Same(active.Frame, after.Frame) || !Same(active.Document, after.Document)
                        || !Same(active.Item, after.Item) || !Same(active.Owner, after.Owner)
                        || !string.Equals(active.Moniker, after.Moniker, StringComparison.OrdinalIgnoreCase) || !current()
                        || ErrorHandler.Failed(persist.IsDocDataDirty(out second)) || (first != 0) != (second != 0)) return null;
                    if (!MatchesRdt(active.Moniker, cookie, data, hierarchy, item)) { InvalidateKnown(active.Moniker); return null; }
                    if (disposed || invalidated.Contains(active.Moniker)) return null;
                    return first != 0;
                }
                finally { Marshal.Release(data); }
            }
            catch { return null; }
        }

        private static bool ParseObjectId(string objectId, out string physical, out Guid guid, out string qualified)
        {
            physical = null; qualified = null; guid = Guid.Empty;
            if (objectId == null || objectId.Length > 1024) return false;
            var parts = objectId.Split('|');
            if (parts.Length != 3 || !Path.IsPathRooted(parts[0]) || parts[0].IndexOf(';') >= 0
                || !string.Equals(Path.GetFullPath(parts[0]), parts[0], StringComparison.OrdinalIgnoreCase)
                || !parts[0].EndsWith(".TcPOU", StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(parts[1], "D", out guid) || guid == Guid.Empty || parts[1] != guid.ToString("D")
                || !Regex.IsMatch(parts[2], @"\A[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return false;
            physical = parts[0]; qualified = parts[2]; return true;
        }

        private sealed class Active
        {
            internal IVsWindowFrame Frame; internal Document Document; internal object Item, Owner; internal string Moniker;
        }
        private Active Capture(string physical, string qualified)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            object frameValue, value;
            if (!current() || ErrorHandler.Failed(selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out frameValue))) return null;
            var frame = frameValue as IVsWindowFrame;
            if (frame == null || ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out value))) return null;
            var moniker = value as string;
            if (string.IsNullOrEmpty(moniker)) return null;
            var separator = moniker.IndexOf(';');
            var file = separator < 0 ? moniker : moniker.Substring(0, separator);
            if (!string.Equals(file, physical, StringComparison.OrdinalIgnoreCase)) return null;
            var active = new NativeActiveEditorReader(dte, binding.IdePid, null).Capture(moniker);
            if (active == null || !string.Equals(Path.GetFullPath(active.Identity.SolutionFile), binding.SolutionFile, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(active.Identity.ProjectFile), binding.ProjectFile, StringComparison.OrdinalIgnoreCase)
                || (qualified != null && !string.Equals(active.Owner.Name + (active.Members.Length == 0 ? "" : "." + string.Join(".", active.Members)), qualified, StringComparison.Ordinal))) return null;
            return new Active { Frame = frame, Document = active.Document, Item = active.Item, Owner = active.Owner, Moniker = moniker };
        }

        private object Resolve(Types types, IVsWindowFrame frame, object adapter, Guid guid, int handle, string section)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            object docView;
            if (ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out docView))
                || docView == null || !types.VsEditor.IsInstanceOfType(docView)) return null;
            if (!Same(types.VsEditor.GetProperty("DataObject").GetValue(docView, null), adapter)) return null;
            var factory = (Guid)types.VsEditor.GetProperty("EditorViewFactoryGuid").GetValue(docView, null);
            if (factory == Guid.Empty) return null;
            var rootView = types.VsEditor.GetProperty("EditorView").GetValue(docView, null);
            if (rootView == null || !types.EditorView.IsInstanceOfType(rootView)) return null;
            var rootEditor = types.EditorView.GetProperty("Editor").GetValue(rootView, null);
            var parent = types.PouEditor.IsInstanceOfType(rootEditor) ? types.PouEditor
                : types.MethodEditor.IsInstanceOfType(rootEditor) ? types.MethodEditor : null;
            if (parent == null || !ValidEditor(types, rootEditor, guid, handle)) return null;
            var slot = parent.GetProperty(section == "declaration" ? "InterfaceEditorView" : "ImplementationEditorView").GetValue(rootEditor, null);
            if (slot == null || !types.EditorView.IsInstanceOfType(slot)) return null;
            var result = FindConcreteView(types, slot, guid, handle, section == "declaration");
            if (result == null || !Same(rootView, types.VsEditor.GetProperty("EditorView").GetValue(docView, null))
                || !Same(adapter, types.VsEditor.GetProperty("DataObject").GetValue(docView, null))
                || factory != (Guid)types.VsEditor.GetProperty("EditorViewFactoryGuid").GetValue(docView, null)
                || !Same(slot, parent.GetProperty(section == "declaration" ? "InterfaceEditorView" : "ImplementationEditorView").GetValue(rootEditor, null))) return null;
            return result;
        }

        private static object FindConcreteView(Types types, object slot, Guid guid, int handle, bool declaration)
        {
            var editor = types.EditorView.GetProperty("Editor").GetValue(slot, null);
            if (Concrete(types, slot, editor, guid, handle, declaration)) return slot;
            // A wrapper may expose controls only within this exact selected slot.
            // Walk a bounded subtree and accept exactly one attested concrete view.
            if (!declaration || !ValidEditor(types, editor, guid, handle)) return null;
            var roots = new List<Control>();
            var control = types.View.GetProperty("Control").GetValue(slot, null) as Control;
            var panes = types.View.GetProperty("Panes").GetValue(slot, null) as Control[];
            if (control != null) roots.Add(control);
            if (panes != null) roots.AddRange(panes.Where(p => p != null));
            var queue = new Queue<Control>(roots); var seen = new HashSet<Control>(); object match = null;
            while (queue.Count != 0)
            {
                var node = queue.Dequeue();
                if (!seen.Add(node)) continue;
                if (seen.Count > 256) return null;
                if (types.EditorView.IsInstanceOfType(node))
                {
                    var child = types.EditorView.GetProperty("Editor").GetValue(node, null);
                    if (Concrete(types, node, child, guid, handle, declaration))
                    { if (match != null && !ReferenceEquals(match, node)) return null; match = node; }
                }
                foreach (Control child in node.Controls) queue.Enqueue(child);
            }
            return match;
        }
        private static bool Concrete(Types types, object view, object editor, Guid guid, int handle, bool declaration)
        {
            if (view == null || editor == null || !ValidEditor(types, editor, guid, handle)) return false;
            var concrete = types.ConcreteEditor.IsInstanceOfType(view) ? view
                : types.ConcreteEditor.IsInstanceOfType(editor) ? editor : null;
            if (concrete == null) return false;
            return (bool)types.ConcreteBase.GetMethod("IsDeclarationEditor", Type.EmptyTypes).Invoke(concrete, null) == declaration
                && TextDocument(types, view, editor) != null;
        }
        private static object TextDocument(Types types, object view, object editor)
        {
            var concrete = types.ConcreteEditor.IsInstanceOfType(view) ? view
                : types.ConcreteEditor.IsInstanceOfType(editor) ? editor : null;
            return concrete == null ? null : types.ConcreteBase.GetMethod("GetTextDocument", new[] { typeof(bool) }).Invoke(concrete, new object[] { false });
        }
        private static bool ValidEditor(Types types, object editor, Guid guid, int handle)
        {
            return editor != null && types.Editor.IsInstanceOfType(editor)
                && (Guid)types.Editor.GetProperty("ObjectGuid").GetValue(editor, null) == guid
                && (int)types.Editor.GetProperty("ProjectHandle").GetValue(editor, null) == handle;
        }

        private bool MatchesRdt(string moniker, uint cookie, IntPtr expected, IVsHierarchy hierarchy, uint item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            uint flags, reads, edits, foundItem; string actual; IVsHierarchy foundHierarchy; IntPtr data;
            var hr = rdt.GetDocumentInfo(cookie, out flags, out reads, out edits, out actual, out foundHierarchy, out foundItem, out data);
            try
            {
                if (ErrorHandler.Failed(hr) || data == IntPtr.Zero || data != expected || foundItem != item || !Same(hierarchy, foundHierarchy)
                    || !string.Equals(actual, moniker, StringComparison.OrdinalIgnoreCase)) return false;
                uint nextItem, nextCookie; IVsHierarchy nextHierarchy; IntPtr next;
                hr = rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, moniker, out nextHierarchy, out nextItem, out next, out nextCookie);
                try { return !ErrorHandler.Failed(hr) && next == expected && nextCookie == cookie && nextItem == item && Same(hierarchy, nextHierarchy); }
                finally { if (next != IntPtr.Zero) Marshal.Release(next); }
            }
            finally { if (data != IntPtr.Zero) Marshal.Release(data); }
        }
        public int OnBeforeLastDocumentUnlock(uint cookie, uint lockType, uint readLocks, uint editLocks)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var entry in identities)
                if (entry.Value.Item1 == cookie) invalidated.Add(entry.Key);
            return VSConstants.S_OK;
        }
        public int OnAfterFirstDocumentLock(uint cookie, uint lockType, uint readLocks, uint editLocks) { return VSConstants.S_OK; }
        public int OnAfterAttributeChange(uint cookie, uint attributes)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var identityChange = (uint)(__VSRDTATTRIB.RDTA_Hierarchy | __VSRDTATTRIB.RDTA_ItemID | __VSRDTATTRIB.RDTA_MkDocument);
            if ((attributes & identityChange) != 0)
                foreach (var entry in identities)
                    if (entry.Value.Item1 == cookie) invalidated.Add(entry.Key);
            return VSConstants.S_OK;
        }
        public int OnAfterSave(uint cookie) { return VSConstants.S_OK; }
        public int OnBeforeDocumentWindowShow(uint cookie, int firstShow, IVsWindowFrame frame) { return VSConstants.S_OK; }
        public int OnAfterDocumentWindowHide(uint cookie, IVsWindowFrame frame) { return VSConstants.S_OK; }
        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposed) return;
            disposed = true;
            if (eventCookie != 0) { try { rdt.UnadviseRunningDocTableEvents(eventCookie); } catch { } eventCookie = 0; }
            identities.Clear(); invalidated.Clear();
        }
        private void InvalidateKnown(string moniker) { if (identities.ContainsKey(moniker)) invalidated.Add(moniker); }
        private static bool Same(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            if (!Marshal.IsComObject(a) || !Marshal.IsComObject(b)) return false;
            var x = Marshal.GetIUnknownForObject(a); var y = Marshal.GetIUnknownForObject(b);
            try { return x == y; } finally { Marshal.Release(x); Marshal.Release(y); }
        }
        private sealed class Types
        {
            internal Type DocData, VsEditor, EditorView, View, Editor, PouEditor, MethodEditor, ConcreteBase, ConcreteEditor;
            internal MethodInfo AdapterFromBuffer;
            internal static Types TryGet()
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var tc = assemblies.SingleOrDefault(a => a.GetName().Name == "TwinCATPlcControl" && a.GetName().Version == new Version(3, 1, 4024, 0));
                var core = assemblies.SingleOrDefault(a => a.GetName().Name == "Core");
                var pou = assemblies.SingleOrDefault(a => a.GetName().Name == "POUEditor");
                var method = assemblies.SingleOrDefault(a => a.GetName().Name == "MethodEditor");
                var iec = assemblies.SingleOrDefault(a => a.GetName().Name == "IECTextEditor.plugin");
                if (tc == null || core == null || pou == null || method == null || iec == null) return null;
                var t = new Types {
                    DocData = tc.GetType("Beckhoff.TwinCAT.VS.TextImage.PlcDocDataObject", false), VsEditor = tc.GetType("Beckhoff.TwinCAT.VS.VSEditor", false),
                    EditorView = core.GetType("_3S.CoDeSys.Core.Views.IEditorView", false), View = core.GetType("_3S.CoDeSys.Core.Views.IView", false),
                    Editor = core.GetType("_3S.CoDeSys.Core.Objects.IEditor", false), PouEditor = pou.GetType("_3S.CoDeSys.POUEditor.IPOUEditor", false),
                    MethodEditor = method.GetType("_3S.CoDeSys.MethodEditor.IMethodEditor", false),
                    ConcreteBase = iec.GetType("_3S.CoDeSys.IECTextEditor.AbstractIECTextEditorBase", false),
                    ConcreteEditor = iec.GetType("_3S.CoDeSys.IECTextEditor.IECTextEditor", false)
                };
                if (t.DocData == null || t.VsEditor == null || t.EditorView == null || t.View == null || t.Editor == null
                    || t.PouEditor == null || t.MethodEditor == null || t.ConcreteBase == null || t.ConcreteEditor == null
                    || !t.EditorView.IsInterface || !t.View.IsInterface || !t.Editor.IsInterface || !t.PouEditor.IsInterface || !t.MethodEditor.IsInterface) return null;
                t.AdapterFromBuffer = t.DocData.GetMethod("GetAdapterFromVsTextBuffer", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(object) }, null);
                return t.AdapterFromBuffer == null ? null : t;
            }
        }
    }
}
