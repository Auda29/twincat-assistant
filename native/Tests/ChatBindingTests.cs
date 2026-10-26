using TwinCatAssistant.Native;

internal static class ChatBindingTests
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool result, string name) { if (!result) throw new InvalidOperationException(name); count++; Console.WriteLine("PASS " + name); }
        var first = new ActiveEditorIdentity(10, "C:/Test/Context.sln", "C:/Test/PLC_A.plcproj", "C:/Test/FB_Motor.TcPOU", "TIPC/PLC_A/POUs/FB_Motor");
        var second = new ActiveEditorIdentity(10, first.SolutionFile, "C:/Test/PLC_B.plcproj", first.DocumentMoniker, first.TreePath);
        var otherIde = new ActiveEditorIdentity(11, first.SolutionFile, first.ProjectFile, first.DocumentMoniker, first.TreePath);
        var binding = ChatContextBinding.Create(first, "instance-one", "http://127.0.0.1:3123/", "ses_one");
        Check(binding.Matches(first, "instance-one", binding.Origin, "ses_one"), "exact IDE/project/thread binding accepted");
        Check(!binding.Matches(second, "instance-one", binding.Origin, "ses_one"), "same-name POU in another PLC cannot reuse chat binding");
        Check(!binding.Matches(otherIde, "instance-one", binding.Origin, "ses_one"), "another IDE cannot reuse chat binding");
        Check(!binding.Matches(first, "reopened-instance", binding.Origin, "ses_one"), "PID reuse or a new host lifetime requires explicit rebinding");
        Check(!binding.Matches(first, "instance-one", binding.Origin, "ses_two"), "another session cannot reuse binding");
        var general = ChatContextBinding.CreateGeneral("instance-one", 10, binding.Origin, "ses_general");
        Check(general.MatchesGeneral("instance-one", 10, binding.Origin, "ses_general"), "general chat remains usable without a POU identity");
        Check(!general.Matches(first, "instance-one", binding.Origin, "ses_general"), "general chat cannot authorize native POU source attachment");
        Check(!general.MatchesGeneral("another-instance", 10, binding.Origin, "ses_general"), "general chat still has exact host-lifetime ownership");
        var snapshot = new ActiveEditorSnapshot { Identity = first, ObjectName = "FB_Motor", Declaration = "VAR\nEND_VAR", Implementation = "bRun := TRUE;", SourceKind = "Automation Interface snapshot" };
        var revision = ChatContextPolicy.Revision(snapshot);
        snapshot.Implementation += "\n// unsaved";
        Check(revision != ChatContextPolicy.Revision(snapshot), "source edits invalidate disclosure revision");
        revision = ChatContextPolicy.Revision(snapshot);
        snapshot.ContextGeneration++;
        Check(revision != ChatContextPolicy.Revision(snapshot), "same-path reopen invalidates pending disclosure even with unchanged source");
        Check(ChatContextPolicy.AttachmentText(snapshot).Contains("Unsaved editor text is not verified"), "chat payload preserves editor-buffer uncertainty");
        Check(ChatContextPolicy.ReferenceId(snapshot).StartsWith("FB_Motor@PLC_A#") && ChatContextPolicy.ReferenceId(snapshot).Length <= 128, "composer chip starts with the POU/project name instead of an opaque hash");
        ChatProviderChoice Provider(string id) => new ChatProviderChoice { id = id, name = id };
        Check(ChatContextPolicy.ProviderToEnable(new ChatSelection { providers = new ChatProviderChoice[0] }) == null, "no provider is enabled before one is connected in OpenChamber");
        Check(ChatContextPolicy.ProviderToEnable(new ChatSelection { providers = new[] { Provider("openai") } }) == "openai", "the single connected provider is enabled automatically");
        Check(ChatContextPolicy.ProviderToEnable(new ChatSelection { providers = new[] { Provider("anthropic"), Provider("openai") }, allowedProviders = new[] { "openai" } }) == null, "an enabled connected provider is never switched automatically");
        Check(ChatContextPolicy.ProviderToEnable(new ChatSelection { providers = new[] { Provider("anthropic"), Provider("openai") }, model = "openai/gpt-x" }) == "openai", "the provider of the chosen model is preferred");
        Check(ChatContextPolicy.ProviderToEnable(new ChatSelection { providers = new[] { Provider("openai") }, allowedProviders = new[] { "disconnected" } }) == "openai", "a disconnected enabled provider is replaced by a connected one");
        Check(ChatContextPolicy.ProviderToEnable(new ChatSelection { providers = new[] { Provider("bad id!") } }) == null, "provider IDs outside the policy alphabet are ignored");
        Check(ActiveEditorPresentation.Kind("FUNCTION_BLOCK FB_Motor") == "FB" && ActiveEditorPresentation.Kind("  FUNCTION F_Add : INT") == "FUN"
            && ActiveEditorPresentation.Kind("PROGRAM MAIN") == "PRG" && ActiveEditorPresentation.Kind("FUNCTION_BLOCKX") == "ST"
            && ActiveEditorPresentation.Kind(null) == "ST", "context badge derives the POU kind from the declaration keyword");
        snapshot.Implementation = new string('x', 16000);
        var refused = false;
        try { ChatContextPolicy.AttachmentText(snapshot); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "oversized source is refused without truncation");
        Check(ChatContextPolicy.IsPromptRequest("POST", new Uri("http://127.0.0.1:3123/api/session/ses_one/message"), out var session) && session == "ses_one", "prompt route is identified for native binding enforcement");
        Check(!ChatContextPolicy.IsPromptRequest("GET", new Uri("http://127.0.0.1:3123/api/session/ses_one/message"), out _), "read-only message history is not treated as a prompt");
        Check(ChatContextPolicy.IsPromptRequest("POST", new Uri("http://127.0.0.1:3123/API/session/ses_one/message/"), out _), "route casing and trailing slash cannot bypass binding checks");
        Check(ChatContextPolicy.BlocksMutatingFeature("POST", new Uri("http://127.0.0.1:3123/api/session/ses_one/shell")), "direct user shell endpoint is blocked even for a bound session");
        Check(ChatContextPolicy.BlocksMutatingFeature("PUT", new Uri("http://127.0.0.1:3123/api/fs/file")), "general file writes are blocked by the embedded host");
        Check(!ChatContextPolicy.BlocksMutatingFeature("POST", new Uri("http://127.0.0.1:3123/auth/session")), "protected login remains available");
        Check(!ChatContextPolicy.BlocksMutatingFeature("DELETE", new Uri("http://127.0.0.1:3123/api/debug/location")), "explicit profile policy reload remains available");
        return count;
    }
}
