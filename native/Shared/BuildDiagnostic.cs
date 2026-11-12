using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TwinCatAssistant.Native
{
    // Only lines in the newly emitted Build pane segment are compiler evidence.
    public sealed class BuildDiagnostic
    {
        public string File, Severity, Message, Code;
        public int Line, Column;
        private static readonly Regex Pattern = new Regex(@"^\s*(?<file>.+?)\((?<line>\d+)(?:,(?<column>\d+))?\)\s*:\s*(?<severity>error|warning|Fehler|Warnung)\s*(?:(?<code>[A-Z]\d+)\s*:\s*)?(?<message>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        public static List<BuildDiagnostic> Parse(string output)
        {
            var result = new List<BuildDiagnostic>();
            if (output == null || output.Length > 262144) return result;
            foreach (var text in output.Split('\n'))
            {
                if (result.Count == 200) break;
                if (text.Length > 4096) continue;
                var match = Pattern.Match(Regex.Replace(text.TrimEnd('\r'), @"^\s*\d+>", ""));
                int line, column = 1;
                if (!match.Success || !int.TryParse(match.Groups["line"].Value, out line) || line < 1 || line > 1000000) continue;
                if (match.Groups["column"].Success && (!int.TryParse(match.Groups["column"].Value, out column) || column < 1 || column > 100000)) continue;
                result.Add(new BuildDiagnostic { File = match.Groups["file"].Value, Line = line, Column = column,
                    Severity = (match.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase) || match.Groups["severity"].Value.Equals("Fehler", StringComparison.OrdinalIgnoreCase)) ? "error" : "warning", Message = match.Groups["message"].Value, Code = match.Groups["code"].Value });
            }
            return result;
        }
        public static string FreshOutput(string before, string after)
        {
            if (before == null || after == null || !after.StartsWith(before, StringComparison.Ordinal)) return null;
            var delta = after.Substring(before.Length);
            return delta.Length <= 262144 ? delta : null;
        }
    }
}
