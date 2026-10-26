using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using EnvDTE;
using TCatSysManagerLib;

namespace TwinCatAssistant.Native
{
    public sealed class NativeActiveDocument
    {
        public ActiveEditorIdentity Identity { get; internal set; }
        internal Document Document;
        internal ITcSmTreeItem Item;
    }

    // Independent of discovery/catalog limits. Never enumerates the project or
    // opens/saves a document; the active document's ProjectItem supplies the node.
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
            var moniker = document.FullName;
            if (expectedMoniker != null && !string.Equals(moniker, expectedMoniker, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active document frame and TwinCAT document do not match.");
            var projectItem = document.ProjectItem;
            var item = projectItem?.Object as ITcSmTreeItem;
            var project = projectItem?.ContainingProject?.FullName;
            if (item == null || string.IsNullOrEmpty(project))
                throw new InvalidOperationException("The active editor has no exact TwinCAT object binding.");
            var implementation = item as ITcPlcImplementation;
            if (!(item is ITcPlcDeclaration) || implementation == null
                || implementation.Language != IECLANGUAGETYPES.IECLANGUAGE_ST)
                throw new InvalidOperationException("The active object is not a supported Structured Text POU.");
            return new NativeActiveDocument {
                Identity = new ActiveEditorIdentity(_pid, solution, project, moniker, item.PathName),
                Document = document, Item = item
            };
        }

        public ActiveEditorSnapshot Read(NativeActiveDocument active)
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            var watch = Stopwatch.StartNew();
            var item = active.Item;
            if (!active.Identity.Equals(Capture().Identity)) throw new InvalidOperationException("The active editor changed.");
            var declaration = ((ITcPlcDeclaration)item).DeclarationText;
            var implementation = ((ITcPlcImplementation)item).ImplementationText;
            if (declaration == null || implementation == null || declaration.Length > TextLimit || implementation.Length > TextLimit)
                throw new InvalidOperationException("The active object exceeds the local source limit.");
            if (!Regex.IsMatch(declaration, @"\A\s*(?:(?:\(\*.*?\*\)|//[^\r\n]*)\s*)*(?:FUNCTION_BLOCK|PROGRAM|FUNCTION)\b",
                RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
                throw new InvalidOperationException("Methods, actions and other subobjects are not supported yet. Open the POU itself.");

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
                var liveXml = item is ITcXmlDocument live ? live.DocumentXml : item.ProduceXml(false);
                var livePou = ActivePouSource.Parse(liveXml);
                var file = PhysicalFile(active.Identity.DocumentMoniker);
                if (livePou != null && file != null && File.Exists(file)
                    && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 && new FileInfo(file).Length <= 1048576)
                {
                    string savedXml;
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream)) savedXml = ReadBounded(reader);
                    ActivePouSource.Correlate(evidence, livePou, ActivePouSource.Parse(savedXml), item.Name, declaration, implementation);
                }
            }
            catch { evidence.EvidenceFailed = true; }

            // TwinCAT's custom editor does not necessarily expose a TextDocument.
            // Automation Interface reads are snapshots, never proof of editor text.
            if (!active.Identity.Equals(Capture().Identity)
                || declaration != ((ITcPlcDeclaration)item).DeclarationText
                || implementation != ((ITcPlcImplementation)item).ImplementationText)
                throw new InvalidOperationException("The active object changed during the read. Retrying automatically.");
            var assessment = DirtyAssessment.Evaluate(evidence);
            return new ActiveEditorSnapshot {
                Identity = active.Identity, ObjectName = item.Name,
                Declaration = declaration, Implementation = implementation,
                SourceKind = "Automation Interface snapshot",
                SourceNotice = "Unsaved editor text is not verified. " + assessment.Reason,
                Dirty = assessment, ReadMilliseconds = watch.Elapsed.TotalMilliseconds
            };
        }

        internal static string PhysicalFile(string moniker)
        {
            // TwinCAT uses <file>.TcPOU;<POU/member> monikers for custom editors.
            var index = moniker.IndexOf(';');
            var path = index < 0 ? moniker : moniker.Substring(0, index);
            return Path.IsPathRooted(path) && path.EndsWith(".TcPOU", StringComparison.OrdinalIgnoreCase) ? path : null;
        }

        private static string ReadBounded(TextReader reader)
        {
            var buffer = new char[1048577];
            var used = 0;
            int count;
            while (used < buffer.Length && (count = reader.Read(buffer, used, buffer.Length - used)) > 0) used += count;
            if (used > 1048576) throw new IOException("Source XML exceeds the local limit.");
            return new string(buffer, 0, used);
        }

    }
}
