using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Engineering.Bridge;
using Engineering.Safety;

namespace Engineering.Safety.Tests;

internal static class Program
{
    private static readonly Func<Task>[] Cases =
    [
        DomainTests.AllowedExistingStObjectCanBeProposedAndApplied,
        DomainTests.SafetyAndUnsupportedObjectsAreRejected,
        DomainTests.DirtyUnknownOrMissingCapabilitiesBlockProposal,
        DomainTests.OperatorApprovalBindsExactHashTargetAndBaseline,
        DomainTests.StaleRevisionAndDirtyStateBlockApply,
        DomainTests.ApprovalExpiresAndCannotBeReplayed,
        DomainTests.BindingSwitchInvalidatesApproval,
        DomainTests.ConcurrentApplySerializesAndConsumesOnce,
        DomainTests.BackupIsPersistedBeforeAdapterCalls,
        DomainTests.PartialFailureIsVisibleAndNotRetried,
        DomainTests.AmbiguousFailureBlocksNewRequestsUntilFreshCheck,
        DomainTests.ReadbackMismatchIsPartialFailure,
        DomainTests.RollbackRequiresMatchingPostStateAndSeparateApproval,
        DomainTests.RollbackBlocksNewerUserChange,
        DomainTests.UnknownRollbackIsNeverRetried,
        DomainTests.ShutdownRejectsApprovalReplay,
        DomainTests.BindingRoundTripRejectsOldProposal,
        DomainTests.CanonicalHashesDoNotCollide,
        DomainTests.UnknownReconcileRequiresExactCleanState,
        DomainTests.PartialUnknownRemainsBlocked,
        DomainTests.ReconcileAuditMustSucceedBeforeUnblock,
        DomainTests.UnknownApplyBlocksAlreadyApprovedProposal,
        DomainTests.CapabilityLossAfterBackupBlocksWrites,
        DomainTests.GenericFailureWithUnreadableReadbackIsUnknown,
        PersistenceTests.LocalStoreUsesBoundedPrivateFiles,
        PipeTests.ValidTokenAndReadOnlyAllowlistWork,
        PipeTests.InvalidTokenIsRejected,
        PipeTests.FramesAndResponsesAreBoundedAndStable,
        PipeTests.TopLevelMalformedFramesAreStable,
        PipeTests.IdMismatchIsRejectedBeforeError,
        PipeTests.ExactFrameLimitExcludesNewline,
        PipeTests.IdlePeerDoesNotBlockNextPeer,
        PipeTests.MutationMethodsAreNotExposed,
        PipeTests.UnavailableDiagnosticServerDoesNotPretendToBuild
    ];

    public static async Task<int> Main()
    {
        var failed = 0;
        foreach (var test in Cases)
        {
            try
            {
                // A synchronous Windows pipe stall must fail the fixture rather
                // than hang the entire regression process indefinitely.
                await Task.Run(test).WaitAsync(TimeSpan.FromSeconds(15));
                Console.WriteLine($"PASS {test.Method.Name}");
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine($"FAIL {test.Method.Name}: {error.Message}");
            }
        }

        Console.WriteLine($"RESULT passed={Cases.Length - failed} failed={failed} total={Cases.Length}");
        return failed == 0 ? 0 : 1;
    }
}

internal static class DomainTests
{
    private static readonly BindingIdentity Binding = new("ide-a", "solution-a", "project-a");
    private static readonly TargetIdentity Target = new(Binding, "FB_Main", TargetKind.FunctionBlock, TextLanguage.StructuredText, true, false);

    public static async Task AllowedExistingStObjectCanBeProposedAndApplied()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var store = new TestStore();
        var safety = new EngineeringCoordinator(adapter, store, new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "VAR_INPUT x : BOOL; END_VAR", "x := TRUE;", TimeSpan.FromMinutes(5));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        var result = await safety.ApplyAsync(proposal.Id);
        Check.Equal(ChangeOutcome.Applied, result.Outcome);
        Check.Equal(1, store.Backups.Count);
        Check.Equal("VAR_INPUT x : BOOL; END_VAR", adapter.Current.Declaration);
        Check.Equal("x := TRUE;", adapter.Current.Implementation);
        Check.True(adapter.Writes.Count == 2, "declaration and implementation should be separate writes");
    }

    public static async Task SafetyAndUnsupportedObjectsAreRejected()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.CreateProposalAsync(Target with { Kind = TargetKind.Method }, "d", "i", TimeSpan.FromMinutes(1)));
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.CreateProposalAsync(Target with { Kind = TargetKind.Safety, IsSafety = true }, "d", "i", TimeSpan.FromMinutes(1)));
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.CreateProposalAsync(Target with { Language = TextLanguage.Unknown }, "d", "i", TimeSpan.FromMinutes(1)));
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.CreateProposalAsync(Target with { Exists = false }, "d", "i", TimeSpan.FromMinutes(1)));
    }

    public static async Task DirtyUnknownOrMissingCapabilitiesBlockProposal()
    {
        var clock = new ManualClock();
        var dirty = new TestAdapter(Snapshot("r1", DirtyState.KnownDirty));
        await Check.ThrowsAsync<SafetyViolationException>(() => new EngineeringCoordinator(dirty, new TestStore(), clock).CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1)));
        var unknown = new TestAdapter(Snapshot("r1", DirtyState.Unknown));
        await Check.ThrowsAsync<SafetyViolationException>(() => new EngineeringCoordinator(unknown, new TestStore(), clock).CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1)));
        var noWrite = new TestAdapter(Snapshot("r1", DirtyState.KnownClean), EngineeringCapability.Read | EngineeringCapability.DirtyState | EngineeringCapability.Revision);
        await Check.ThrowsAsync<SafetyViolationException>(() => new EngineeringCoordinator(noWrite, new TestStore(), clock).CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1)));
    }

    public static async Task OperatorApprovalBindsExactHashTargetAndBaseline()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        var wrong = new OperatorApprovalReceipt(proposal.Id, "wrong", proposal.Target, proposal.Baseline.ContentHash, proposal.ExpiresAtUtc, "operator");
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.SubmitOperatorApprovalAsync(proposal.Id, wrong));
        var wrongTarget = new OperatorApprovalReceipt(proposal.Id, proposal.ApprovalHash, Target with { ObjectId = "other" }, proposal.Baseline.ContentHash, proposal.ExpiresAtUtc, "operator");
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.SubmitOperatorApprovalAsync(proposal.Id, wrongTarget));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
    }

    public static async Task StaleRevisionAndDirtyStateBlockApply()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        adapter.Current = Snapshot("r2", DirtyState.KnownClean);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ApplyAsync(proposal.Id));
        var proposal2 = await safety.CreateProposalAsync(Target, "d2", "i2", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal2.Id, OperatorApprovalReceipt.For(proposal2, proposal2.ExpiresAtUtc));
        adapter.Current = Snapshot("r1", DirtyState.KnownDirty);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ApplyAsync(proposal2.Id));
        Check.Equal(0, adapter.Writes.Count);
    }

    public static async Task ApprovalExpiresAndCannotBeReplayed()
    {
        var clock = new ManualClock();
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var safety = new EngineeringCoordinator(adapter, new TestStore(), clock);
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromSeconds(2));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        clock.Advance(TimeSpan.FromSeconds(3));
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ApplyAsync(proposal.Id));
        var fresh = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(fresh.Id, OperatorApprovalReceipt.For(fresh, fresh.ExpiresAtUtc));
        Check.Equal(ChangeOutcome.Applied, (await safety.ApplyAsync(fresh.Id)).Outcome);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ApplyAsync(fresh.Id));
    }

    public static async Task BindingSwitchInvalidatesApproval()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        safety.NotifyBindingChanged(Binding with { ProjectId = "project-b" });
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ApplyAsync(proposal.Id));
    }

    public static async Task ConcurrentApplySerializesAndConsumesOnce()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean)) { WriteDelay = TimeSpan.FromMilliseconds(20) };
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        var results = await Task.WhenAll(Observe(safety.ApplyAsync(proposal.Id)), Observe(safety.ApplyAsync(proposal.Id)));
        Check.Equal(1, results.Count(x => x is ChangeResult r && r.Outcome == ChangeOutcome.Applied));
        Check.Equal(1, results.Count(x => x is SafetyViolationException));
    }

    private static async Task<object> Observe(Task<ChangeResult> task)
    {
        try { return await task; }
        catch (Exception error) { return error; }
    }

    public static async Task BackupIsPersistedBeforeAdapterCalls()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var store = new TestStore { ThrowOnBackup = true };
        var safety = new EngineeringCoordinator(adapter, store, new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        await Check.ThrowsAsync<IOException>(() => safety.ApplyAsync(proposal.Id));
        Check.Equal(0, adapter.Writes.Count);
    }

    public static async Task PartialFailureIsVisibleAndNotRetried()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean)) { FailImplementation = new IOException("implementation failed") };
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        var result = await safety.ApplyAsync(proposal.Id);
        Check.Equal(ChangeOutcome.PartialFailure, result.Outcome);
        Check.Equal(1, adapter.Writes.Count);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ApplyAsync(proposal.Id));
    }

    public static async Task AmbiguousFailureBlocksNewRequestsUntilFreshCheck()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean)) { FailDeclaration = new UnknownOutcomeException("pipe disconnected") };
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        Check.Equal(ChangeOutcome.Unknown, (await safety.ApplyAsync(proposal.Id)).Outcome);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.CreateProposalAsync(Target, "other", "other", TimeSpan.FromMinutes(1)));
        adapter.FailDeclaration = null;
        var resolution = await safety.ReconcileUnknownAsync(proposal.OperationId);
        Check.Equal(UnknownResolution.Unchanged, resolution.Resolution);
        var next = await safety.CreateProposalAsync(Target, "other", "other", TimeSpan.FromMinutes(1));
        Check.True(next.Id != proposal.Id, "fresh request should be possible after reconciliation");
    }

    public static async Task ReadbackMismatchIsPartialFailure()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean)) { CorruptReadback = true };
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        Check.Equal(ChangeOutcome.PartialFailure, (await safety.ApplyAsync(proposal.Id)).Outcome);
    }

    public static async Task RollbackRequiresMatchingPostStateAndSeparateApproval()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "new-d", "new-i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        var operation = await safety.ApplyAsync(proposal.Id);
        var rollback = await safety.CreateRollbackProposalAsync(operation.OperationId);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.RollbackAsync(rollback.Id));
        await safety.SubmitOperatorApprovalAsync(rollback.Id, OperatorApprovalReceipt.For(rollback, rollback.ExpiresAtUtc));
        Check.Equal(ChangeOutcome.Applied, (await safety.RollbackAsync(rollback.Id)).Outcome);
        Check.Equal("d0", adapter.Current.Declaration);
    }

    public static async Task RollbackBlocksNewerUserChange()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "new-d", "new-i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        var operation = await safety.ApplyAsync(proposal.Id);
        var rollback = await safety.CreateRollbackProposalAsync(operation.OperationId);
        adapter.Current = Snapshot("r-user", DirtyState.KnownClean) with { Declaration = "user-d", Implementation = "user-i" };
        await safety.SubmitOperatorApprovalAsync(rollback.Id, OperatorApprovalReceipt.For(rollback, rollback.ExpiresAtUtc));
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.RollbackAsync(rollback.Id));
        Check.Equal("user-d", adapter.Current.Declaration);
    }

    public static async Task UnknownRollbackIsNeverRetried()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "new-d", "new-i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        var operation = await safety.ApplyAsync(proposal.Id);
        var rollback = await safety.CreateRollbackProposalAsync(operation.OperationId);
        await safety.SubmitOperatorApprovalAsync(rollback.Id, OperatorApprovalReceipt.For(rollback, rollback.ExpiresAtUtc));
        adapter.FailDeclaration = new UnknownOutcomeException("lost");
        Check.Equal(ChangeOutcome.Unknown, (await safety.RollbackAsync(rollback.Id)).Outcome);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.RollbackAsync(rollback.Id));
    }

    public static async Task ShutdownRejectsApprovalReplay()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        var receipt = OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc);
        safety.NotifyShutdown();
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.SubmitOperatorApprovalAsync(proposal.Id, receipt));
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ApplyAsync(proposal.Id));
    }

    public static async Task BindingRoundTripRejectsOldProposal()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        var receipt = OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc);
        safety.NotifyBindingChanged(Binding with { ProjectId = "project-b" });
        safety.NotifyBindingChanged(Binding);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.SubmitOperatorApprovalAsync(proposal.Id, receipt));
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ApplyAsync(proposal.Id));
    }

    public static async Task CanonicalHashesDoNotCollide()
    {
        var firstSnapshot = new EngineeringSnapshot(Target, "d", "\ni", "r1", DirtyState.KnownClean);
        var secondSnapshot = new EngineeringSnapshot(Target, "d\n", "i", "r1", DirtyState.KnownClean);
        Check.True(firstSnapshot.ContentHash != secondSnapshot.ContentHash, "canonical encoding must distinguish newline field boundaries");

        var adapter = new TestAdapter(firstSnapshot);
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var delimiterA = Target with { Binding = new BindingIdentity("a|b", "c", "d"), ObjectId = "e" };
        var delimiterB = Target with { Binding = new BindingIdentity("a", "b|c", "d"), ObjectId = "e" };
        adapter.Current = adapter.Current with { Target = delimiterA };
        var firstTarget = await safety.CreateProposalAsync(delimiterA, "x", "y", TimeSpan.FromMinutes(1));
        adapter.Current = adapter.Current with { Target = delimiterB };
        _ = await safety.CreateProposalAsync(delimiterB, "x", "y", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(firstTarget.Id, OperatorApprovalReceipt.For(firstTarget, firstTarget.ExpiresAtUtc));
        adapter.Current = adapter.Current with { Target = delimiterA };
        adapter.FailDeclaration = new UnknownOutcomeException("lost");
        Check.Equal(ChangeOutcome.Unknown, (await safety.ApplyAsync(firstTarget.Id)).Outcome);
        adapter.FailDeclaration = null;
        adapter.Current = adapter.Current with { Target = delimiterB };
        var freshForDelimiterB = await safety.CreateProposalAsync(delimiterB, "fresh", "fresh", TimeSpan.FromMinutes(1));
        Check.True(freshForDelimiterB.Target == delimiterB, "delimiter-distinct target must not inherit another target's block");
    }

    public static async Task UnknownReconcileRequiresExactCleanState()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean)) { FailDeclaration = new UnknownOutcomeException("lost") };
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        Check.Equal(ChangeOutcome.Unknown, (await safety.ApplyAsync(proposal.Id)).Outcome);
        adapter.FailDeclaration = null;
        adapter.Current = adapter.Current with { Target = Target with { ObjectId = "other" } };
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ReconcileUnknownAsync(proposal.OperationId));
        adapter.Current = Snapshot("r1", DirtyState.KnownDirty);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ReconcileUnknownAsync(proposal.OperationId));
    }

    public static async Task PartialUnknownRemainsBlocked()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean)) { FailDeclaration = new UnknownOutcomeException("lost") };
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        Check.Equal(ChangeOutcome.Unknown, (await safety.ApplyAsync(proposal.Id)).Outcome);
        adapter.FailDeclaration = null;
        adapter.Current = Snapshot("r2", DirtyState.KnownClean) with { Declaration = "partial" };
        Check.Equal(UnknownResolution.Partial, (await safety.ReconcileUnknownAsync(proposal.OperationId)).Resolution);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.CreateProposalAsync(Target, "next", "next", TimeSpan.FromMinutes(1)));
    }

    public static async Task ReconcileAuditMustSucceedBeforeUnblock()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean)) { FailDeclaration = new UnknownOutcomeException("lost") };
        var store = new TestStore();
        var safety = new EngineeringCoordinator(adapter, store, new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        Check.Equal(ChangeOutcome.Unknown, (await safety.ApplyAsync(proposal.Id)).Outcome);
        adapter.FailDeclaration = null;
        store.ThrowOnAudit = true;
        await Check.ThrowsAsync<IOException>(() => safety.ReconcileUnknownAsync(proposal.OperationId));
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.CreateProposalAsync(Target, "next", "next", TimeSpan.FromMinutes(1)));
        store.ThrowOnAudit = false;
        Check.Equal(UnknownResolution.Unchanged, (await safety.ReconcileUnknownAsync(proposal.OperationId)).Resolution);
        _ = await safety.CreateProposalAsync(Target, "next", "next", TimeSpan.FromMinutes(1));
    }

    public static async Task UnknownApplyBlocksAlreadyApprovedProposal()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean)) { FailDeclaration = new UnknownOutcomeException("lost") };
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var first = await safety.CreateProposalAsync(Target, "d1", "i1", TimeSpan.FromMinutes(1));
        var second = await safety.CreateProposalAsync(Target, "d2", "i2", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(first.Id, OperatorApprovalReceipt.For(first, first.ExpiresAtUtc));
        await safety.SubmitOperatorApprovalAsync(second.Id, OperatorApprovalReceipt.For(second, second.ExpiresAtUtc));
        Check.Equal(ChangeOutcome.Unknown, (await safety.ApplyAsync(first.Id)).Outcome);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ApplyAsync(second.Id));
    }

    public static async Task CapabilityLossAfterBackupBlocksWrites()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean));
        var store = new TestStore { OnBackup = () => adapter.CapabilitiesOverride = EngineeringCapability.Read | EngineeringCapability.DirtyState | EngineeringCapability.Revision };
        var safety = new EngineeringCoordinator(adapter, store, new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.ApplyAsync(proposal.Id));
        Check.Equal(0, adapter.Writes.Count);
    }

    public static async Task GenericFailureWithUnreadableReadbackIsUnknown()
    {
        var adapter = new TestAdapter(Snapshot("r1", DirtyState.KnownClean)) { FailDeclaration = new IOException("write may have happened") };
        adapter.OnDeclarationAttempt = () =>
        {
            adapter.Current = adapter.Current with { Declaration = "d", Revision = "r1-d" };
            adapter.FailReadback = true;
        };
        var safety = new EngineeringCoordinator(adapter, new TestStore(), new ManualClock());
        var proposal = await safety.CreateProposalAsync(Target, "d", "i", TimeSpan.FromMinutes(1));
        await safety.SubmitOperatorApprovalAsync(proposal.Id, OperatorApprovalReceipt.For(proposal, proposal.ExpiresAtUtc));
        Check.Equal(ChangeOutcome.Unknown, (await safety.ApplyAsync(proposal.Id)).Outcome);
        await Check.ThrowsAsync<SafetyViolationException>(() => safety.CreateProposalAsync(Target, "next", "next", TimeSpan.FromMinutes(1)));
    }

    private static EngineeringSnapshot Snapshot(string revision, DirtyState dirty) =>
        new(Target, "d0", "i0", revision, dirty);
}

internal static class PersistenceTests
{
    public static async Task LocalStoreUsesBoundedPrivateFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "engineering-safety-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileEngineeringStore(root);
            var binding = new BindingIdentity("i", "s", "p");
            var target = new TargetIdentity(binding, "FB", TargetKind.FunctionBlock, TextLanguage.StructuredText, true, false);
            var snapshot = new EngineeringSnapshot(target, "d", "i", "r", DirtyState.KnownClean);
            await store.PersistBackupAsync(new BackupRecord("op", target, snapshot, DateTimeOffset.UtcNow), CancellationToken.None);
            await store.AppendAuditAsync(new AuditRecord("op", "apply", ChangeOutcome.Applied, "FB", snapshot.ContentHash, DateTimeOffset.UtcNow), CancellationToken.None);
            var files = Directory.GetFiles(root, "*.json");
            Check.Equal(2, files.Length);
            if (!OperatingSystem.IsWindows())
            {
                Check.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(files[0]) & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

internal static class PipeTests
{
    public static async Task ValidTokenAndReadOnlyAllowlistWork()
    {
        await using var server = TestServer.Create();
        await server.StartAsync();
        await using var client = await AuthenticatedPipeClient.ConnectAsync(server.PipeName, server.Token);
        var response = await client.RequestAsync("get-status", new { });
        Check.Equal("ok-1", response.GetProperty("id").GetString());
        Check.True(response.GetProperty("result").GetProperty("available").GetBoolean());
        var read = await client.RequestAsync("read-target", new { objectId = "FB_Main" });
        Check.True(read.GetProperty("result").GetProperty("readOnly").GetBoolean());
        await server.StopAsync();
    }

    public static async Task InvalidTokenIsRejected()
    {
        await using var server = TestServer.Create();
        await server.StartAsync();
        await Check.ThrowsAsync<AuthenticationException>(() => AuthenticatedPipeClient.ConnectAsync(server.PipeName, "wrong-token"));
        await server.StopAsync();
    }

    public static async Task FramesAndResponsesAreBoundedAndStable()
    {
        await using var server = TestServer.Create(maxFrameBytes: 256);
        await server.StartAsync();
        await using var client = await AuthenticatedPipeClient.ConnectAsync(server.PipeName, server.Token, maxFrameBytes: 256);
        var response = await client.RequestAsync("get-context", new { });
        Check.Equal("ok-1", response.GetProperty("id").GetString());
        Check.True(response.GetProperty("result").GetProperty("context").GetProperty("binding").GetProperty("ideInstanceId").GetString() is not null);
        await Check.ThrowsAsync<BridgeProtocolException>(() => client.RequestAsync("get-context", new { oversized = new string('x', 512) }));
        await server.StopAsync();
    }

    public static async Task MutationMethodsAreNotExposed()
    {
        await using var server = TestServer.Create();
        await server.StartAsync();
        await using var client = await AuthenticatedPipeClient.ConnectAsync(server.PipeName, server.Token);
        var response = await client.RequestAsync("apply", new { });
        Check.Equal("method_not_allowed", response.GetProperty("error").GetProperty("code").GetString());
        await server.StopAsync();
    }

    public static async Task UnavailableDiagnosticServerDoesNotPretendToBuild()
    {
        await using var server = TestServer.Create(new UnavailableBridgeReadService());
        await server.StartAsync();
        await using var client = await AuthenticatedPipeClient.ConnectAsync(server.PipeName, server.Token);
        var response = await client.RequestAsync("get-status", new { });
        Check.Equal("unavailable", response.GetProperty("result").GetProperty("status").GetString());
        await server.StopAsync();
    }

    public static async Task TopLevelMalformedFramesAreStable()
    {
        await using var server = TestServer.Create();
        await server.StartAsync();
            await using (var malformed = await RawClient.ConnectAsync(server.PipeName))
            {
                await malformed.SendAsync("[]\n");
                var error = await malformed.ReadAsync();
                Check.Equal("unauthorized", error.GetProperty("error").GetProperty("code").GetString());
            }
            await using (var malformed = await RawClient.ConnectAsync(server.PipeName))
            {
                await malformed.SendAsync("null\n");
                var error = await malformed.ReadAsync();
                Check.Equal("unauthorized", error.GetProperty("error").GetProperty("code").GetString());
            }
        foreach (var protocol in new[] { "\"wrong\"", "null", "[]", "{}" })
        {
            await using var malformed = await RawClient.ConnectAsync(server.PipeName);
            await malformed.SendAsync($"{{\"type\":\"hello\",\"protocol\":{protocol},\"token\":\"{server.Token}\"}}\n");
            Check.Equal("unauthorized", (await malformed.ReadAsync()).GetProperty("error").GetProperty("code").GetString());
        }
        await using var client = await RawClient.ConnectAsync(server.PipeName);
        await client.SendAsync($"{{\"type\":\"hello\",\"protocol\":1,\"token\":\"{server.Token}\"}}\n");
        _ = await client.ReadAsync();
        await client.SendAsync("[]\n");
        Check.Equal("invalid_request", (await client.ReadAsync()).GetProperty("error").GetProperty("code").GetString());
        await client.SendAsync("null\n");
        Check.Equal("invalid_request", (await client.ReadAsync()).GetProperty("error").GetProperty("code").GetString());
        await client.SendAsync("{\"id\":\"x\",\"method\":\"get-status\",\"params\":{}}\n");
        var response = await client.ReadAsync();
        Check.Equal("x", response.GetProperty("id").GetString());
    }

    public static async Task IdMismatchIsRejectedBeforeError()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeName = $"engineering-safety-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var connected = server.WaitForConnectionAsync();
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2000);
        await connected;
        var client = new AuthenticatedPipeClient(pipe, 8192);
        var hello = PipeBridgeServer.ReadBoundedLineAsync(server, 8192, timeout.Token);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes("{\"type\":\"hello\",\"protocol\":1,\"token\":\"test\"}\n"), timeout.Token);
        _ = await hello;
        var ack = PipeBridgeServer.ReadBoundedLineAsync(pipe, 8192, timeout.Token);
        await server.WriteAsync(Encoding.UTF8.GetBytes("{\"type\":\"hello-ack\",\"protocol\":1}\n"));
        _ = await ack;
        // Start the server read before calling the real client, so this fixture
        // remains concurrent like the production server on both platforms.
        var incoming = PipeBridgeServer.ReadBoundedLineAsync(server, 8192, timeout.Token);
        var request = client.RequestAsync("get-status", new { }, timeout.Token);
        _ = await incoming;
        await server.WriteAsync(Encoding.UTF8.GetBytes("{\"id\":\"wrong\",\"error\":{\"code\":\"internal_error\",\"message\":\"x\"}}\n"));
        await Check.ThrowsAsync<BridgeProtocolException>(() => request);
    }

    public static async Task ExactFrameLimitExcludesNewline()
    {
        const int max = 256;
        await using var server = TestServer.Create(maxFrameBytes: max);
        await server.StartAsync();
        await using var client = await RawClient.ConnectAsync(server.PipeName);
        await client.SendAsync($"{{\"type\":\"hello\",\"protocol\":1,\"token\":\"{server.Token}\"}}\n");
        _ = await client.ReadAsync();
        var prefix = "{\"id\":\"x\",\"method\":\"get-status\",\"params\":{},\"padding\":\"";
        var suffix = "\"}";
        var padding = new string('x', max - Encoding.UTF8.GetByteCount(prefix) - Encoding.UTF8.GetByteCount(suffix));
        await client.SendAsync(prefix + padding + suffix + "\n");
        var response = await client.ReadAsync();
        Check.Equal("x", response.GetProperty("id").GetString());
        await server.StopAsync();
    }

    public static async Task IdlePeerDoesNotBlockNextPeer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = TestServer.Create(readTimeout: TimeSpan.FromMilliseconds(100));
        await server.StartAsync();
        await using var idle = await RawClient.ConnectAsync(server.PipeName);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        await using var client = await AuthenticatedPipeClient.ConnectAsync(server.PipeName, server.Token, TimeSpan.FromSeconds(2));
        var response = await client.RequestAsync("get-status", new { }, timeout.Token);
        Check.Equal("ok-1", response.GetProperty("id").GetString());
        await server.StopAsync();
    }

    private sealed class RawClient : IAsyncDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private RawClient(NamedPipeClientStream pipe) => _pipe = pipe;
        public static async Task<RawClient> ConnectAsync(string pipeName)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(2000);
            return new RawClient(pipe);
        }
        public async Task SendAsync(string text) => await _pipe.WriteAsync(Encoding.UTF8.GetBytes(text));
        public async Task<JsonElement> ReadAsync()
        {
            var bytes = new List<byte>();
            var one = new byte[1];
            while (true)
            {
                var read = await _pipe.ReadAsync(one);
                if (read == 0) throw new IOException("pipe closed");
                if (one[0] == (byte)'\n') break;
                bytes.Add(one[0]);
            }
            using var document = JsonDocument.Parse(bytes.ToArray());
            return document.RootElement.Clone();
        }
        public ValueTask DisposeAsync() => _pipe.DisposeAsync();
    }

    private sealed class TestServer : IAsyncDisposable
    {
        private readonly PipeBridgeServer _server;
        private readonly CancellationTokenSource _stop = new();
        private Task? _run;
        public string PipeName { get; } = $"engineering-safety-{Guid.NewGuid():N}";
        public string Token { get; } = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        private TestServer(PipeBridgeServer server) => _server = server;
        public static TestServer Create(IBridgeReadService? service = null, int maxFrameBytes = 8192, TimeSpan? readTimeout = null) =>
            new(new PipeBridgeServer(service ?? new SyntheticReadService(), maxFrameBytes, readTimeout));
        public Task StartAsync() { _run = _server.RunAsync(PipeName, Token, _stop.Token); return Task.CompletedTask; }
        public async Task StopAsync() { _stop.Cancel(); if (_run is not null) await _run; }
        public ValueTask DisposeAsync() { _stop.Cancel(); _server.Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class SyntheticReadService : IBridgeReadService
    {
        public Task<object> GetContextAsync(JsonElement _, CancellationToken __) => Task.FromResult<object>(new { context = new { binding = new { ideInstanceId = "ide-test", solutionId = "solution-test", projectId = "project-test" } }, readOnly = true });
        public Task<object> GetStatusAsync(JsonElement _, CancellationToken __) => Task.FromResult<object>(new { available = true, status = "ready", readOnly = true });
        public Task<object> ReadTargetAsync(JsonElement _, CancellationToken __) => Task.FromResult<object>(new { objectId = "FB_Main", declaration = "", implementation = "", readOnly = true });
    }
}

internal sealed class TestAdapter : IEngineeringAdapter
{
    public EngineeringSnapshot Current { get; set; }
    public EngineeringCapability Capabilities { get; }
    public List<string> Writes { get; } = [];
    public Exception? FailDeclaration { get; set; }
    public Exception? FailImplementation { get; set; }
    public bool CorruptReadback { get; set; }
    public bool FailReadback { get; set; }
    public EngineeringCapability? CapabilitiesOverride { get; set; }
    public Action? OnDeclarationAttempt { get; set; }
    public TimeSpan WriteDelay { get; set; }
    public TestAdapter(EngineeringSnapshot current, EngineeringCapability? capabilities = null)
    {
        Current = current;
        Capabilities = capabilities ?? (EngineeringCapability.Read | EngineeringCapability.WriteDeclaration | EngineeringCapability.WriteImplementation | EngineeringCapability.DirtyState | EngineeringCapability.Revision);
    }
    public Task<EngineeringCapabilities> GetCapabilitiesAsync(CancellationToken _) => Task.FromResult(new EngineeringCapabilities(CapabilitiesOverride ?? Capabilities));
    public Task<BindingIdentity?> GetCurrentBindingAsync(CancellationToken _) => Task.FromResult<BindingIdentity?>(Current.Target.Binding);
    public Task<EngineeringSnapshot> ReadTargetAsync(TargetIdentity _, CancellationToken __)
    {
        if (FailReadback) throw new IOException("readback unavailable");
        var result = CorruptReadback && Writes.Count > 0 ? Current with { Implementation = Current.Implementation + " [corrupt]" } : Current;
        return Task.FromResult(result);
    }
    public async Task ApplyDeclarationAsync(TargetIdentity _, string declaration, CancellationToken cancellationToken)
    {
        await Task.Delay(WriteDelay, cancellationToken);
        OnDeclarationAttempt?.Invoke();
        if (FailDeclaration is not null) throw FailDeclaration;
        Current = Current with { Declaration = declaration, Revision = Current.Revision + "-d" };
        Writes.Add("declaration");
    }
    public async Task ApplyImplementationAsync(TargetIdentity _, string implementation, CancellationToken cancellationToken)
    {
        await Task.Delay(WriteDelay, cancellationToken);
        if (FailImplementation is not null) throw FailImplementation;
        Current = Current with { Implementation = implementation, Revision = Current.Revision + "-i" };
        Writes.Add("implementation");
    }
}

internal sealed class TestStore : IEngineeringStore
{
    public List<BackupRecord> Backups { get; } = [];
    public bool ThrowOnBackup { get; set; }
    public Action? OnBackup { get; set; }
    public bool ThrowOnAudit { get; set; }
    public Task PersistBackupAsync(BackupRecord backup, CancellationToken _)
    {
        if (ThrowOnBackup) throw new IOException("backup failed");
        Backups.Add(backup);
        OnBackup?.Invoke();
        return Task.CompletedTask;
    }
    public Task AppendAuditAsync(AuditRecord _, CancellationToken __)
    {
        if (ThrowOnAudit) throw new IOException("audit failed");
        return Task.CompletedTask;
    }
}

internal sealed class ManualClock : IEngineeringClock
{
    public DateTimeOffset Now { get; private set; } = DateTimeOffset.UtcNow;
    public void Advance(TimeSpan amount) => Now += amount;
}

internal static class Check
{
    public static void True(bool value, string message = "assertion failed") { if (!value) throw new InvalidOperationException(message); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"expected {expected}, got {actual}"); }
    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException error) { return error; }
        throw new InvalidOperationException($"expected {typeof(TException).Name}");
    }
}
