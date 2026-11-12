using TwinCatAssistant.Native;

static class BuildDiagnosticTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
        var complete = new BuildCompletionEvidence();
        Check(!complete.CanPublish, "No events cannot succeed");
        complete.Begin(true); Check(!complete.CanPublish, "Begin alone cannot succeed or release results");
        complete.End(true, true); Check(complete.CanPublish && complete.Succeeded, "Matching success completion");
        var failed = new BuildCompletionEvidence(); failed.Begin(true); failed.End(true, false);
        Check(failed.CanPublish && !failed.Succeeded, "Matching compiler failure completion");
        var cancelled = new BuildCompletionEvidence(); cancelled.Begin(true);
        Check(!cancelled.CanPublish, "Cancelled/incomplete run cannot publish");
        var unrelated = new BuildCompletionEvidence(); unrelated.Begin(false); unrelated.End(true, true);
        Check(!unrelated.CanPublish, "Unrelated project cannot publish");
        var duplicate = new BuildCompletionEvidence(); duplicate.Begin(true); duplicate.Begin(true); duplicate.End(true, true);
        Check(!duplicate.CanPublish, "Concurrent or duplicate events rejected");
        var reordered = new BuildCompletionEvidence(); reordered.End(true, true); reordered.Begin(true);
        Check(!reordered.CanPublish, "Completion before begin rejected");
        var messages = BuildDiagnostic.Parse(@"C:\fixture\Main.TcPOU;Main.Method(12,3): error C0001: Invalid token
C:\fixture\Main.TcPOU(2): warning C0100: Unused variable");
        Check(messages.Count == 2 && messages[0].File.EndsWith(";Main.Method") && messages[0].Line == 12 && messages[0].Column == 3 && messages[0].Severity == "error", "Exact member diagnostic");
        Check(BuildDiagnostic.Parse(@"1>C:\fixture\Main.TcPOU(12,3): Fehler C0001: Invalid")[0].Severity == "error", "German build and project prefix");
        Check(messages[1].Column == 1 && messages[1].Severity == "warning", "Optional column");
        Check(BuildDiagnostic.Parse("Main(0,2): error Invalid\nMain(999999999999,2): error Overflow\nMain(3,0): warning Invalid").Count == 0, "Invalid positions");
        Check(BuildDiagnostic.Parse(new string('x', 262145)).Count == 0, "Bounded output");
        Check(BuildDiagnostic.Parse(string.Join("\n", Enumerable.Repeat("Main(1,1): error C1: Error", 500))).Count == 200, "Bounded rows");
        Check(BuildDiagnostic.FreshOutput("old errors\n", "old errors\nnew") == "new", "Old diagnostics excluded");
        Check(BuildDiagnostic.FreshOutput("old", "cleared") == null, "Output replacement unknown");
        Check(BuildDiagnostic.FreshOutput(null, "no baseline") == null, "Missing baseline unknown");
        Check(BuildDiagnostic.FreshOutput("same", "same") == "", "Empty delta");
        var folder = Path.Combine(Environment.CurrentDirectory, "local-data", "build-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var project = Path.Combine(folder, "fixture.plcproj");
        try
        {
            File.WriteAllText(project, "<Project><Import Project='$(MSBuildExtensionsPath)\\Beckhoff\\TwinCAT\\v3.1\\TcPlcProject.targets'/></Project>");
            BuildProjectPolicy.Validate(project); count++;
            foreach (var hook in new[] { "<Target Name='Run' />", "<UsingTask TaskName='Run' />", "<PreBuildEvent>run</PreBuildEvent>", "<PostBuildEvent>run</PostBuildEvent>", "<ProjectReference Include='other.plcproj'/>", "<Import Project='custom.targets'/>" })
            {
                File.WriteAllText(project, "<Project>" + hook + "</Project>");
                var refused = false;
                try { BuildProjectPolicy.Validate(project); } catch (InvalidOperationException) { refused = true; }
                Check(refused, "Custom hook/dependency refused " + hook);
            }
            File.WriteAllText(project, "<Project DefaultTargets='Build'><Import Project='$(MSBuildExtensionsPath)\\Beckhoff\\TwinCAT\\v3.1\\TcPlcProject.targets'/></Project>");
            BuildProjectPolicy.Validate(project); count++;
            File.WriteAllText(Path.Combine(folder, "Directory.Build.props"), "<Project/>");
            var inherited = false;
            try { BuildProjectPolicy.Validate(project); } catch (InvalidOperationException) { inherited = true; }
            Check(inherited, "Inherited hooks refused");
        }
        finally { Directory.Delete(folder, true); }
        Console.WriteLine($"PASS {count} build diagnostics/policy regressions");
        return count;
    }
}
