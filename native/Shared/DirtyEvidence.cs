using System;

namespace TwinCatAssistant.Native
{
    public sealed class DirtyEvidence
    {
        public bool? DocumentSaved { get; set; }
        public bool? PersistedDocumentDirty { get; set; }
        public bool SavedSourceAvailable { get; set; }
        public bool LiveMatchesSaved { get; set; }
        public bool EvidenceFailed { get; set; }
    }

    public sealed class DirtyAssessment
    {
        public string State { get; set; }
        public string Reason { get; set; }
        public bool WritesAllowed { get { return false; } }

        public static DirtyAssessment Evaluate(DirtyEvidence evidence)
        {
            if (evidence.PersistedDocumentDirty == true || evidence.DocumentSaved == false)
                return Result("known-dirty", "The matching IDE document reports unsaved changes.");
            if (evidence.SavedSourceAvailable && !evidence.LiveMatchesSaved)
                return Result("known-dirty", "Live engineering text differs from the saved object.");
            if (evidence.EvidenceFailed || !evidence.SavedSourceAvailable)
                return Result("unknown", "Exact saved-source or document correlation is unavailable.");
            if (evidence.PersistedDocumentDirty == false && evidence.LiveMatchesSaved)
                return Result("known-clean", "The exact running document reports clean and live text matches saved text.");
            return Result("unknown", "No authoritative dirty-state evidence from the exact running document.");
        }

        private static DirtyAssessment Result(string state, string reason)
        {
            return new DirtyAssessment { State = state, Reason = reason };
        }
    }
}
