using TwinCatAssistant.Native;

var cases = new[] {
    ("no evidence", new DirtyEvidence(), "unknown"),
    ("DTE saved alone", new DirtyEvidence { DocumentSaved = true }, "unknown"),
    ("live/saved equality alone", new DirtyEvidence { SavedSourceAvailable = true, LiveMatchesSaved = true }, "unknown"),
    ("DTE unsaved", new DirtyEvidence { DocumentSaved = false }, "known-dirty"),
    ("RDT dirty", new DirtyEvidence { PersistedDocumentDirty = true }, "known-dirty"),
    ("text mismatch", new DirtyEvidence { SavedSourceAvailable = true, LiveMatchesSaved = false }, "known-dirty"),
    ("RDT clean without source", new DirtyEvidence { PersistedDocumentDirty = false }, "unknown"),
    ("RDT clean plus equal exact source", new DirtyEvidence { PersistedDocumentDirty = false, SavedSourceAvailable = true, LiveMatchesSaved = true }, "known-clean"),
    ("failed evidence", new DirtyEvidence { PersistedDocumentDirty = false, SavedSourceAvailable = true, LiveMatchesSaved = true, EvidenceFailed = true }, "unknown"),
    ("dirty overrides failure", new DirtyEvidence { PersistedDocumentDirty = true, EvidenceFailed = true }, "known-dirty"),
    ("contradictory document flags", new DirtyEvidence { DocumentSaved = false, PersistedDocumentDirty = false, SavedSourceAvailable = true, LiveMatchesSaved = true }, "known-dirty"),
    ("mismatch overrides RDT clean", new DirtyEvidence { PersistedDocumentDirty = false, SavedSourceAvailable = true, LiveMatchesSaved = false }, "known-dirty"),
};
foreach (var (name, evidence, expected) in cases)
{
    var result = DirtyAssessment.Evaluate(evidence);
    if (result.State != expected || result.WritesAllowed || string.IsNullOrWhiteSpace(result.Reason))
        throw new InvalidOperationException("Dirty assessment failed: " + name);
    Console.WriteLine("PASS " + name);
}
var activeCases = ActiveEditorTests.Run();
var chatCases = ChatConnectionTests.Run();
var bindingCases = ChatBindingTests.Run();
var viewStateCases = ProjectChatViewStateTests.Run();
var hostCases = HostIdentityTests.Run();
var languageCases = HostTextTests.Run();
Console.WriteLine($"{cases.Length + activeCases + chatCases + bindingCases + viewStateCases + hostCases + languageCases} native policy/context/chat regressions passed (not live IDE/editor acceptance).");
BuildDiagnosticTests.Run();
