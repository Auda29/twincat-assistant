using System;
using System.Collections.Generic;
namespace TwinCatAssistant.Native
{
    public static partial class HostText
    {
        // Exact messages from the read-only editor contracts. Only status fields
        // call this method; source text and conversation content do not.
        private static readonly Dictionary<string, string> StatusKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "TwinCAT 4026 in the 32-bit shell has no Assistant acceptance.", "Host4026" },
            { "TwinCAT 3.1 build was not identified.", "HostBuildUnknown" },
            { "Alpha target host; open acceptance gates remain open.", "HostAlpha" },
            { "TcXaeShell64 has no validated payload target or acceptance.", "Host64" },
            { "No payload targets Visual Studio 2026.", "Host2026" },
            { "TwinCAT in full Visual Studio has no Assistant acceptance.", "HostVS" },
            { "Host is not a known TwinCAT engineering host.", "HostUnrecognized" },
            { "Unsaved editor text is not verified.", "SourceUnverified" },
            { "No source context bound.", "SourceNoContext" },
            { "No active ST POU", "SourceNoObject" },
            { "Open a Structured Text POU in this IDE.", "SourceOpenObject" },
            { "The matching IDE document reports unsaved changes.", "DirtyDocument" },
            { "Live engineering text differs from the saved object.", "DirtyDifference" },
            { "Exact saved-source or document correlation is unavailable.", "DirtyUnavailable" },
            { "The exact running document reports clean and live text matches saved text.", "DirtyClean" },
            { "No authoritative dirty-state evidence from the exact running document.", "DirtyUnknown" },
            { "Resolving the active editor...", "EditorResolving" },
            { "Updating active editor context...", "EditorUpdating" },
            { "The active editor changed. Resolving context...", "EditorChanged" },
            { "Active editor context is unavailable. It will retry automatically.", "EditorUnavailable" },
            { "PLC project context is unavailable.", "ProjectUnavailable" },
            { "Native editor services are unavailable.", "EditorServices" },
            { "This IDE has no open solution.", "SolutionMissing" },
            { "The active document frame and TwinCAT document do not match.", "EditorMismatch" },
            { "The active editor has no exact TwinCAT object binding.", "EditorUnbound" },
            { "The frame and document identify different POU members.", "MemberMismatch" },
            { "The active POU member could not be resolved exactly.", "MemberUnresolved" },
            { "The active implementation is not readable Structured Text.", "SourceNotST" },
            { "Select one PLC project in this IDE. No unambiguous project is bound.", "ProjectAmbiguous" },
            { "No PLC project is bound.", "ProjectUnbound" },
            { "Select a PLC project; automatic project discovery is incomplete.", "ProjectDiscovery" },
            { "The active editor changed.", "EditorChange" },
            { "The active object exceeds the local source limit.", "SourceLimit" },
            { "The active object changed during the read. Retrying automatically.", "SourceChanged" },
            { "The active object is not a supported POU, action, method or property.", "ObjectUnsupported" },
            { "The active member has no exact owning POU.", "OwnerMissing" },
            { "The active member belongs to another POU.", "OwnerMismatch" },
            { "The active object has no readable declaration.", "DeclarationMissing" },
            { "The property accessor list is incomplete.", "AccessorIncomplete" },
            { "The property accessor identity is ambiguous.", "AccessorAmbiguous" },
            { "The active document frame has no exact document identity.", "FrameUnbound" },
        };
        public static string DisplayStatus(string value)
        {
            if (value == null || Description(value) != null) return value;
            if (StatusKeys.TryGetValue(value, out var key)) return Get(key);
            const string prefix = "Unsaved editor text is not verified. ";
            if (value.StartsWith(prefix, StringComparison.Ordinal)) return Format("SourceState", Get("SourceUnverified"), DisplayStatus(value.Substring(prefix.Length)));
            if (value.StartsWith("Shell executable version ", StringComparison.Ordinal) && value.EndsWith(" differs from the alpha target.", StringComparison.Ordinal))
                return Format("HostVersionMismatch", value.Substring("Shell executable version ".Length, value.Length - "Shell executable version ".Length - " differs from the alpha target.".Length));
            if (value.StartsWith("TwinCAT ", StringComparison.Ordinal) && value.EndsWith(" is not a verified build of the alpha target.", StringComparison.Ordinal))
                return Format("HostUnverifiedBuild", value.Substring("TwinCAT ".Length, value.Length - "TwinCAT ".Length - " is not a verified build of the alpha target.".Length));
            return value;
        }
    }
}
