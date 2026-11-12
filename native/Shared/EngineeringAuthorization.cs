using System;
using System.Security.Cryptography;
using System.Text;

namespace TwinCatAssistant.Native
{
    // Host-owned one-shot authorization. Neither a tool argument nor possession
    // of a transport capability grants permission to compile or mutate source.
    internal sealed class EngineeringBinding
    {
        internal readonly Guid SessionId;
        internal readonly int IdePid;
        internal readonly DateTime IdeStartedUtc;
        internal readonly string SolutionFile, ProjectFile, ContextEpoch;
        internal EngineeringBinding(Guid session, int pid, DateTime started, string solution, string project, string epoch)
        {
            if (session == Guid.Empty || pid <= 0 || started.Kind != DateTimeKind.Utc
                || string.IsNullOrWhiteSpace(solution) || string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(epoch))
                throw new ArgumentException("A complete engineering binding is required.");
            SessionId = session; IdePid = pid; IdeStartedUtc = started;
            SolutionFile = solution; ProjectFile = project; ContextEpoch = epoch;
        }
        internal bool Same(EngineeringBinding other)
        {
            return other != null && SessionId == other.SessionId && IdePid == other.IdePid
                && IdeStartedUtc == other.IdeStartedUtc && ContextEpoch == other.ContextEpoch
                && string.Equals(SolutionFile, other.SolutionFile, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ProjectFile, other.ProjectFile, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class EngineeringAction
    {
        internal readonly EngineeringBinding Binding;
        internal readonly Guid OperationId;
        internal readonly string Operation, TargetObjectId, Section, PreviewId, JournalId;
        internal readonly string RequestDigest, BaselineDigest, ProposedDigest;
        internal readonly DateTime ExpiresUtc;
        internal EngineeringAction(EngineeringBinding binding, Guid operationId, string operation,
            string target, string section, string preview, string journal, DateTime expires,
            string requestDigest, string baseline, string proposed)
        {
            if (binding == null || operationId == Guid.Empty || expires.Kind != DateTimeKind.Utc || !Digest(requestDigest)
                || (operation != "build.start" && operation != "source.apply" && operation != "source.rollback"))
                throw new ArgumentException("An exact native action is required.");
            if (operation != "build.start" && (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(section)
                || !Digest(baseline) || !Digest(proposed)
                || (operation == "source.apply" ? string.IsNullOrWhiteSpace(preview) : string.IsNullOrWhiteSpace(journal))))
                throw new ArgumentException("An exact source action is required.");
            Binding = binding; OperationId = operationId; Operation = operation;
            TargetObjectId = target; Section = section; PreviewId = preview; JournalId = journal;
            ExpiresUtc = expires; RequestDigest = requestDigest; BaselineDigest = baseline; ProposedDigest = proposed;
        }
        private static bool Digest(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }
        internal bool Same(EngineeringAction other)
        {
            return other != null && Binding.Same(other.Binding) && OperationId == other.OperationId
                && Operation == other.Operation && TargetObjectId == other.TargetObjectId && Section == other.Section
                && PreviewId == other.PreviewId && JournalId == other.JournalId && ExpiresUtc == other.ExpiresUtc
                && RequestDigest == other.RequestDigest && BaselineDigest == other.BaselineDigest && ProposedDigest == other.ProposedDigest;
        }
        internal static string HashRequest(string request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false, true).GetBytes(request))).Replace("-", "").ToLowerInvariant();
        }
    }

    internal sealed class EngineeringAuthorization
    {
        private readonly object gate = new object();
        private EngineeringAction pending;
        // Called only by the native UI after displaying the exact action/diff.
        internal void Grant(EngineeringAction action, DateTime nowUtc)
        {
            if (action == null || nowUtc.Kind != DateTimeKind.Utc || action.ExpiresUtc <= nowUtc
                || action.ExpiresUtc > nowUtc.AddMinutes(2)) throw new ArgumentException("Approval has expired or exceeds its lifetime.");
            lock (gate) pending = action;
        }
        internal bool Consume(EngineeringAction action, EngineeringBinding current, DateTime nowUtc)
        {
            lock (gate)
            {
                var granted = pending; pending = null;
                return nowUtc.Kind == DateTimeKind.Utc && granted != null && granted.Same(action)
                    && granted.Binding.Same(current) && nowUtc < granted.ExpiresUtc;
            }
        }
        internal void Revoke() { lock (gate) pending = null; }
    }
}
