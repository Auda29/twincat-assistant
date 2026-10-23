using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using EnvDTE;
using TCatSysManagerLib;

namespace TwinCatAssistant.Native
{
    public sealed class NativeTarget
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string TreePath { get; set; }
        public string ProjectFile { get; set; }
        public string SourceFile { get; set; }
        public string SourceId { get; set; }
    }

    public sealed class NativeContext
    {
        public string SolutionFile { get; set; }
        public string IdeVersion { get; set; }
        public int IdeProcessId { get; set; }
        public string ReaderArchitecture { get; set; }
        public bool ReadOnly { get { return true; } }
        public List<NativeTarget> Targets { get; set; } = new List<NativeTarget>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class NativeRead
    {
        public NativeTarget Target { get; set; }
        public string Declaration { get; set; }
        public string Implementation { get; set; }
        public string Revision { get; set; }
        public DirtyAssessment Dirty { get; set; }
        public bool ReadOnly { get { return true; } }
    }

    // Called only on the IDE UI thread, or on a read-only external STA probe.
    // No Save, Open, Create, ConsumeXml, activation, ADS, or mutating COM calls.
    public sealed class NativeProjectReader
    {
        private readonly DTE _dte;
        private readonly Func<string, bool?> _documentDirty;
        private readonly int _processId;
        private string _solution;
        private readonly Dictionary<string, ITcSmTreeItem> _items = new Dictionary<string, ITcSmTreeItem>();
        private readonly Dictionary<string, NativeTarget> _targets = new Dictionary<string, NativeTarget>();

        public NativeProjectReader(DTE dte, int processId, Func<string, bool?> documentDirty = null)
        {
            _dte = dte;
            _processId = processId;
            _documentDirty = documentDirty;
        }

        public void Invalidate()
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            _items.Clear();
            _targets.Clear();
            _solution = null;
        }

        public NativeContext GetContext()
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            _items.Clear();
            _targets.Clear();
            _solution = _dte.Solution.FullName;
            var context = new NativeContext {
                SolutionFile = _solution, IdeVersion = _dte.Version, IdeProcessId = _processId,
                ReaderArchitecture = IntPtr.Size == 8 ? "amd64" : "x86"
            };
            if (string.IsNullOrWhiteSpace(_solution))
            {
                context.Warnings.Add("The selected IDE has no open solution.");
                return context;
            }
            foreach (var project in EnumerateProjects(_dte.Solution.Projects, 0))
            {
                try
                {
                    var manager = project.Object as ITcSysManager;
                    if (manager == null) continue;
                    var projectFile = project.FullName;
                    var sources = ReadSources(Path.GetDirectoryName(projectFile));
                    var visited = 0;
                    Visit(manager.LookupTreeItem("TIPC"), projectFile, sources, context, 0, ref visited);
                }
                catch (Exception)
                {
                    context.Warnings.Add("A TwinCAT project could not be enumerated; the IDE may be busy or the Automation Interface unavailable.");
                }
            }
            if (context.Targets.Count == 0)
                context.Warnings.Add("No readable Structured Text objects were exposed by the selected IDE's Automation Interface.");
            if (context.Targets.Count >= 200)
                context.Warnings.Add("The 200-target prototype limit was reached; this is not a complete project inventory.");
            return context;
        }

        public NativeRead ReadTarget(string id)
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            if (_dte.Solution.FullName != _solution || string.IsNullOrWhiteSpace(_solution))
            {
                _items.Clear(); _targets.Clear();
                throw new InvalidOperationException("Solution binding changed. Refresh the context first.");
            }
            ITcSmTreeItem item;
            NativeTarget target;
            if (!_items.TryGetValue(id, out item) || !_targets.TryGetValue(id, out target))
                throw new InvalidOperationException("Unknown target. Refresh the context first.");
            if (item.PathName != target.TreePath || item.Name != target.Name)
                throw new InvalidOperationException("Object identity changed. Refresh the context first.");
            var declaration = ((ITcPlcDeclaration)item).DeclarationText;
            var implementation = (ITcPlcImplementation)item;
            if (implementation.Language != IECLANGUAGETYPES.IECLANGUAGE_ST)
                throw new InvalidOperationException("The target is no longer Structured Text.");
            var code = implementation.ImplementationText;
            if (declaration.Length > 262144 || code.Length > 262144)
                throw new InvalidOperationException("The target exceeds the prototype read limit.");
            var evidence = new DirtyEvidence();
            try
            {
                if (!string.IsNullOrEmpty(target.SourceFile))
                {
                    var source = ReadSource(target.SourceFile);
                    if (source != null && source.Name == target.Name && source.Id == target.SourceId)
                    {
                        evidence.SavedSourceAvailable = true;
                        evidence.LiveMatchesSaved = Normalize(source.Declaration) == Normalize(declaration)
                            && Normalize(source.Implementation) == Normalize(code);
                    }
                    foreach (Document document in _dte.Documents)
                    {
                        if (SamePath(document.FullName, target.SourceFile))
                        {
                            var saved = document.Saved;
                            if (!saved || evidence.DocumentSaved == null) evidence.DocumentSaved = saved;
                        }
                    }
                    evidence.PersistedDocumentDirty = _documentDirty?.Invoke(target.SourceFile);
                }
            }
            catch { evidence.EvidenceFailed = true; }
            return new NativeRead {
                Target = target, Declaration = declaration, Implementation = code,
                Revision = Hash(_processId.ToString(), _solution, target.Id, declaration, code), Dirty = DirtyAssessment.Evaluate(evidence)
            };
        }

        private void Visit(ITcSmTreeItem item, string projectFile, List<SavedSource> sources,
            NativeContext context, int depth, ref int visited)
        {
            if (depth > 24 || ++visited > 2000 || context.Targets.Count >= 200) return;
            // ChildCount/Child only enumerate "main" children. A PLC project root
            // otherwise exposes the instance, not its nested source-code project.
            var projectRoot = item as ITcProjectRoot;
            if (projectRoot != null)
            {
                Visit(projectRoot.NestedProject, projectFile, sources, context, depth + 1, ref visited);
                return;
            }
            var declaration = item as ITcPlcDeclaration;
            var implementation = item as ITcPlcImplementation;
            if (declaration != null && implementation != null && implementation.Language == IECLANGUAGETYPES.IECLANGUAGE_ST)
            {
                var text = declaration.DeclarationText;
                if (text.Length <= 262144 && Regex.IsMatch(text, @"\A\s*(?:(?:\(\*.*?\*\)|//[^\r\n]*)\s*)*(?:FUNCTION_BLOCK|PROGRAM|FUNCTION)\b",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)))
                {
                    var name = item.Name;
                    // A name alone is not an object identity (multiple PLCs can reuse it).
                    // Only correlate saved source when live XML exposes the exact POU UUID.
                    string liveId = null;
                    try
                    {
                        var liveDocument = item as ITcXmlDocument;
                        var liveXml = liveDocument != null ? liveDocument.DocumentXml : item.ProduceXml(false);
                        if (liveXml.Length <= 1048576)
                            using (var xmlReader = XmlReader.Create(new StringReader(liveXml), new XmlReaderSettings {
                                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 }))
                            {
                                var pou = XDocument.Load(xmlReader).Descendants().FirstOrDefault(element => element.Name.LocalName == "POU" && (string)element.Attribute("Name") == name);
                                liveId = (string)pou?.Attribute("Id");
                            }
                    }
                    catch { /* Unknown identity must never become known-clean. */ }
                    var matching = sources.Where(source => source.Name == name && !string.IsNullOrEmpty(liveId)
                        && string.Equals(source.Id, liveId, StringComparison.OrdinalIgnoreCase)).ToList();
                    var source = matching.Count == 1 ? matching[0] : null;
                    var target = new NativeTarget {
                        Id = Hash(_processId.ToString(), _solution, projectFile, item.PathName), Name = name,
                        TreePath = item.PathName, ProjectFile = projectFile,
                        SourceFile = source?.File, SourceId = source?.Id
                    };
                    context.Targets.Add(target);
                    _items[target.Id] = item;
                    _targets[target.Id] = target;
                }
            }
            var count = 0;
            foreach (ITcSmTreeItem child in item)
            {
                if (++count > 500 || visited >= 2000 || context.Targets.Count >= 200) break;
                Visit(child, projectFile, sources, context, depth + 1, ref visited);
            }
        }

        private static IEnumerable<Project> EnumerateProjects(Projects projects, int depth)
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            if (depth > 12) yield break;
            foreach (Project project in projects)
                foreach (var nested in EnumerateProject(project, depth)) yield return nested;
        }

        private static IEnumerable<Project> EnumerateProject(Project project, int depth)
        {
#if HOST_X86 || HOST_AMD64
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
#endif
            if (depth > 12) yield break;
            {
                yield return project;
                if (project.ProjectItems == null) yield break;
                foreach (ProjectItem item in project.ProjectItems)
                    if (item.SubProject != null)
                        foreach (var nested in EnumerateProject(item.SubProject, depth + 1)) yield return nested;
            }
        }

        private static List<SavedSource> ReadSources(string root)
        {
            var result = new List<SavedSource>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return result;
            var directories = new Queue<string>();
            directories.Enqueue(root);
            var visited = 0;
            while (directories.Count > 0 && ++visited <= 500 && result.Count < 500)
            {
                var directory = directories.Dequeue();
                foreach (var project in Directory.EnumerateFiles(directory, "*.plcproj"))
                {
                    var xml = LoadXml(project);
                    foreach (var include in xml.Descendants().Where(e => e.Name.LocalName == "Compile").Select(e => (string)e.Attribute("Include")))
                    {
                        if (string.IsNullOrWhiteSpace(include) || !include.EndsWith(".TcPOU", StringComparison.OrdinalIgnoreCase)) continue;
                        var file = Path.GetFullPath(Path.Combine(directory, include));
                        var rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        if (!file.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) continue;
                        var source = ReadSource(file);
                        if (source != null && !result.Any(existing => SamePath(existing.File, file))) result.Add(source);
                        if (result.Count >= 500) break;
                    }
                }
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if (directories.Count + visited >= 500) break;
                    if (!Path.GetFileName(child).StartsWith(".") && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        directories.Enqueue(child);
                }
            }
            return result;
        }

        private static SavedSource ReadSource(string file)
        {
            var xml = LoadXml(file);
            var pou = xml.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "POU");
            if (pou == null) return null;
            return new SavedSource {
                File = file, Id = (string)pou.Attribute("Id"), Name = (string)pou.Attribute("Name"),
                Declaration = pou.Elements().FirstOrDefault(e => e.Name.LocalName == "Declaration")?.Value ?? "",
                Implementation = pou.Elements().FirstOrDefault(e => e.Name.LocalName == "Implementation")?.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "ST")?.Value ?? ""
            };
        }

        private static XDocument LoadXml(string file)
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 1048576)
                throw new IOException("Source XML is not a bounded regular file.");
            using (var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 }))
                return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        private static string Normalize(string text) { return text.Replace("\r\n", "\n"); }
        private static bool SamePath(string first, string second)
        {
            return !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second)
                && string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        private static string Hash(params string[] fields)
        {
            var canonical = string.Concat(fields.Select(field => (field ?? "").Length + ":" + field));
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
        }
        private sealed class SavedSource
        {
            internal string File, Id, Name, Declaration, Implementation;
        }
    }
}
