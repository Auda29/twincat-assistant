using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System.Windows.Threading;

namespace TwinCatAssistant.Native
{
    // No bridge or model tool reaches this service. Start is a native user action.
    internal sealed class ManualProjectBuild : IDisposable
    {
        private readonly DTE dte;
        private readonly Func<NativeProjectIdentity> current;
        private readonly Func<string, bool?> dirty;
        private readonly Action<string, string, List<BuildDiagnostic>> render;
        private readonly DispatcherTimer timer;
        private Events events;
        private BuildEvents builds;
        private SolutionEvents solutions;
        private NativeProjectIdentity identity;
        private Project project;
        private string configuration, platform, uniqueName, before;
        private bool running, disposed, invalid;
        private BuildCompletionEvidence evidence;
        private DateTime deadline;
        internal bool Running { get { return running; } }
        internal NativeProjectIdentity Identity { get { return identity; } }
        internal string Output { get; private set; }
        private readonly Dictionary<string, Tuple<long, long>> fileVersions = new Dictionary<string, Tuple<long, long>>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, ProjectItem> Files = new Dictionary<string, ProjectItem>(StringComparer.OrdinalIgnoreCase);
        internal ManualProjectBuild(DTE dte, Func<NativeProjectIdentity> current, Func<string, bool?> dirty,
            Action<string, string, List<BuildDiagnostic>> render)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            this.dte = dte; this.current = current; this.dirty = dirty; this.render = render;
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += Tick;
            try
            {
                events = dte.Events; builds = events.BuildEvents; solutions = events.SolutionEvents;
                builds.OnBuildBegin += BeginBuild;
                builds.OnBuildProjConfigBegin += BeginProject;
                builds.OnBuildProjConfigDone += EndProject;
                builds.OnBuildDone += EndBuild;
                solutions.BeforeClosing += Invalidate;
                solutions.AfterClosing += Closed;
                solutions.ProjectRemoved += Removed;
            }
            catch { Dispose(); throw; }
        }
        internal void Start(NativeProjectIdentity bound)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposed || running || dte.Solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress) throw new InvalidOperationException();
            identity = null; Output = null; Files.Clear(); fileVersions.Clear();
            if (bound == null || !bound.Equals(current()) || !HostTargets.Assess(HostIdentityReader.Current).AlphaTarget) throw new InvalidOperationException();
            var budget = 512;
            var matches = new List<Project>();
            foreach (Project root in dte.Solution.Projects) Find(root, bound.ProjectFile, matches, ref budget, 0);
            if (matches.Count != 1 || !dte.Solution.Saved || !matches[0].Saved) throw new InvalidOperationException();
            project = matches[0];
            // No implicit SaveAll. Unknown RDT state is a refusal, including unrelated
            // documents, since IDE build preferences can save outside this project.
            if (dte.Documents.Count > 256) throw new InvalidOperationException();
            foreach (Document doc in dte.Documents)
                if (!doc.Saved || dirty == null || dirty(doc.FullName) != false) throw new InvalidOperationException();
            BuildProjectPolicy.Validate(bound.ProjectFile);
            var active = dte.Solution.SolutionBuild.ActiveConfiguration;
            configuration = active.Name; platform = project.ConfigurationManager.ActiveConfiguration.PlatformName;
            if ((configuration != "Debug" && configuration != "Release") ||
                (platform != "TwinCAT RT (x86)" && platform != "TwinCAT RT (x64)")) throw new InvalidOperationException();
            uniqueName = project.UniqueName;
            var contexts = active.SolutionContexts.Cast<SolutionContext>().Where(c => { ThreadHelper.ThrowIfNotOnUIThread(); return string.Equals(c.ProjectName, uniqueName, StringComparison.OrdinalIgnoreCase); }).ToArray();
            if (contexts.Length != 1 || !contexts[0].ShouldBuild || contexts[0].ConfigurationName != configuration || contexts[0].PlatformName != platform) throw new InvalidOperationException();
            budget = 4096; Collect(project.ProjectItems, ref budget, 0);
            before = ReadOutput(); // absent/oversized output means no trustworthy capture
            identity = bound; evidence = new BuildCompletionEvidence(); invalid = false;
            if (!bound.Equals(current())) throw new InvalidOperationException();
            running = true; deadline = DateTime.UtcNow.AddMinutes(2); timer.Start();
            render("BuildRunning", configuration + " | " + platform, null);
            try { dte.Solution.SolutionBuild.BuildProject(configuration + "|" + platform, uniqueName, false); }
            catch { Finish("BuildUnknown", null); throw; }
        }
        private static void Find(Project p, string file, List<Project> matches, ref int budget, int depth)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (--budget < 0 || depth > 8) throw new InvalidOperationException();
            if (!p.Saved) throw new InvalidOperationException();
            if (string.Equals(p.FullName, file, StringComparison.OrdinalIgnoreCase)) matches.Add(p);
            if (p.ProjectItems == null || p.FullName.EndsWith(".plcproj", StringComparison.OrdinalIgnoreCase)) return;
            foreach (ProjectItem item in p.ProjectItems)
            { if (--budget < 0) throw new InvalidOperationException(); if (item.SubProject != null) Find(item.SubProject, file, matches, ref budget, depth + 1); }
        }
        private void Collect(ProjectItems items, ref int budget, int depth)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (items == null) return;
            if (depth > 16) throw new InvalidOperationException();
            foreach (ProjectItem item in items)
            {
                if (--budget < 0) throw new InvalidOperationException();
                var fileCount = item.FileCount;
                if (fileCount < 0 || fileCount > 16 || (budget -= fileCount) < 0) throw new InvalidOperationException();
                for (short i = 1; i <= fileCount; i++)
                {
                    var file = item.FileNames[i];
                    if (!Path.IsPathRooted(file) || file.IndexOf(';') >= 0) continue;
                    if (Files.ContainsKey(file)) throw new InvalidOperationException();
                    Files.Add(file, item);
                    var info = new FileInfo(file);
                    if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0)
                        fileVersions[file] = Tuple.Create(info.Length, info.LastWriteTimeUtc.Ticks);
                }
                Collect(item.ProjectItems, ref budget, depth + 1);
            }
        }
        private string ReadOutput()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            // Canonical Build pane GUID, independent of IDE UI language.
            var pane = ((OutputWindow)dte.Windows.Item(EnvDTE.Constants.vsWindowKindOutput).Object).OutputWindowPanes.Item("{1BD8A850-02D1-11D1-BEE7-00A0C913D1F8}");
            var doc = pane.TextDocument;
            if (doc.EndPoint.AbsoluteCharOffset > 524288) return null;
            return doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
        }
        private bool Same(string name, string config, string target)
        { return string.Equals(name, uniqueName, StringComparison.OrdinalIgnoreCase) && config == configuration && target == platform; }
        internal void ObserveProject(NativeProjectIdentity observed)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (identity != null && !identity.Equals(observed)) Invalidate();
        }
        private void BeginBuild(vsBuildScope scope, vsBuildAction action)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!running) { if (identity != null) Invalidate(); return; }
            if (action != vsBuildAction.vsBuildActionBuild || scope != vsBuildScope.vsBuildScopeProject) invalid = true;
        }
        internal bool TryItem(string moniker, out ProjectItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            item = null;
            var separator = moniker.IndexOf(';');
            var physical = separator < 0 ? moniker : moniker.Substring(0, separator);
            if (!Files.TryGetValue(physical, out item) || !fileVersions.TryGetValue(physical, out var version)) return false;
            var saved = new FileInfo(physical);
            if (!saved.Exists || saved.Length != version.Item1 || saved.LastWriteTimeUtc.Ticks != version.Item2
                || (saved.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (separator < 0) return true;
            try
            {
                var info = new FileInfo(physical);
                if (info.Length > 1048576 || (info.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                var xml = File.ReadAllText(physical);
                var owner = ActivePouSource.Parse(xml);
                return owner != null && ActivePouSource.Parse(xml, ActiveEditorMemberPath.Parse(moniker, owner.Name)) != null;
            }
            catch { item = null; return false; }
        }
        private void BeginProject(string name, string config, string target, string solutionConfig)
        { ThreadHelper.ThrowIfNotOnUIThread(); if (!running) return; evidence.Begin(Same(name, config, target)); try { before = ReadOutput(); } catch { before = null; } }
        private void EndProject(string name, string config, string target, string solutionConfig, bool success)
        { ThreadHelper.ThrowIfNotOnUIThread(); if (!running) return; evidence.End(Same(name, config, target), success); }
        private void EndBuild(vsBuildScope scope, vsBuildAction action)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!running) return;
            try
            {
                if (invalid || !evidence.CanPublish || action != vsBuildAction.vsBuildActionBuild || !IsCurrent()) { Finish("BuildStale", null); return; }
                Output = BuildDiagnostic.FreshOutput(before, ReadOutput());
                Finish(!evidence.Succeeded || dte.Solution.SolutionBuild.LastBuildInfo != 0 ? "BuildFailed" : "BuildSucceeded", Output == null ? null : BuildDiagnostic.Parse(Output));
            }
            catch { Finish("BuildUnknown", null); }
        }
        internal bool IsCurrent()
        { ThreadHelper.ThrowIfNotOnUIThread(); return !disposed && !invalid && identity != null && identity.Equals(current()) && dte.Solution.FullName == identity.SolutionFile && dte.Solution.SolutionBuild.ActiveConfiguration.Name == configuration && project.ConfigurationManager.ActiveConfiguration.PlatformName == platform; }
        private void Tick(object sender, EventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (invalid) return;
                if (!IsCurrent()) { Invalidate(); return; }
                if (running && DateTime.UtcNow >= deadline) { invalid = true; render("BuildUnknown", "", null); }
                // Keep the build gate locked after timeout until IDE completion.
            }
            catch { Invalidate(); }
        }
        private void Removed(Project removed) { ThreadHelper.ThrowIfNotOnUIThread(); Invalidate(); }
        private void Closed() { ThreadHelper.ThrowIfNotOnUIThread(); running = false; Invalidate(); }
        private void Invalidate() { ThreadHelper.ThrowIfNotOnUIThread(); invalid = true; Output = null; Files.Clear(); fileVersions.Clear(); render("BuildStale", "", null); if (!running) timer.Stop(); }
        private void Finish(string key, List<BuildDiagnostic> diagnostics)
        { running = false; render(key, configuration + " | " + platform, diagnostics); if (invalid) timer.Stop(); }
        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposed) return; disposed = true; timer.Stop(); timer.Tick -= Tick;
            if (builds != null)
            {
                try { builds.OnBuildBegin -= BeginBuild; } catch { }
                try { builds.OnBuildProjConfigBegin -= BeginProject; } catch { }
                try { builds.OnBuildProjConfigDone -= EndProject; } catch { }
                try { builds.OnBuildDone -= EndBuild; } catch { }
            }
            if (solutions != null)
            {
                try { solutions.BeforeClosing -= Invalidate; } catch { }
                try { solutions.AfterClosing -= Closed; } catch { }
                try { solutions.ProjectRemoved -= Removed; } catch { }
            }
            identity = null; Files.Clear(); fileVersions.Clear(); Output = null;
        }
    }
}
