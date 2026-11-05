using TwinCatAssistant.Native;

// In-process host classification mirrors native/host-targets.json. These are
// policy regressions, not live host acceptance.
internal static class HostIdentityTests
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool condition, string label) {
            if (!condition) throw new InvalidOperationException("Host identity regression: " + label);
            Console.WriteLine("PASS " + label); count++;
        }
        HostIdentity Host(string exe = "TcXaeShell.exe", string version = "15.0.0.0", string arch = "x86",
            string shellBase = "15.0.35826.140 built by: D15.9", string twinCat = "3.1.4024.78") =>
            new HostIdentity { ExecutableName = exe, ExecutableVersion = version, ProcessArchitecture = arch, ShellBaseVersion = shellBase, TwinCatVersion = twinCat };

        var alpha = HostTargets.Assess(Host());
        Check(alpha.AlphaTarget && alpha.TargetId == HostTargets.AlphaTargetId, "verified TcXaeShell x86 with 4024.78 is the alpha target");
        Check(Host().Kind == "TcXaeShell" && Host().ShellBaseMajor == 15 && Host().TwinCatFamily == "4024", "identity fields are derived");
        var other = HostTargets.Assess(Host(twinCat: "3.1.4024.62"));
        Check(!other.AlphaTarget && other.Classification == "alpha-target-unverified-build", "other 4024 sub-build stays unverified");
        Check(HostTargets.Assess(Host(twinCat: "3.1.4026.17")).Classification == "unaccepted", "4026 in the x86 shell is unaccepted");
        Check(HostTargets.Assess(Host(twinCat: null)).Classification == "unknown", "unknown TwinCAT build fails closed");
        Check(HostTargets.Assess(Host(version: "15.0.1.0")).Classification == "unknown", "different shell executable version fails closed");
        Check(HostTargets.Assess(Host(arch: "amd64", shellBase: "17.0.1")).Classification == "blocked", "a 64-bit TcXaeShell is Shell64 and blocked");
        Check(HostTargets.Assess(Host(exe: "TcXaeShell64.exe", arch: "amd64", shellBase: "17.0.1", twinCat: "3.1.4026.17")).Classification == "blocked", "TcXaeShell64 is blocked");
        Check(HostTargets.Assess(Host(exe: "devenv.exe", version: "16.11.37827.8", shellBase: "16.11.37827.8")).Classification == "unaccepted", "VS2019 integration is unaccepted");
        Check(HostTargets.Assess(Host(exe: "devenv.exe", version: "17.14.0", arch: "amd64", shellBase: "17.14.0", twinCat: "3.1.4026.17")).Classification == "unaccepted", "VS2022 integration is unaccepted");
        Check(HostTargets.Assess(Host(exe: "devenv.exe", version: "18.0.0", arch: "amd64", shellBase: "18.0.0", twinCat: "3.1.4026.17")).Classification == "out-of-scope", "VS2026 is out of scope");
        Check(HostTargets.Assess(new HostIdentity()).Classification == "unknown", "empty identity is unknown");
        Check(HostTargets.Describe(Host()) == "TcXaeShell x86 · TwinCAT 3.1.4024.78", "compact description");
        return count;
    }
}
