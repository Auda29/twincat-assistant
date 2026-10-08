using System.Security.Cryptography;
using System.Text;

namespace Engineering.Safety;

public enum TargetKind { Program, FunctionBlock, Function, Safety, Method, Property, Action, Interface, Unknown }
public enum TextLanguage { StructuredText, Unknown }
public enum DirtyState { KnownClean, KnownDirty, Unknown }
public enum ChangeOutcome { Applied, Rejected, PartialFailure, Unknown }
public enum UnknownResolution { Applied, Unchanged, Partial }
public enum ApprovalKind { Change, Rollback }
[Flags]
public enum EngineeringCapability
{
    None = 0,
    Read = 1,
    WriteDeclaration = 2,
    WriteImplementation = 4,
    DirtyState = 8,
    Revision = 16
}

public sealed class SafetyViolationException(string message) : InvalidOperationException(message);
public sealed class UnknownOutcomeException(string message) : IOException(message);
public sealed class EngineeringUnavailableException(string message) : InvalidOperationException(message);

public sealed record BindingIdentity(string IdeInstanceId, string SolutionId, string ProjectId)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(IdeInstanceId) && !string.IsNullOrWhiteSpace(SolutionId) && !string.IsNullOrWhiteSpace(ProjectId);
}

public sealed record TargetIdentity(
    BindingIdentity Binding,
    string ObjectId,
    TargetKind Kind,
    TextLanguage Language,
    bool Exists,
    bool IsSafety);

public sealed record EngineeringSnapshot(
    TargetIdentity Target,
    string Declaration,
    string Implementation,
    string Revision,
    DirtyState Dirty)
{
    public string ContentHash => SafetyHash.Sha256Fields([
        Target.Binding.IdeInstanceId,
        Target.Binding.SolutionId,
        Target.Binding.ProjectId,
        Target.ObjectId,
        Target.Kind.ToString(),
        Target.Language.ToString(),
        Target.Exists.ToString(),
        Target.IsSafety.ToString(),
        Revision,
        Dirty.ToString(),
        Declaration,
        Implementation]);
}

public sealed record EngineeringCapabilities(EngineeringCapability Available)
{
    public bool Has(EngineeringCapability required) => (Available & required) == required;
}

public interface IEngineeringClock { DateTimeOffset Now { get; } }
public sealed class SystemEngineeringClock : IEngineeringClock { public DateTimeOffset Now => DateTimeOffset.UtcNow; }

public interface IEngineeringAdapter
{
    Task<EngineeringCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken);
    Task<BindingIdentity?> GetCurrentBindingAsync(CancellationToken cancellationToken);
    Task<EngineeringSnapshot> ReadTargetAsync(TargetIdentity target, CancellationToken cancellationToken);
    Task ApplyDeclarationAsync(TargetIdentity target, string declaration, CancellationToken cancellationToken);
    Task ApplyImplementationAsync(TargetIdentity target, string implementation, CancellationToken cancellationToken);
}

public sealed record BackupRecord(
    string OperationId,
    TargetIdentity Target,
    EngineeringSnapshot Baseline,
    DateTimeOffset CreatedAtUtc);

public sealed record AuditRecord(
    string OperationId,
    string Event,
    ChangeOutcome? Outcome,
    string Target,
    string? ObservedStateHash,
    DateTimeOffset AtUtc);

public interface IEngineeringStore
{
    Task PersistBackupAsync(BackupRecord backup, CancellationToken cancellationToken);
    Task AppendAuditAsync(AuditRecord audit, CancellationToken cancellationToken);
}

public sealed record ChangeProposal(
    string Id,
    string OperationId,
    ApprovalKind Kind,
    TargetIdentity Target,
    EngineeringSnapshot Baseline,
    string ProposedDeclaration,
    string ProposedImplementation,
    string ApprovalHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? OriginalOperationId = null);

public sealed record OperatorApprovalReceipt(
    string ProposalId,
    string ApprovalHash,
    TargetIdentity Target,
    string BaselineHash,
    DateTimeOffset ExpiresAtUtc,
    string OperatorId)
{
    public static OperatorApprovalReceipt For(ChangeProposal proposal, DateTimeOffset expiresAtUtc) =>
        new(proposal.Id, proposal.ApprovalHash, proposal.Target, proposal.Baseline.ContentHash, expiresAtUtc, "operator");
}

public sealed record ChangeResult(
    string OperationId,
    ChangeOutcome Outcome,
    EngineeringSnapshot? Observed,
    string Message);

public sealed record UnknownCheckResult(
    string OperationId,
    UnknownResolution Resolution,
    EngineeringSnapshot Observed);

public static class SafetyHash
{
    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string Sha256Fields(params string?[] fields) => Sha256(Canonical(fields));

    public static string Canonical(params string?[] fields)
    {
        var builder = new StringBuilder();
        foreach (var field in fields)
        {
            if (field is null)
            {
                builder.Append("-1:");
                continue;
            }
            var bytes = Encoding.UTF8.GetBytes(field);
            builder.Append(bytes.Length).Append(':').Append(field);
        }
        return builder.ToString();
    }
}

public sealed class EngineeringCoordinator
{
    private const EngineeringCapability Required = EngineeringCapability.Read | EngineeringCapability.WriteDeclaration |
        EngineeringCapability.WriteImplementation | EngineeringCapability.DirtyState | EngineeringCapability.Revision;
    private readonly IEngineeringAdapter _adapter;
    private readonly IEngineeringStore _store;
    private readonly IEngineeringClock _clock;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly Dictionary<string, ChangeProposal> _proposals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OperatorApprovalReceipt> _approvals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OperationState> _operations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unknownTargets = new(StringComparer.Ordinal);
    private bool _shutdown;

    public EngineeringCoordinator(IEngineeringAdapter adapter, IEngineeringStore store, IEngineeringClock? clock = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? new SystemEngineeringClock();
    }

    public async Task<ChangeProposal> CreateProposalAsync(TargetIdentity target, string declaration, string implementation, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureUsableTarget(target);
            if (_shutdown) throw new SafetyViolationException("engineering binding is shut down");
            if (_unknownTargets.Contains(TargetKey(target))) throw new SafetyViolationException("target has an unknown prior outcome; reconcile first");
            if (lifetime <= TimeSpan.Zero) throw new SafetyViolationException("approval lifetime must be positive");
            var capabilities = await _adapter.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            if (!capabilities.Has(Required)) throw new SafetyViolationException("required read, write, dirty-state, or revision capability is unavailable");
            var currentBinding = await _adapter.GetCurrentBindingAsync(cancellationToken).ConfigureAwait(false);
            if (currentBinding is null || currentBinding != target.Binding) throw new SafetyViolationException("engineering binding is not current");
            var baseline = await _adapter.ReadTargetAsync(target, cancellationToken).ConfigureAwait(false);
            ValidateBaseline(target, baseline);
            var now = _clock.Now;
            var id = Guid.NewGuid().ToString("N");
            var proposal = new ChangeProposal(id, id, ApprovalKind.Change, target, baseline, declaration, implementation,
                ApprovalHash(id, ApprovalKind.Change, target, baseline, declaration, implementation, now + lifetime), now, now + lifetime);
            _proposals.Add(id, proposal);
            return proposal;
        }
        finally { _serial.Release(); }
    }

    public async Task SubmitOperatorApprovalAsync(string proposalId, OperatorApprovalReceipt receipt, CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) throw new SafetyViolationException("engineering binding is shut down");
            if (!_proposals.TryGetValue(proposalId, out var proposal)) throw new SafetyViolationException("proposal not found");
            if (receipt.ProposalId != proposal.Id || receipt.OperatorId != "operator" || receipt.Target != proposal.Target ||
                receipt.ApprovalHash != proposal.ApprovalHash || receipt.BaselineHash != proposal.Baseline.ContentHash ||
                receipt.ExpiresAtUtc != proposal.ExpiresAtUtc || receipt.ExpiresAtUtc <= _clock.Now)
                throw new SafetyViolationException("operator approval does not bind to the exact proposal");
            _approvals[proposalId] = receipt;
        }
        finally { _serial.Release(); }
    }

    public async Task<ChangeResult> ApplyAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown) throw new SafetyViolationException("engineering binding is shut down");
            var proposal = GetProposal(proposalId);
            EnsureApproved(proposal);
            EnsureUnknownTargetClear(proposal.Target);
            if (_operations.ContainsKey(proposal.OperationId)) throw new SafetyViolationException("operation has already been consumed");
            var current = await ValidateCurrentApplyStateAsync(proposal, cancellationToken).ConfigureAwait(false);
            await _store.PersistBackupAsync(new BackupRecord(proposal.OperationId, proposal.Target, current, _clock.Now), cancellationToken).ConfigureAwait(false);
            current = await ValidateCurrentApplyStateAsync(proposal, cancellationToken).ConfigureAwait(false);
            _approvals.Remove(proposalId);
            var result = await ExecuteWritesAsync(proposal, current, cancellationToken).ConfigureAwait(false);
            _operations[proposal.OperationId] = new OperationState(proposal, result);
            if (result.Outcome == ChangeOutcome.Unknown) _unknownTargets.Add(TargetKey(proposal.Target));
            await _store.AppendAuditAsync(new AuditRecord(proposal.OperationId, "apply", result.Outcome, proposal.Target.ObjectId, result.Observed?.ContentHash, _clock.Now), cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally { _serial.Release(); }
    }

    public async Task<ChangeProposal> CreateRollbackProposalAsync(string operationId, TimeSpan? lifetime = null, CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_operations.TryGetValue(operationId, out var state) || state.Result.Outcome != ChangeOutcome.Applied || state.Result.Observed is null)
                throw new SafetyViolationException("rollback requires a verified applied operation");
            var current = await _adapter.ReadTargetAsync(state.Proposal.Target, cancellationToken).ConfigureAwait(false);
            if (current.ContentHash != state.Result.Observed.ContentHash) throw new SafetyViolationException("rollback blocked because post-change state no longer matches");
            var now = _clock.Now;
            var expires = now + (lifetime ?? TimeSpan.FromMinutes(5));
            if (expires <= now) throw new SafetyViolationException("approval lifetime must be positive");
            var id = Guid.NewGuid().ToString("N");
            var proposal = new ChangeProposal(id, id, ApprovalKind.Rollback, state.Proposal.Target, current,
                state.Proposal.Baseline.Declaration, state.Proposal.Baseline.Implementation,
                ApprovalHash(id, ApprovalKind.Rollback, state.Proposal.Target, current, state.Proposal.Baseline.Declaration, state.Proposal.Baseline.Implementation, expires),
                now, expires, operationId);
            _proposals.Add(id, proposal);
            return proposal;
        }
        finally { _serial.Release(); }
    }

    public Task<ChangeResult> RollbackAsync(string rollbackProposalId, CancellationToken cancellationToken = default) => ApplyAsync(rollbackProposalId, cancellationToken);

    public async Task<UnknownCheckResult> ReconcileUnknownAsync(string operationId, CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_operations.TryGetValue(operationId, out var state) || state.Result.Outcome != ChangeOutcome.Unknown)
                throw new SafetyViolationException("operation is not awaiting unknown-outcome reconciliation");
            var capabilities = await _adapter.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            if (!capabilities.Has(Required)) throw new SafetyViolationException("required capability is unavailable for reconciliation");
            var currentBinding = await _adapter.GetCurrentBindingAsync(cancellationToken).ConfigureAwait(false);
            if (currentBinding is null || currentBinding != state.Proposal.Target.Binding)
                throw new SafetyViolationException("reconciliation binding is not the exact proposal binding");
            var observed = await _adapter.ReadTargetAsync(state.Proposal.Target, cancellationToken).ConfigureAwait(false);
            ValidateBaseline(state.Proposal.Target, observed);
            var resolution = observed.Declaration == state.Proposal.ProposedDeclaration && observed.Implementation == state.Proposal.ProposedImplementation
                ? UnknownResolution.Applied
                : observed.ContentHash == state.Proposal.Baseline.ContentHash ? UnknownResolution.Unchanged : UnknownResolution.Partial;
            await _store.AppendAuditAsync(new AuditRecord(operationId, "reconcile-unknown", null, state.Proposal.Target.ObjectId, observed.ContentHash, _clock.Now), cancellationToken).ConfigureAwait(false);
            if (resolution != UnknownResolution.Partial) _unknownTargets.Remove(TargetKey(state.Proposal.Target));
            return new UnknownCheckResult(operationId, resolution, observed);
        }
        finally { _serial.Release(); }
    }

    public void NotifyBindingChanged(BindingIdentity? currentBinding)
    {
        _serial.Wait();
        try { InvalidateBindingUnsafe(currentBinding); }
        finally { _serial.Release(); }
    }

    public void NotifyShutdown()
    {
        _serial.Wait();
        try { _shutdown = true; _proposals.Clear(); _approvals.Clear(); }
        finally { _serial.Release(); }
    }

    private async Task<ChangeResult> ExecuteWritesAsync(ChangeProposal proposal, EngineeringSnapshot current, CancellationToken cancellationToken)
    {
        try
        {
            await _adapter.ApplyDeclarationAsync(proposal.Target, proposal.ProposedDeclaration, cancellationToken).ConfigureAwait(false);
            await _adapter.ApplyImplementationAsync(proposal.Target, proposal.ProposedImplementation, cancellationToken).ConfigureAwait(false);
        }
        catch (UnknownOutcomeException error)
        {
            var observedAfterFailure = await TryReadBackAsync(proposal, cancellationToken).ConfigureAwait(false);
            return new ChangeResult(proposal.OperationId, ChangeOutcome.Unknown, observedAfterFailure, error.Message);
        }
        catch (Exception error)
        {
            var observedAfterFailure = await TryReadBackAsync(proposal, cancellationToken).ConfigureAwait(false);
            return IsReliableReadback(proposal, observedAfterFailure)
                ? new ChangeResult(proposal.OperationId, ChangeOutcome.PartialFailure, observedAfterFailure, error.Message)
                : new ChangeResult(proposal.OperationId, ChangeOutcome.Unknown, observedAfterFailure, $"outcome unknown; read-back unavailable or unreliable: {error.Message}");
        }

        EngineeringSnapshot observed;
        try { observed = await _adapter.ReadTargetAsync(proposal.Target, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) { return new ChangeResult(proposal.OperationId, ChangeOutcome.Unknown, null, $"read-back unavailable: {error.Message}"); }
        if (!IsReliableReadback(proposal, observed))
            return new ChangeResult(proposal.OperationId, ChangeOutcome.Unknown, observed, "read-back was not an exact clean target with a reliable revision");
        var expectedHash = SafetyHashFor(proposal.Target, proposal.ProposedDeclaration, proposal.ProposedImplementation, observed);
        var matches = observed.Declaration == proposal.ProposedDeclaration && observed.Implementation == proposal.ProposedImplementation &&
            observed.Target == proposal.Target && observed.Dirty == DirtyState.KnownClean;
        return matches
            ? new ChangeResult(proposal.OperationId, ChangeOutcome.Applied, observed, "declaration and implementation read back exactly")
            : new ChangeResult(proposal.OperationId, ChangeOutcome.PartialFailure, observed, $"read-back mismatch; expected content hash {expectedHash}");
    }

    private static bool IsReliableReadback(ChangeProposal proposal, EngineeringSnapshot? observed) =>
        observed is not null && observed.Target == proposal.Target && observed.Dirty == DirtyState.KnownClean && !string.IsNullOrEmpty(observed.Revision);

    private async Task<EngineeringSnapshot?> TryReadBackAsync(ChangeProposal proposal, CancellationToken cancellationToken)
    {
        try { return await _adapter.ReadTargetAsync(proposal.Target, cancellationToken).ConfigureAwait(false); }
        catch { return null; }
    }

    private async Task<EngineeringSnapshot> ValidateCurrentApplyStateAsync(ChangeProposal proposal, CancellationToken cancellationToken)
    {
        if (_shutdown) throw new SafetyViolationException("engineering binding is shut down");
        EnsureApproved(proposal);
        EnsureUnknownTargetClear(proposal.Target);
        var capabilities = await _adapter.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        if (!capabilities.Has(Required)) throw new SafetyViolationException("required capability was lost before apply");
        var currentBinding = await _adapter.GetCurrentBindingAsync(cancellationToken).ConfigureAwait(false);
        if (currentBinding is null || currentBinding != proposal.Target.Binding)
        {
            InvalidateBindingUnsafe(currentBinding);
            throw new SafetyViolationException("engineering binding changed before apply");
        }
        var current = await _adapter.ReadTargetAsync(proposal.Target, cancellationToken).ConfigureAwait(false);
        ValidateBaseline(proposal.Target, current);
        if (current.ContentHash != proposal.Baseline.ContentHash) throw new SafetyViolationException("target is stale or dirty; re-read and approve again");
        return current;
    }

    private void EnsureUnknownTargetClear(TargetIdentity target)
    {
        if (_unknownTargets.Contains(TargetKey(target))) throw new SafetyViolationException("target has an unknown prior outcome; reconcile first");
    }

    private ChangeProposal GetProposal(string id) => _proposals.TryGetValue(id, out var proposal) ? proposal : throw new SafetyViolationException("proposal not found");

    private void EnsureApproved(ChangeProposal proposal)
    {
        if (!_approvals.TryGetValue(proposal.Id, out var approval)) throw new SafetyViolationException("operator approval is required");
        if (approval.ExpiresAtUtc <= _clock.Now) throw new SafetyViolationException("operator approval expired");
        if (approval.ApprovalHash != proposal.ApprovalHash || approval.Target != proposal.Target || approval.BaselineHash != proposal.Baseline.ContentHash)
            throw new SafetyViolationException("operator approval no longer matches");
    }

    private static void EnsureUsableTarget(TargetIdentity target)
    {
        if (!target.Binding.IsComplete || string.IsNullOrWhiteSpace(target.ObjectId) || !target.Exists || target.IsSafety ||
            target.Language != TextLanguage.StructuredText || target.Kind is not (TargetKind.Program or TargetKind.FunctionBlock or TargetKind.Function))
            throw new SafetyViolationException("only existing non-safety Program, FunctionBlock, or Function ST objects are writable");
    }

    private static void ValidateBaseline(TargetIdentity target, EngineeringSnapshot baseline)
    {
        EnsureUsableTarget(target);
        if (baseline.Target != target || baseline.Dirty != DirtyState.KnownClean) throw new SafetyViolationException("baseline target is not a known clean exact object");
        if (string.IsNullOrEmpty(baseline.Revision)) throw new SafetyViolationException("baseline revision is unavailable");
    }

    private void InvalidateBindingUnsafe(BindingIdentity? currentBinding)
    {
        _proposals.Clear();
        _approvals.Clear();
        if (currentBinding is null) _shutdown = true;
    }

    private static string TargetKey(TargetIdentity target) => SafetyHash.Canonical(TargetFields(target));

    private static string[] TargetFields(TargetIdentity target) =>
    [
        target.Binding.IdeInstanceId,
        target.Binding.SolutionId,
        target.Binding.ProjectId,
        target.ObjectId,
        target.Kind.ToString(),
        target.Language.ToString(),
        target.Exists.ToString(),
        target.IsSafety.ToString()
    ];

    private static string ApprovalHash(string id, ApprovalKind kind, TargetIdentity target, EngineeringSnapshot baseline, string declaration, string implementation, DateTimeOffset expires)
    {
        var safetyHash = SafetyHashFor(target, declaration, implementation, baseline);
        return SafetyHash.Sha256Fields([
            id, kind.ToString(), .. TargetFields(target), baseline.ContentHash, declaration, implementation,
            safetyHash, expires.ToUniversalTime().ToString("O")]);
    }

    private static string SafetyHashFor(TargetIdentity target, string declaration, string implementation, EngineeringSnapshot observed) =>
        SafetyHash.Sha256Fields([.. TargetFields(target), observed.ContentHash, declaration, implementation, observed.Revision, observed.Dirty.ToString()]);

    private sealed record OperationState(ChangeProposal Proposal, ChangeResult Result);
}
