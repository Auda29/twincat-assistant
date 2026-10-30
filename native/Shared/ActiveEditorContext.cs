using System;
using System.IO;
using System.Text.RegularExpressions;

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
        public NativeProjectIdentity ProjectIdentity { get { return new NativeProjectIdentity(IdeProcessId, SolutionFile, ProjectFile); } }

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
        public string ObjectKind { get; set; }
        public string OwnerName { get; set; }
        public string MemberPath { get; set; }
        public string Declaration { get; set; }
        public string Implementation { get; set; }
        public string SourceKind { get; set; }
        public string SourceNotice { get; set; }
        public DirtyAssessment Dirty { get; set; }
        public double ReadMilliseconds { get; set; }
        public long ContextGeneration { get; set; }
    }

    public static class ActiveEditorMemberPath
    {
        public static string[] Parse(string moniker, string owner)
        {
            var index = (moniker ?? "").IndexOf(';');
            if (index < 0) return new string[0];
            var names = moniker.Substring(index + 1).Split(new[] { '.', '/', '\\', '^' }, StringSplitOptions.None);
            if (names.Length < 1 || names.Length > 3 || !string.Equals(names[0], owner, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The editor moniker does not identify this POU and member chain.");
            foreach (var name in names)
                if (!Regex.IsMatch(name, @"\A[\p{L}_][\p{L}\p{Nd}_]*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    throw new InvalidOperationException("The editor member identity is unsupported or ambiguous.");
            var result = new string[names.Length - 1];
            Array.Copy(names, 1, result, 0, result.Length);
            return result;
        }
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

        // Short POU kind for the context badge, taken from the declaration's
        // leading keyword. Unknown declarations fall back to the generic "ST".
        public static string Kind(string declaration)
        {
            var text = (declaration ?? "").TrimStart();
            foreach (var pair in new[] { new[] { "FUNCTION_BLOCK", "FB" }, new[] { "FUNCTION", "FUN" }, new[] { "PROGRAM", "PRG" },
                new[] { "METHOD", "METH" }, new[] { "PROPERTY", "PROP" }, new[] { "INTERFACE", "ITF" }, new[] { "TYPE", "DUT" } })
                if (text.StartsWith(pair[0], StringComparison.OrdinalIgnoreCase)
                    && (text.Length == pair[0].Length || !char.IsLetterOrDigit(text[pair[0].Length]) && text[pair[0].Length] != '_')) return pair[1];
            return "ST";
        }

        public static string BadgeKind(ActiveEditorSnapshot snapshot)
        {
            switch (snapshot?.ObjectKind)
            {
                case "Action": return "ACT";
                case "Method": return "METH";
                case "Property": return "PROP";
                case "Get": return "GET";
                case "Set": return "SET";
                default: return Kind(snapshot?.Declaration);
            }
        }

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
