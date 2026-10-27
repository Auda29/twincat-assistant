using System;

namespace TwinCatAssistant.Native
{
    // Project authorization does not depend on a supported source editor.
    public sealed class NativeProjectIdentity : IEquatable<NativeProjectIdentity>
    {
        public int IdeProcessId { get; }
        public string SolutionFile { get; }
        public string ProjectFile { get; }
        public NativeProjectIdentity(int processId, string solution, string project)
        {
            if (processId <= 0 || string.IsNullOrWhiteSpace(solution) || string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("An exact IDE, solution and PLC project are required.");
            IdeProcessId = processId; SolutionFile = solution; ProjectFile = project;
        }
        public bool Equals(NativeProjectIdentity other)
        {
            return other != null && IdeProcessId == other.IdeProcessId
                && string.Equals(SolutionFile, other.SolutionFile, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ProjectFile, other.ProjectFile, StringComparison.OrdinalIgnoreCase);
        }
        public override bool Equals(object obj) { return Equals(obj as NativeProjectIdentity); }
        public override int GetHashCode() { return IdeProcessId ^ StringComparer.OrdinalIgnoreCase.GetHashCode(ProjectFile); }
    }
}
