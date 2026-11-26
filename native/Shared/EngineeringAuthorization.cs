using System;
using System.Security.Cryptography;
using System.Text;

namespace TwinCatAssistant.Native
{
    internal sealed class EngineeringSourceCreation
    {
        internal readonly Guid OperationId;
        internal readonly string ProjectFile, TargetPath;

        internal EngineeringSourceCreation(Guid operationId, string projectFile, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(projectFile) || string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("Source creation requires a project and a target path.");
            OperationId = operationId;
            ProjectFile = projectFile;
            TargetPath = targetPath;
        }
    }

    // Kept for the IDE process lifetime by AssistantControl. Losing a view or
    // provider is not evidence that an already submitted operation has finished.
    internal sealed class EngineeringOperationGuard
    {
        private readonly object gate = new object();
        private Guid operation;
        private EngineeringSourceCreation creation;
        private bool creationUnknown;
        internal Guid OperationId { get { lock (gate) return operation; } }
        internal bool HasUnresolved { get { return OperationId != Guid.Empty; } }
        internal EngineeringSourceCreation UnknownSourceCreation { get { lock (gate) return creationUnknown ? creation : null; } }
        internal bool TryBegin(Guid id, EngineeringSourceCreation sourceCreation = null)
        {
            if (id == Guid.Empty || (sourceCreation != null && sourceCreation.OperationId != id))
                throw new ArgumentException("A matching operation identity is required.");
            lock (gate)
            {
                if (operation != Guid.Empty) return false;
                operation = id;
                creation = sourceCreation;
                return true;
            }
        }
        internal void MarkSourceCreationUnknown(Guid id)
        {
            lock (gate) { if (creation != null && operation == id) creationUnknown = true; }
        }
        internal bool AcknowledgeSourceCreation(EngineeringSourceCreation inspected, bool confirmed)
        {
            lock (gate)
            {
                return confirmed && creationUnknown && ReferenceEquals(inspected, creation) && Reconcile(inspected.OperationId);
            }
        }
        internal bool Reconcile(Guid id)
        {
            lock (gate)
            {
                if (id == Guid.Empty || operation != id) return false;
                operation = Guid.Empty;
                creation = null;
                creationUnknown = false;
                return true;
            }
        }
    }

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
        internal const string EmptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

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
                || (operation != "build.start" && !IsSourceMutation(operation)))
                throw new ArgumentException("An exact native action is required.");
            if (operation != "build.start" && (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(section)
                || !Digest(baseline) || !Digest(proposed)
                || (operation == "source.create"
                    ? !IsCreateKind(section) || preview != null || journal != null || baseline != EmptyDigest
                    : string.IsNullOrWhiteSpace(preview) || (operation == "source.apply" ? journal != null : string.IsNullOrWhiteSpace(journal)))))
                throw new ArgumentException("An exact source action is required.");
            Binding = binding; OperationId = operationId; Operation = operation;
            TargetObjectId = target; Section = section; PreviewId = preview; JournalId = journal;
            ExpiresUtc = expires; RequestDigest = requestDigest; BaselineDigest = baseline; ProposedDigest = proposed;
        }
        internal static bool IsSourceMutation(string operation)
        { return operation == "source.apply" || operation == "source.rollback" || operation == "source.create"; }

        internal static bool IsCreateKind(string kind)
        {
            switch (kind)
            {
                case "fb": case "prg": case "fun": case "method": case "action":
                case "property": case "dut_struct": case "gvl": return true;
                default: return false;
            }
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
