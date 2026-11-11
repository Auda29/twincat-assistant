using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using EnvDTE;
using TCatSysManagerLib;

namespace TwinCatAssistant.Native
{
    public sealed class NativeActiveDocument
    {
        public ActiveEditorIdentity Identity { get; internal set; }
        internal Document Document;
        internal ITcSmTreeItem Item;
        internal ITcSmTreeItem Owner;
        internal string[] Members;
    }

    // Active source reads use the document's exact ProjectItem, independently
    // of catalog limits, without opening/saving documents. Project-only capture
    // can fall back to bounded project metadata; it never inventories POU source.
    public sealed class NativeActiveEditorReader
    {
        private readonly DTE _dte;
        private readonly int _pid;
        private readonly Func<string, bool?> _dirty;
        private const int TextLimit = 262144;

        public NativeActiveEditorReader(DTE dte, int pid, Func<string, bool?> dirty)
        {
            _dte = dte;
            _pid = pid;
            _dirty = dirty;
        }

        public NativeActiveDocument Capture(string expectedMoniker = null)
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            var solution = _dte.Solution.FullName;
            if (string.IsNullOrWhiteSpace(solution)) throw new InvalidOperationException("This IDE has no open solution.");
            var document = _dte.ActiveDocument;
            if (document == null) throw new InvalidOperationException("Open a Structured Text POU in this IDE.");
            var documentMoniker = document.FullName;
            var moniker = expectedMoniker ?? documentMoniker;
            var file = PhysicalFile(moniker);
            if (file == null || !string.Equals(file, PhysicalFile(documentMoniker), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active document frame and TwinCAT document do not match.");
            var projectItem = document.ProjectItem;
            var item = projectItem?.Object as ITcSmTreeItem;
            var project = projectItem?.ContainingProject?.FullName;
            if (item == null || string.IsNullOrEmpty(project))
                throw new InvalidOperationException("The active editor has no exact TwinCAT object binding.");
            var owner = OwnerOf(item);
            var members = ActiveEditorMemberPath.Parse(moniker, owner.Name);
            var documentMembers = ActiveEditorMemberPath.Parse(documentMoniker, owner.Name);
            if (documentMoniker.IndexOf(';') >= 0 && moniker.IndexOf(';') >= 0
                && !string.Equals(string.Join("^", members), string.Join("^", documentMembers), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The frame and document identify different POU members.");
            if (moniker.IndexOf(';') >= 0)
            {
                var path = string.Join("^", members);
                item = members.Length == 0 ? owner : owner.LookupChild(path);
                if (item == null || !string.Equals(item.PathName, owner.PathName + (path.Length == 0 ? "" : "^" + path), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The active POU member could not be resolved exactly.");
            }
            else members = RelativeMembers(owner, item);
            // Validate only this node (and a property's two accessors), never
            // scan the PLC catalog or fall back to the parent implementation.
            var kind = ObjectKind(item);
            var implementation = item as ITcPlcImplementation;
            if (kind != "Property" && (implementation == null || implementation.Language != IECLANGUAGETYPES.IECLANGUAGE_ST))
                throw new InvalidOperationException("The active implementation is not readable Structured Text.");
            return new NativeActiveDocument {
                Identity = new ActiveEditorIdentity(_pid, solution, project, moniker, item.PathName),
                Document = document, Item = item, Owner = owner, Members = members
            };
        }

        public NativeProjectIdentity CaptureProject()
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            var solution = _dte.Solution.FullName;
            if (string.IsNullOrWhiteSpace(solution)) throw new InvalidOperationException("This IDE has no open solution.");
            var file = PlcProjectFile(_dte.ActiveDocument?.ProjectItem?.ContainingProject);
            if (file != null) return new NativeProjectIdentity(_pid, solution, file);
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SelectedItem item in _dte.SelectedItems)
            {
                file = PlcProjectFile(item.ProjectItem?.ContainingProject) ?? PlcProjectFile(item.Project);
                if (file != null) selected.Add(file);
            }
            if (selected.Count == 0)
            {
                var budget = 256;
                foreach (Project project in _dte.Solution.Projects) CollectPlcProjects(project, selected, 0, ref budget);
            }
            if (selected.Count != 1) throw new InvalidOperationException("Select one PLC project in this IDE. No unambiguous project is bound.");
            foreach (var project in selected) return new NativeProjectIdentity(_pid, solution, project);
            throw new InvalidOperationException("No PLC project is bound.");
        }

        private static string PlcProjectFile(Project project)
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            var file = project?.FullName;
            return !string.IsNullOrWhiteSpace(file) && Path.IsPathRooted(file)
                && file.EndsWith(".plcproj", StringComparison.OrdinalIgnoreCase) ? file : null;
        }
        private static void CollectPlcProjects(Project project, HashSet<string> files, int depth, ref int budget)
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            if (--budget < 0 || depth > 8) throw new InvalidOperationException("Select a PLC project; automatic project discovery is incomplete.");
            var file = PlcProjectFile(project);
            if (file != null) { files.Add(file); return; } // Never scan POU items.
            if (project.ProjectItems == null) return;
            foreach (ProjectItem item in project.ProjectItems)
            {
                if (--budget < 0) throw new InvalidOperationException("Select a PLC project; automatic project discovery is incomplete.");
                if (item.SubProject != null) CollectPlcProjects(item.SubProject, files, depth + 1, ref budget);
            }
        }

        public ActiveEditorSnapshot Read(NativeActiveDocument active)
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            var watch = Stopwatch.StartNew();
            var item = active.Item;
            if (!active.Identity.Equals(Capture(active.Identity.DocumentMoniker).Identity)) throw new InvalidOperationException("The active editor changed.");
            string declaration, implementation;
            ReadText(item, out declaration, out implementation);
            if (declaration == null || implementation == null || declaration.Length > TextLimit || implementation.Length > TextLimit)
                throw new InvalidOperationException("The active object exceeds the local source limit.");
            var evidence = new DirtyEvidence();
            // Use the complete native moniker, including any subdocument suffix,
            // for RDT evidence. A filesystem path alone may identify another view.
            try { evidence.DocumentSaved = active.Document.Saved; }
            catch { evidence.EvidenceFailed = true; }
            try { evidence.PersistedDocumentDirty = _dirty?.Invoke(active.Identity.DocumentMoniker); }
            catch { evidence.EvidenceFailed = true; }
            try
            {
                // A GUID/name equality with the exact live node is mandatory.
                // Text equality or a matching tab title alone proves no identity.
                var liveXml = active.Owner is ITcXmlDocument live ? live.DocumentXml : active.Owner.ProduceXml(false);
                var livePou = ActivePouSource.Parse(liveXml, active.Members);
                var file = PhysicalFile(active.Identity.DocumentMoniker);
                if (livePou != null && file != null && File.Exists(file)
                    && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 && new FileInfo(file).Length <= 1048576)
                {
                    string savedXml;
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream)) savedXml = ReadBounded(reader);
                    ActivePouSource.Correlate(evidence, livePou, ActivePouSource.Parse(savedXml, active.Members), item.Name, declaration, implementation);
                }
            }
            catch { evidence.EvidenceFailed = true; }

            // TwinCAT's custom editor does not necessarily expose a TextDocument.
            // Automation Interface reads are snapshots, never proof of editor text.
            string currentDeclaration, currentImplementation;
            ReadText(item, out currentDeclaration, out currentImplementation);
            if (!active.Identity.Equals(Capture(active.Identity.DocumentMoniker).Identity)
                || declaration != currentDeclaration || implementation != currentImplementation)
                throw new InvalidOperationException("The active object changed during the read. Retrying automatically.");
            var assessment = DirtyAssessment.Evaluate(evidence);
            return new ActiveEditorSnapshot {
                Identity = active.Identity, ObjectName = active.Owner.Name + (active.Members.Length == 0 ? "" : "." + string.Join(".", active.Members)),
                OwnerName = active.Owner.Name, MemberPath = string.Join(".", active.Members), ObjectKind = ObjectKind(item),
                Declaration = declaration, Implementation = implementation,
                SourceKind = "Automation Interface snapshot",
                SourceNotice = "Unsaved editor text is not verified. " + assessment.Reason,
                Dirty = assessment, ReadMilliseconds = watch.Elapsed.TotalMilliseconds
            };
        }

        private static string ObjectKind(ITcSmTreeItem item)
        {
            switch ((TREEITEMTYPES)item.ItemType)
            {
                case TREEITEMTYPES.TREEITEMTYPE_PLCPOUPROG:
                case TREEITEMTYPES.TREEITEMTYPE_PLCPOUFUNC:
                case TREEITEMTYPES.TREEITEMTYPE_PLCPOUFB: return "POU";
                case TREEITEMTYPES.TREEITEMTYPE_PLCACTION: return "Action";
                case TREEITEMTYPES.TREEITEMTYPE_PLCMETHOD: return "Method";
                case TREEITEMTYPES.TREEITEMTYPE_PLCPROP: return "Property";
                case TREEITEMTYPES.TREEITEMTYPE_PLCPROPGET: return "Get";
                case TREEITEMTYPES.TREEITEMTYPE_PLCPROPSET: return "Set";
                default: throw new InvalidOperationException("The active object is not a supported POU, action, method or property.");
            }
        }

        private static ITcSmTreeItem OwnerOf(ITcSmTreeItem item)
        {
            for (var depth = 0; item != null && depth < 4; depth++, item = item.Parent)
                if (ObjectKind(item) == "POU") return item;
            throw new InvalidOperationException("The active member has no exact owning POU.");
        }

        private static string[] RelativeMembers(ITcSmTreeItem owner, ITcSmTreeItem item)
        {
            if (item.PathName == owner.PathName) return new string[0];
            var prefix = owner.PathName + "^";
            if (!item.PathName.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("The active member belongs to another POU.");
            return ActiveEditorMemberPath.Parse(owner.Name + ";" + owner.Name + "." + item.PathName.Substring(prefix.Length).Replace('^', '.'), owner.Name);
        }

        private static void ReadText(ITcSmTreeItem item, out string declaration, out string implementation)
        {
            var kind = ObjectKind(item);
            var declared = item as ITcPlcDeclaration;
            if (declared == null && (kind == "POU" || kind == "Method" || kind == "Property"))
                throw new InvalidOperationException("The active object has no readable declaration.");
            declaration = declared?.DeclarationText ?? "";
            if (kind == "Property")
            {
                string getDeclaration = null, getBody = null, setDeclaration = null, setBody = null;
                var count = 0;
                foreach (ITcSmTreeItem child in item)
                {
                    if (++count > 16) throw new InvalidOperationException("The property accessor list is incomplete.");
                    var childKind = ObjectKind(child);
                    if (childKind == "Get" && getBody == null) ReadText(child, out getDeclaration, out getBody);
                    else if (childKind == "Set" && setBody == null) ReadText(child, out setDeclaration, out setBody);
                    else throw new InvalidOperationException("The property accessor identity is ambiguous.");
                }
                implementation = ActivePouSource.PropertyImplementation(getDeclaration, getBody, setDeclaration, setBody);
            }
            else
            {
                var body = item as ITcPlcImplementation;
                if (body == null || body.Language != IECLANGUAGETYPES.IECLANGUAGE_ST)
                    throw new InvalidOperationException("The active implementation is not readable Structured Text.");
                implementation = body.ImplementationText;
            }
            if (declaration == null || implementation == null || declaration.Length > TextLimit || implementation.Length > TextLimit)
                throw new InvalidOperationException("The active object exceeds the local source limit.");
        }

        internal static string PhysicalFile(string moniker)
        {
            if (string.IsNullOrWhiteSpace(moniker)) return null;
            // TwinCAT uses <file>.TcPOU;<POU/member> monikers for custom editors.
            var index = moniker.IndexOf(';');
            var path = index < 0 ? moniker : moniker.Substring(0, index);
            return Path.IsPathRooted(path) && path.EndsWith(".TcPOU", StringComparison.OrdinalIgnoreCase) ? path : null;
        }

        private static string ReadBounded(TextReader reader)
        {
            var buffer = new char[4096];
            var text = new System.Text.StringBuilder();
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (text.Length + count > 1048576) throw new IOException("Source XML exceeds the local limit.");
                text.Append(buffer, 0, count);
            }
            return text.ToString();
        }

    }
}
