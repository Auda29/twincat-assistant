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
        var profile = Path.GetFullPath("selection-test-profile");
        Check(ChatContextPolicy.ProjectSelectionKey(profile, first.ProjectIdentity) == ChatContextPolicy.ProjectSelectionKey(profile, otherIde.ProjectIdentity), "last selected project chat survives IDE lifetime and PID changes");
        Check(ChatContextPolicy.ProjectSelectionKey(profile, first.ProjectIdentity) != ChatContextPolicy.ProjectSelectionKey(profile, second.ProjectIdentity), "last selected chat is separate for each PLC project");
        Check(ChatContextPolicy.ProjectSelectionKey(profile, first.ProjectIdentity) != ChatContextPolicy.ProjectSelectionKey(profile + "-other", first.ProjectIdentity), "last selected chat is separate for each operator profile");
        var binding = ChatContextBinding.Create(first, "instance-one", "http://127.0.0.1:3123/", "ses_one");
        Check(binding.Matches(first, "instance-one", binding.Origin, "ses_one"), "exact IDE/project/thread binding accepted");
        Check(!binding.Matches(second, "instance-one", binding.Origin, "ses_one"), "same-name POU in another PLC cannot reuse chat binding");
        Check(!binding.Matches(otherIde, "instance-one", binding.Origin, "ses_one"), "another IDE cannot reuse chat binding");
        Check(!binding.Matches(first, "reopened-instance", binding.Origin, "ses_one"), "PID reuse or a new host lifetime requires explicit rebinding");
        Check(!binding.Matches(first, "instance-one", binding.Origin, "ses_two"), "another session cannot reuse binding");
        var projectOnly = ChatContextBinding.CreateProject(first.ProjectIdentity, "instance-one", binding.Origin, "ses_project");
        Check(projectOnly.MatchesProject(first.ProjectIdentity, "instance-one", binding.Origin, "ses_project"), "project binding works without any POU identity");
        Check(!projectOnly.MatchesProject(second.ProjectIdentity, "instance-one", binding.Origin, "ses_project"), "project-only binding rejects a different PLC");
        Check(!projectOnly.MatchesProject(otherIde.ProjectIdentity, "instance-one", binding.Origin, "ses_project"), "project-only binding rejects another IDE");
        Check(!projectOnly.MatchesProject(null, "instance-one", binding.Origin, "ses_project"), "lost project context cannot authorize a project chat");
        var general = ChatContextBinding.CreateGeneral("instance-one", 10, binding.Origin, "ses_general");
        Check(general.MatchesGeneral("instance-one", 10, binding.Origin, "ses_general"), "general chat remains usable without a POU identity");
        Check(!general.Matches(first, "instance-one", binding.Origin, "ses_general"), "general chat cannot authorize native POU source attachment");
        Check(!general.MatchesGeneral("another-instance", 10, binding.Origin, "ses_general"), "general chat still has exact host-lifetime ownership");
        var snapshot = new ActiveEditorSnapshot { Identity = first, ObjectName = "FB_Motor", Declaration = "VAR\nEND_VAR", Implementation = "bRun := TRUE;", SourceKind = "Automation Interface snapshot" };
        var revision = ChatContextPolicy.Revision(snapshot);
        var automatic = new AutoAttachmentAttempts();
        var now = DateTime.UtcNow;
        var autoKey = "chat-one\0page-one\0" + revision;
        Check(automatic.CanAttempt(autoKey, now), "automatic PRG/POU attachment starts without a prior successful attempt");
        automatic.Begin(autoKey, now);
        Check(!automatic.CanAttempt(autoKey, now.AddMilliseconds(500)) && automatic.CanAttempt(autoKey, now.AddSeconds(1)), "a failed automatic snapshot read retries the same revision with a delay");
        automatic.Begin(autoKey, now.AddSeconds(1)); automatic.Confirm(autoKey);
        Check(!automatic.CanAttempt(autoKey, now.AddMinutes(1)), "confirmed automatic attachment respects removing the chip in the same context");
        Check(automatic.CanAttempt("chat-one\0page-two\0" + revision, now), "a reloaded composer can stage the same revision again");
        automatic.Reset();
        for (var attempt = 0; attempt < 3; attempt++) automatic.Begin(autoKey, now.AddSeconds(attempt));
        Check(automatic.Exhausted(autoKey) && !automatic.CanAttempt(autoKey, now.AddMinutes(1)), "automatic attachment stops after three failed reads instead of retrying forever");
        automatic.Reset();
        Check(automatic.CanAttempt(autoKey, now), "explicit retry or restored context clears the failed attachment budget");
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
        snapshot.Implementation = "";
        var overhead = ChatContextPolicy.AttachmentText(snapshot).Length;
        snapshot.Implementation = new string('x', ChatContextPolicy.DirectAttachmentLimit - overhead);
        var direct = ChatContextPolicy.CreateAttachment(snapshot);
        Check(!direct.IsSavedReference && direct.Text.Length == ChatContextPolicy.DirectAttachmentLimit
            && direct.Text.EndsWith(snapshot.Implementation), "source exactly at the limit remains a complete direct snapshot");
        snapshot.Implementation += "x";
        var savedReference = ChatContextPolicy.CreateAttachment(snapshot);
        Check(savedReference.IsSavedReference && savedReference.Text.Length < ChatContextPolicy.DirectAttachmentLimit
            && !savedReference.Text.Contains(snapshot.Implementation) && !savedReference.Text.Contains("Declaration:\n"),
            "oversized source becomes a bounded reference without partial source");
        Check(savedReference.Text.Contains(first.SolutionFile) && savedReference.Text.Contains(first.ProjectFile)
            && savedReference.Text.Contains(first.DocumentMoniker) && savedReference.Text.Contains(first.TreePath),
            "saved reference retains exact solution project document and tree identity");
        Check(savedReference.Text.Contains("engineering_plc_list_pous") && savedReference.Text.Contains("engineering_plc_read_pou")
            && savedReference.Text.Contains("returned object ID") && savedReference.Text.Contains("could not be read"),
            "saved reference requests verified MCP reading and reports unavailable or missing objects honestly");
        snapshot.Dirty = new DirtyAssessment { State = "known-dirty" };
        snapshot.OwnerName = "FB_Motor"; snapshot.ObjectName = "Execute";
        snapshot.ObjectKind = "Method"; snapshot.MemberPath = "Execute";
        var memberReference = ChatContextPolicy.CreateAttachment(snapshot);
        Check(memberReference.IsSavedReference && memberReference.Text.Contains("Owning POU: FB_Motor")
            && memberReference.Text.Contains("Member: Execute") && memberReference.Text.Contains("Object kind: Method"),
            "oversized member reference preserves its owner and member instead of falling back to the POU body");
        Check(memberReference.Text.Contains("known-dirty") && memberReference.Text.Contains("Unsaved editor changes are not included")
            && memberReference.Text.Contains("disk state at tool-call time") && memberReference.Text.Contains("Do not save or edit"),
            "dirty reference distinguishes future saved-file read from unsaved or frozen editor content");
        snapshot.Identity = second;
        var secondReference = ChatContextPolicy.CreateAttachment(snapshot);
        Check(secondReference.Text.Contains(second.ProjectFile) && !secondReference.Text.Contains(first.ProjectFile),
            "same-name objects in another project produce a different saved reference");
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
