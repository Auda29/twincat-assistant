using System.Xml;
using TwinCatAssistant.Native;

internal static class ActiveEditorTests
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool result, string name)
        {
            if (!result) throw new InvalidOperationException("Active editor regression failed: " + name);
            Console.WriteLine("PASS " + name);
            count++;
        }
        ActiveEditorIdentity Identity(int pid = 42, string project = "A.plcproj", string moniker = "FB_Motor.TcPOU",
            string tree = "TIPC^A^A Project^POUs^FB_Motor", string solution = "Test.sln")
            => new(pid, solution, project, moniker, tree);
        ActiveEditorSnapshot Snapshot(ActiveEditorIdentity id) => new() {
            Identity = id, ObjectName = "FB_Motor", Declaration = "FUNCTION_BLOCK FB_Motor", Implementation = "bRun := FALSE;",
            Dirty = DirtyAssessment.Evaluate(new DirtyEvidence()), SourceKind = "Automation Interface snapshot",
            SourceNotice = "Unsaved editor text is not verified."
        };

        var original = Identity();
        foreach (var other in new[] { Identity(pid: 43), Identity(project: "B.plcproj"),
            Identity(moniker: "FB_Motor.TcPOU;FB_Motor.Method"), Identity(tree: "TIPC^B^B Project^POUs^FB_Motor"),
            Identity(solution: "Other.sln") })
            Check(!original.Equals(other), "identity rejects a different IDE/project/document/tree/solution " + count);
        Check(original.Equals(Identity()), "stable identity equality");
        try { _ = new ActiveEditorIdentity(42, "", "A", "B", "C"); Check(false, "incomplete identity"); }
        catch (ArgumentException) { Check(true, "incomplete identity rejected"); }

        var gate = new ActiveEditorPublication();
        var generation = gate.Generation;
        Check(gate.TryPublish(generation, Snapshot(original), original), "exact current snapshot publishes");
        gate.Invalidate();
        Check(gate.Current == null, "invalidation clears source immediately");
        Check(!gate.TryPublish(generation, Snapshot(original), original), "late read after close and same-path reopen rejected");
        generation = gate.Generation;
        Check(!gate.TryPublish(generation, Snapshot(original), Identity(project: "B.plcproj")), "changed object at publication rejected");
        gate.Invalidate(); // edit arriving during a COM call
        Check(!gate.TryPublish(generation, Snapshot(original), original), "reentrant edit invalidates in-flight result");
        Check(gate.TryPublish(gate.Generation, Snapshot(original), original), "fresh read after invalidation succeeds");
        gate.Dispose();
        Check(gate.Current == null && !gate.TryPublish(gate.Generation, Snapshot(original), original), "disposed tracker cannot publish");

        var unavailable = ActiveEditorPresentation.From(null, "Unsupported editor");
        Check(unavailable.Source == "" && unavailable.Scope == "Unsupported editor", "unsupported editor has no stale source");
        var view = ActiveEditorPresentation.From(Snapshot(original));
        Check(view.Evidence.Contains("unknown") && view.Detail.Contains("not verified")
            && view.Evidence.Contains("Automation Interface snapshot"), "unknown state and source provenance remain explicit");
        Check(ActiveEditorPresentation.Disclosure.Contains("No source attached"), "local disclosure status remains explicit");
        var dirtySnapshot = Snapshot(original);
        dirtySnapshot.Dirty = DirtyAssessment.Evaluate(new DirtyEvidence { DocumentSaved = false });
        Check(ActiveEditorPresentation.From(dirtySnapshot).Evidence.Contains("unsaved changes"), "unsaved status is visible");

        const string id = "269aabf6-2f22-4e25-a2aa-7e780a5a8100";
        string Xml(string uuid = id, string name = "FB_Motor", string body = "bRun := FALSE;") =>
            $"<TcPlcObject><POU Name=\"{name}\" Id=\"{{{uuid}}}\"><Declaration><![CDATA[FUNCTION_BLOCK FB_Motor]]></Declaration>"
            + $"<Implementation><ST><![CDATA[{body}]]></ST></Implementation></POU></TcPlcObject>";
        var source = ActivePouSource.Parse(Xml());
        var equal = new DirtyEvidence { PersistedDocumentDirty = false };
        ActivePouSource.Correlate(equal, source, ActivePouSource.Parse(Xml()), "FB_Motor", source.Declaration, source.Implementation);
        Check(DirtyAssessment.Evaluate(equal).State == "known-clean", "exact GUID/name/content and RDT clean correlate");
        foreach (var mismatch in new[] { Xml(uuid: "269aabf6-2f22-4e25-a2aa-7e780a5a8101"), Xml(name: "Other") })
        {
            var evidence = new DirtyEvidence { PersistedDocumentDirty = false };
            ActivePouSource.Correlate(evidence, source, ActivePouSource.Parse(mismatch), "FB_Motor", source.Declaration, source.Implementation);
            Check(DirtyAssessment.Evaluate(evidence).State == "unknown", "same-name or wrong-object source stays uncorrelated " + count);
        }
        var changed = new DirtyEvidence { PersistedDocumentDirty = false };
        ActivePouSource.Correlate(changed, source, ActivePouSource.Parse(Xml(body: "bRun := TRUE;")), "FB_Motor", source.Declaration, source.Implementation);
        Check(DirtyAssessment.Evaluate(changed).State == "known-dirty", "correlated text mismatch overrides clean RDT");
        Check(ActivePouSource.Parse(Xml().Replace("</TcPlcObject>", Xml() + "</TcPlcObject>")) != null,
            "nested unrelated object is not selected as the direct POU");
        var duplicate = Xml().Replace("</TcPlcObject>", Xml().Replace("<TcPlcObject>", "").Replace("</TcPlcObject>", "") + "</TcPlcObject>");
        Check(ActivePouSource.Parse(duplicate) == null, "multiple direct POUs rejected");
        Check(ActivePouSource.Parse(Xml().Replace("<ST>", "<IL>").Replace("</ST>", "</IL>")) == null, "non-ST source rejected");
        Check(ActivePouSource.Parse(Xml(uuid: "not-a-guid")) == null, "missing exact UUID rejected");
        Check(ActivePouSource.Parse(new string('x', 1048577)) == null, "oversized XML rejected before parsing");
        try { ActivePouSource.Parse("<!DOCTYPE TcPlcObject [<!ENTITY x SYSTEM 'file:///private'>]>" + Xml()); Check(false, "DTD rejection"); }
        catch (XmlException) { Check(true, "DTD and external entities rejected"); }
        return count;
    }
}
