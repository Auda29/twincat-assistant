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
        return count;
    }
}
