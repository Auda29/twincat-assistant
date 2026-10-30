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
        foreach (var separator in new[] { ".", "/", "^", "\\" })
            Check(ActiveEditorMemberPath.Parse("FB_Motor.TcPOU;FB_Motor" + separator + "Speed" + separator + "Get", "FB_Motor")
                .SequenceEqual(new[] { "Speed", "Get" }), "exact property accessor moniker separator " + separator);
        foreach (var suffix in new[] { "Other.Reset", "FB_Motor..Reset", "FB_Motor.Reset.Extra.Invalid", "FB_Motor.Reset;Other", "FB_Motor/../Reset" })
        {
            var rejected = false;
            try { ActiveEditorMemberPath.Parse("FB_Motor.TcPOU;" + suffix, "FB_Motor"); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "ambiguous or foreign moniker rejected " + suffix);
        }
        const string memberId = "269aabf6-2f22-4e25-a2aa-7e780a5a8110";
        var action = $"<Action Name=\"Reset\" Id=\"{{{memberId}}}\"><Implementation><ST>bRun := FALSE;</ST></Implementation></Action>";
        var method = action.Replace("Action", "Method").Replace("<Implementation>", "<Declaration>METHOD Reset</Declaration><Implementation>");
        var property = $"<Property Name=\"Speed\" Id=\"{{{memberId}}}\"><Declaration>PROPERTY Speed : INT</Declaration>"
            + "<Get Name=\"Get\" Id=\"{269aabf6-2f22-4e25-a2aa-7e780a5a8120}\"><Declaration>VAR x : INT; END_VAR</Declaration><Implementation><ST>Speed := x;</ST></Implementation></Get>"
            + "<Set Name=\"Set\" Id=\"{269aabf6-2f22-4e25-a2aa-7e780a5a8130}\"><Declaration></Declaration><Implementation><ST>x := Speed;</ST></Implementation></Set></Property>";
        string MemberXml(string member) => Xml().Replace("</POU>", member + "</POU>");
        var actionSource = ActivePouSource.Parse(MemberXml(action), new[] { "Reset" });
        Check(actionSource?.Kind == "Action" && actionSource.Declaration == "" && actionSource.Implementation == "bRun := FALSE;", "action body read without substituting the POU declaration");
        var methodSource = ActivePouSource.Parse(MemberXml(method), new[] { "Reset" });
        Check(methodSource?.Kind == "Method" && methodSource.Declaration == "METHOD Reset", "method has its own declaration and identity");
        var propertySource = ActivePouSource.Parse(MemberXml(property), new[] { "Speed" });
        Check(propertySource?.Kind == "Property" && propertySource.Implementation.Contains("// GET declaration\nVAR x")
            && propertySource.Implementation.Contains("// SET implementation\nx := Speed;"), "property preserves both labelled accessor bodies and local declarations");
        var getter = ActivePouSource.Parse(MemberXml(property), new[] { "Speed", "Get" });
        Check(getter?.Kind == "Get" && getter.Name == "Get" && getter.Implementation == "Speed := x;", "selected getter excludes the setter and parent body");
        Check(ActivePouSource.Parse(MemberXml(action), new[] { "Missing" }) == null, "missing member never falls back to the parent body");
        Check(ActivePouSource.Parse(MemberXml(action + action), new[] { "Reset" }) == null, "duplicate member names are ambiguous");
        var foreignOwner = new DirtyEvidence { PersistedDocumentDirty = false };
        ActivePouSource.Correlate(foreignOwner, actionSource,
            ActivePouSource.Parse(MemberXml(action).Replace(id, "269aabf6-2f22-4e25-a2aa-7e780a5a8199"), new[] { "Reset" }), "Reset", "", actionSource.Implementation);
        Check(DirtyAssessment.Evaluate(foreignOwner).State == "unknown", "member UUID cannot correlate across a different owner UUID");
        Check(ActiveEditorPresentation.BadgeKind(new() { ObjectKind = "Action" }) == "ACT"
            && ActiveEditorPresentation.BadgeKind(new() { ObjectKind = "Get" }) == "GET", "member badge does not depend on a POU declaration");
        return count;
    }
}
