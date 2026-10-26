using System;
using System.IO;

namespace TwinCatAssistant.Native
{
    // Portable contracts shared by the actual host and regression runner.
    public sealed class ActiveEditorIdentity : IEquatable<ActiveEditorIdentity>
    {
        public int IdeProcessId { get; }
        public string SolutionFile { get; }
        public string ProjectFile { get; }
        public string DocumentMoniker { get; }
        public string TreePath { get; }

        public ActiveEditorIdentity(int pid, string solution, string project, string moniker, string treePath)
        {
            if (pid <= 0 || string.IsNullOrWhiteSpace(solution) || string.IsNullOrWhiteSpace(project)
                || string.IsNullOrWhiteSpace(moniker) || string.IsNullOrWhiteSpace(treePath))
                throw new ArgumentException("An exact IDE, solution, project, document and tree identity is required.");
            IdeProcessId = pid;
            SolutionFile = solution;
            ProjectFile = project;
            DocumentMoniker = moniker;
            TreePath = treePath;
        }

        public bool Equals(ActiveEditorIdentity other)
        {
            return other != null && IdeProcessId == other.IdeProcessId
                && string.Equals(SolutionFile, other.SolutionFile, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ProjectFile, other.ProjectFile, StringComparison.OrdinalIgnoreCase)
                && string.Equals(DocumentMoniker, other.DocumentMoniker, StringComparison.OrdinalIgnoreCase)
                && string.Equals(TreePath, other.TreePath, StringComparison.Ordinal);
        }
        public override bool Equals(object obj) { return Equals(obj as ActiveEditorIdentity); }
        public override int GetHashCode() { return IdeProcessId ^ StringComparer.Ordinal.GetHashCode(TreePath); }
    }

    public sealed class ActiveEditorSnapshot
    {
        public ActiveEditorIdentity Identity { get; set; }
        public string ObjectName { get; set; }
        public string Declaration { get; set; }
        public string Implementation { get; set; }
        public string SourceKind { get; set; }
        public string SourceNotice { get; set; }
        public DirtyAssessment Dirty { get; set; }
        public double ReadMilliseconds { get; set; }
    }

    // A generation changes even if the same path is closed and reopened. A read
    // that reenters the IDE cannot publish across lifecycle or edit notifications.
    public sealed class ActiveEditorPublication
    {
        public long Generation { get; private set; }
        public ActiveEditorSnapshot Current { get; private set; }
        public bool IsDisposed { get; private set; }

        public void Invalidate()
        {
            Generation++;
            Current = null;
        }

        public bool TryPublish(long generation, ActiveEditorSnapshot snapshot, ActiveEditorIdentity currentIdentity)
        {
            if (IsDisposed || generation != Generation || snapshot?.Identity == null
                || !snapshot.Identity.Equals(currentIdentity)) return false;
            Current = snapshot;
            return true;
        }

        public void Dispose() { Invalidate(); IsDisposed = true; }
    }

    public sealed class ActiveEditorPresentation
    {
        public string Title { get; private set; }
        public string Scope { get; private set; }
        public string Evidence { get; private set; }
        public string Detail { get; private set; }
        public string Source { get; private set; }
        public const string Disclosure = "Local context only. No source attached to chat.";

        public static ActiveEditorPresentation From(ActiveEditorSnapshot snapshot, string unavailableReason = null)
        {
            if (snapshot?.Identity == null)
                return new ActiveEditorPresentation {
                    Title = "No active ST POU", Scope = unavailableReason ?? "Open a Structured Text POU in this IDE.",
                    Evidence = "Changes: unknown", Detail = "No source context bound.", Source = ""
                };
            var dirty = snapshot.Dirty?.State == "known-dirty" ? "unsaved changes"
                : snapshot.Dirty?.State == "known-clean" ? "saved" : "unknown";
            return new ActiveEditorPresentation {
                Title = snapshot.ObjectName,
                Scope = Path.GetFileNameWithoutExtension(snapshot.Identity.ProjectFile) + " / "
                    + Path.GetFileName(snapshot.Identity.SolutionFile),
                Evidence = "Changes: " + dirty + ". Source: " + snapshot.SourceKind + ".",
                Detail = snapshot.SourceNotice,
                Source = snapshot.Declaration + Environment.NewLine + snapshot.Implementation
            };
        }
    }
}
