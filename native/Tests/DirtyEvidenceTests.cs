using System;
using System.IO;
using Microsoft.VisualStudio;
using TwinCatAssistant.Native;

internal static partial class EngineeringDirtyStateProbe
{
    private static int RunProjectDirtyEvidenceTests(string directory)
    {
        var checks = 0;
        foreach (var extension in new[] { ".sln", ".tsproj", ".plcproj", ".txt" })
        {
            var path = Path.Combine(directory, "Fixture" + extension);
            var hierarchy = new ProjectHierarchy(path, path);
            var solution = new Solution { Hierarchy = hierarchy };
            var document = new object();
            var rdt = new RunningDocuments();
            RunningDocuments.RdtEntry Entry() => new RunningDocuments.RdtEntry
            {
                Path = path, Hierarchy = hierarchy, Document = document,
            };
            var serviceCalls = 0;
            bool? Read()
            {
                serviceCalls = 0;
                return EngineeringDirtyState.GetDirty(rdt, null, _ => { serviceCalls++; return solution; }, path);
            }
            void Check(bool? expected, int expectedServiceCalls, string scenario)
            {
                var actual = Read();
                if (actual != expected || serviceCalls != expectedServiceCalls)
                    throw new InvalidOperationException($"{extension} {scenario}: got {actual?.ToString() ?? "unknown"} with {serviceCalls} fallback calls");
                checks++;
            }
            void CheckEntry(bool? expected, int expectedServiceCalls, string scenario, Action<RunningDocuments.RdtEntry> edit)
            {
                rdt.FindEntry = _ => { var entry = Entry(); edit(entry); return entry; };
                rdt.InfoEntry = rdt.FindEntry;
                Check(expected, expectedServiceCalls, scenario);
            }
            void CheckReplaced(bool? expected, string scenario, Action<RunningDocuments.RdtEntry> edit)
            {
                rdt.FindEntry = _ => { var entry = Entry(); if (serviceCalls > 0) edit(entry); return entry; };
                rdt.InfoEntry = rdt.FindEntry;
                Check(expected, 1, scenario);
            }
            static void SetAbsent(RunningDocuments.RdtEntry entry)
            {
                entry.Cookie = 0;
                entry.Document = null;
                entry.Hierarchy = null;
                entry.Item = VSConstants.VSITEMID_NIL;
            }

            var supportsFallback = extension != ".txt";
            var fallbackCalls = supportsFallback ? 1 : 0;
            CheckEntry(supportsFallback ? false : (bool?)null, fallbackCalls, "clean without IVsPersistDocData", _ => { });
            if (supportsFallback)
            {
                CheckEntry(null, 0, "clean without IVsPersistDocData or a hierarchy", entry => entry.Hierarchy = null);
                CheckEntry(null, 0, "zero cookie and docdata with a hierarchy", entry =>
                {
                    SetAbsent(entry);
                    entry.Hierarchy = hierarchy;
                });
                CheckEntry(null, 0, "zero cookie and docdata with a valid item", entry =>
                {
                    entry.Cookie = 0;
                    entry.Document = null;
                    entry.Hierarchy = null;
                });
            }
            solution.Dirty = true;
            hierarchy.Dirty = 1;
            CheckEntry(supportsFallback ? true : (bool?)null, fallbackCalls, "dirty without IVsPersistDocData", _ => { });
            CheckEntry(null, 0, "lookup failure", entry => entry.Result = VSConstants.E_POINTER);
            CheckEntry(null, 0, "incomplete lookup with no cookie", entry => entry.Cookie = 0);
            CheckEntry(null, 0, "incomplete lookup with no docdata", entry => entry.Document = null);
            rdt.FindEntry = _ => Entry();
            rdt.InfoEntry = _ => { var entry = Entry(); entry.Path += ".other"; return entry; };
            Check(null, 0, "GetDocumentInfo moniker mismatch");
            rdt.InfoEntry = _ => { var entry = Entry(); entry.Item++; return entry; };
            Check(null, 0, "GetDocumentInfo item mismatch");
            var replacement = new object();
            rdt.InfoEntry = _ => { var entry = Entry(); entry.Document = replacement; return entry; };
            Check(null, 0, "GetDocumentInfo docdata mismatch");
            var replacementHierarchy = new ProjectHierarchy(path, path);
            rdt.InfoEntry = _ => { var entry = Entry(); entry.Hierarchy = replacementHierarchy; return entry; };
            Check(null, 0, "GetDocumentInfo hierarchy mismatch");
            rdt.InfoEntry = _ => { var entry = Entry(); entry.Result = VSConstants.E_POINTER; return entry; };
            Check(null, 0, "GetDocumentInfo failure");
            rdt.DirtyResult = VSConstants.E_POINTER;
            CheckEntry(null, 0, "IsDocDataDirty failure", entry => entry.Document = rdt);
            rdt.DirtyResult = VSConstants.S_OK;
            CheckEntry(true, 0, "valid persist docdata observes dirty", entry => entry.Document = rdt);
            if (!supportsFallback) continue;

            CheckEntry(true, 1, "absent RDT retains the fallback", SetAbsent);
            CheckReplaced(null, "changed cookie", entry => entry.Cookie++);
            CheckReplaced(null, "changed item", entry => entry.Item++);
            CheckReplaced(null, "changed docdata", entry => entry.Document = replacement);
            CheckReplaced(null, "changed hierarchy", entry => entry.Hierarchy = replacementHierarchy);
            CheckReplaced(null, "entry disappears", SetAbsent);
            CheckReplaced(null, "later lookup fails", entry => entry.Result = VSConstants.E_POINTER);
            CheckReplaced(true, "later dirty docdata takes precedence", entry => entry.Document = rdt);
            rdt.Dirty = 0;
            CheckReplaced(null, "later clean docdata does not confirm fallback", entry => entry.Document = rdt);
        }
        Console.WriteLine("PASS project RDT dirty-state fallback: " + checks);
        return checks;
    }
}
