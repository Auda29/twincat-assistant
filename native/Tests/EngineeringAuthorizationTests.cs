using TwinCatAssistant.Native;

internal static class EngineeringAuthorizationTests
{
    internal static int Run()
    {
        var now = DateTime.UtcNow;
        var binding = new EngineeringBinding(Guid.NewGuid(), 42, now, @"C:\synthetic\a.sln", @"C:\synthetic\a.plcproj", "1");
        var other = new EngineeringBinding(binding.SessionId, 42, now, binding.SolutionFile, binding.ProjectFile, "2");
        var digest = EngineeringAction.HashRequest("{\"operation\":\"build.start\"}");
        var approval = new EngineeringAction(binding, Guid.NewGuid(), "build.start", null, null, null, null, now.AddSeconds(30), digest, null, null);
        var store = new EngineeringAuthorization();
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        Check(!store.Consume(approval, binding, now), "Unapproved operation refused");
        store.Grant(approval, now);
        Check(store.Consume(approval, binding, now), "Exact user-approved operation accepted");
        Check(!store.Consume(approval, binding, now), "Approval cannot be replayed");
        store.Grant(approval, now);
        Check(!store.Consume(approval, other, now), "Same paths with changed epoch refused");
        store.Grant(approval, now);
        Check(!store.Consume(approval, binding, now.AddSeconds(30)), "Expired approval refused");
        store.Grant(approval, now); store.Revoke();
        Check(!store.Consume(approval, binding, now), "Close/rebind revocation refused");
        var apply = new EngineeringAction(binding, Guid.NewGuid(), "source.apply", "POU:A/M:M", "implementation", "preview1", null, now.AddSeconds(30), digest, digest, digest);
        var substituted = new EngineeringAction(binding, apply.OperationId, "source.apply", "POU:B/M:M", "implementation", "preview1", null, apply.ExpiresUtc, digest, digest, digest);
        store.Grant(apply, now);
        Check(!store.Consume(substituted, binding, now), "Identical content in a different member refused");
        Check(!store.Consume(apply, binding, now), "Mismatch consumes pending approval");
        store.Grant(apply, now);
        var changedRequest = new EngineeringAction(binding, apply.OperationId, apply.Operation, apply.TargetObjectId, apply.Section, apply.PreviewId, null, apply.ExpiresUtc, EngineeringAction.HashRequest("different bytes"), digest, digest);
        Check(!store.Consume(changedRequest, binding, now), "Changed request bytes refused");
        store.Grant(apply, now);
        var reopened = new EngineeringBinding(Guid.NewGuid(), binding.IdePid, binding.IdeStartedUtc, binding.SolutionFile, binding.ProjectFile, binding.ContextEpoch);
        Check(!store.Consume(apply, reopened, now), "Same project with new session refused");
        Check(EngineeringAction.HashRequest("é") != EngineeringAction.HashRequest("e"), "Digest binds exact UTF8 bytes");
        try { store.Grant(new EngineeringAction(binding, Guid.NewGuid(), "build.start", null, null, null, null, now.AddMinutes(3), digest, null, null), now); throw new Exception("Overlong approval accepted"); }
        catch (ArgumentException) { count++; }
        EngineeringAction Create(string kind, string target = "POUs^FB_Test", string preview = null,
            string journal = null, string baseline = EngineeringAction.EmptyDigest, string proposed = null)
        {
            return new EngineeringAction(binding, Guid.NewGuid(), "source.create", target, kind, preview, journal,
                now.AddSeconds(30), digest, baseline, proposed ?? digest);
        }
        foreach (var kind in new[] { "fb", "prg", "fun", "method", "action", "property", "dut_struct", "gvl" })
        {
            var creation = Create(kind);
            store.Grant(creation, now);
            Check(store.Consume(creation, binding, now) && !store.Consume(creation, binding, now), "Creation is one-shot for " + kind);
        }
        foreach (var invalid in new Action[] {
            () => Create("dut"), () => Create("fb", target: " "), () => Create("fb", preview: ""),
            () => Create("fb", journal: "journal"), () => Create("fb", baseline: digest), () => Create("fb", proposed: "invalid"),
            () => new EngineeringAction(binding, Guid.NewGuid(), "source.apply", "target", "implementation", "preview", "journal", now, digest, digest, digest),
            () => new EngineeringAction(binding, Guid.NewGuid(), "source.rollback", "target", "implementation", null, "journal", now, digest, digest, digest),
            () => new EngineeringAction(binding, Guid.NewGuid(), "source.rollback", "target", "implementation", "preview", null, now, digest, digest, digest),
        })
        {
            try { invalid(); throw new Exception("Invalid source action accepted"); }
            catch (ArgumentException) { count++; }
        }
        var unresolved = new EngineeringOperationGuard();
        var build = Guid.NewGuid();
        Check(unresolved.TryBegin(build), "First native operation reserves the guard");
        Check(unresolved.HasUnresolved && unresolved.OperationId == build, "Operation remains unresolved until terminal evidence");
        Check(!unresolved.TryBegin(Guid.NewGuid()), "A new session cannot bypass unresolved work");
        Check(!unresolved.Reconcile(Guid.NewGuid()) && unresolved.OperationId == build, "Unrelated terminal evidence cannot clear the guard");
        Check(unresolved.Reconcile(build) && !unresolved.HasUnresolved, "Exact terminal evidence clears the guard");
        Check(unresolved.TryBegin(Guid.NewGuid()), "A later operation can start after reconciliation");
        return count;
    }
}
