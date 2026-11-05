using System;
using System.Text.RegularExpressions;

namespace TwinCatAssistant.Native
{
    // Identity of the IDE process that loaded the Assistant package. Values are
    // read from the process image, file version resources and the registry; no
    // COM call is involved. Unknown values stay null rather than guessed.
    public sealed class HostIdentity
    {
        public string ExecutableName { get; set; }
        public string ExecutableVersion { get; set; }
        public string ProcessArchitecture { get; set; }
        public string ShellBaseVersion { get; set; }
        public string TwinCatVersion { get; set; }

        public string Kind
        {
            get
            {
                var name = (ExecutableName ?? "").ToLowerInvariant();
                if (name == "tcxaeshell64.exe" || (name == "tcxaeshell.exe" && ProcessArchitecture == "amd64")) return "TcXaeShell64";
                if (name == "tcxaeshell.exe") return "TcXaeShell";
                if (name == "devenv.exe") return "VisualStudio";
                return "unknown";
            }
        }

        public int? ShellBaseMajor
        {
            get
            {
                var match = Regex.Match(ShellBaseVersion ?? ExecutableVersion ?? "", @"^\s*(\d+)\.");
                return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
            }
        }

        public string TwinCatFamily
        {
            get
            {
                var match = Regex.Match(TwinCatVersion ?? "", @"^3\.1\.(\d{4})(\.|$)");
                return match.Success ? match.Groups[1].Value : null;
            }
        }
    }

    public sealed class HostAssessment
    {
        public string TargetId { get; set; }
        public string Classification { get; set; }
        public string Reason { get; set; }
        public bool AlphaTarget { get { return Classification == "alpha-target"; } }
    }

    // In-process mirror of native/host-targets.json (kept equal by a source
    // contract test). A classification is not live acceptance of the host.
    public static class HostTargets
    {
        public const string AlphaTargetId = "tcxaeshell-x86-tc4024";
        public const string AlphaExecutableVersion = "15.0.0.0";
        public static readonly string[] AlphaVerifiedTwinCatBuilds = { "3.1.4024.78" };

        public static HostAssessment Assess(HostIdentity host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var kind = host.Kind;
            var major = host.ShellBaseMajor;
            var family = host.TwinCatFamily;
            if (kind == "TcXaeShell" && host.ProcessArchitecture == "x86" && major == 15)
            {
                if (family == "4026") return Result("tcxaeshell-x86-tc4026", "unaccepted", "TwinCAT 4026 in the 32-bit shell has no Assistant acceptance.");
                if (family != "4024") return Result(null, "unknown", "TwinCAT 3.1 build was not identified.");
                if (host.ExecutableVersion != AlphaExecutableVersion) return Result(null, "unknown", "Shell executable version " + host.ExecutableVersion + " differs from the alpha target.");
                if (Array.IndexOf(AlphaVerifiedTwinCatBuilds, host.TwinCatVersion) < 0)
                    return Result(AlphaTargetId, "alpha-target-unverified-build", "TwinCAT " + host.TwinCatVersion + " is not a verified build of the alpha target.");
                return Result(AlphaTargetId, "alpha-target", "Alpha target host; open acceptance gates remain open.");
            }
            if (kind == "TcXaeShell64") return Result("tcxaeshell64-amd64-tc4026", "blocked", "TcXaeShell64 has no validated payload target or acceptance.");
            if (kind == "VisualStudio" && major == 18) return Result("visualstudio-2026", "out-of-scope", "No payload targets Visual Studio 2026.");
            if (kind == "VisualStudio" && host.ProcessArchitecture == "x86" && (major == 15 || major == 16))
                return Result("visualstudio-2017-2019-x86", "unaccepted", "TwinCAT in full Visual Studio has no Assistant acceptance.");
            if (kind == "VisualStudio" && host.ProcessArchitecture == "amd64" && major == 17)
                return Result("visualstudio-2022-amd64", "unaccepted", "TwinCAT in full Visual Studio has no Assistant acceptance.");
            return Result(null, "unknown", "Host is not a known TwinCAT engineering host.");
        }

        public static string Describe(HostIdentity host)
        {
            var kind = host.Kind == "unknown" ? (host.ExecutableName ?? "Unbekannter Host") : host.Kind;
            return kind + " " + (host.ProcessArchitecture ?? "?") + " · TwinCAT " + (host.TwinCatVersion ?? "unbekannt");
        }

        private static HostAssessment Result(string target, string classification, string reason)
        {
            return new HostAssessment { TargetId = target, Classification = classification, Reason = reason };
        }
    }
}
