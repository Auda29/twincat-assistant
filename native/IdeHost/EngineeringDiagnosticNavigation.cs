using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System.Web.Script.Serialization;

namespace TwinCatAssistant.Native
{
    // The provider owns the build freshness and baseline proof. This accessor
    // only accepts its one-shot proof and an exact current native ErrorItem.
    internal sealed class EngineeringDiagnosticNavigation
    {
        private readonly DTE dte;
        private readonly EngineeringBinding binding;
        private readonly Func<bool> current;
        private readonly Func<string, string, string, bool> consumeClick;
        private const int MaxRows = 1000, MaxProjects = 512, MaxItems = 4096;

        internal EngineeringDiagnosticNavigation(DTE dte, EngineeringBinding binding, Func<bool> current,
            Func<string, string, string, bool> consumeClick)
        { this.dte = dte; this.binding = binding; this.current = current; this.consumeClick = consumeClick; }

        internal bool Navigate(string strictProofJson)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var proof = Parse(strictProofJson, binding);
                if (proof == null || dte == null || current == null || consumeClick == null || !current() || !SolutionMatches()) return false;
                var types = Types.Create();
                if (types == null || !types.Dte.IsInstanceOfType(dte)) return false;
                var items = Items(types);
                Row first; object selected;
                if (items == null || !FindUnique(types, items, proof, out selected, out first) || !ProjectMatches(first.Project) || !current()) return false;
                // The host owns the matching pending click, consumed only once.
                if (!consumeClick(proof.Request, proof.Build, proof.Diagnostic)) return false;
                if (!current() || !SolutionMatches() || !ProjectMatches(first.Project)) return false;
                var againItems = Items(types); Row again; object againItem;
                if (againItems == null || !FindUnique(types, againItems, proof, out againItem, out again)
                    || !SameCom(items, againItems) || !SameCom(selected, againItem) || !first.Same(again)
                    || !ProjectMatches(again.Project) || !current() || !SolutionMatches()) return false;
                // Re-read the retained COM row immediately before the IDE call.
                var last = ReadRow(types, selected);
                if (!first.Same(last) || !last.Matches(proof) || !current()) return false;
                var navigate = types.Item.GetMethod("Navigate", Type.EmptyTypes);
                if (navigate == null || navigate.ReturnType != typeof(void)) return false;
                navigate.Invoke(selected, null);
                return true;
            }
            catch { return false; }
        }
        private bool SolutionMatches()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return dte.Solution != null && SamePath(dte.Solution.FullName, binding.SolutionFile);
        }
        private object Items(Types types)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var windows = Get(types.Dte, dte, "ToolWindows");
            if (windows == null || !types.Windows.IsInstanceOfType(windows)) return null;
            var list = Get(types.Windows, windows, "ErrorList");
            if (list == null || !types.List.IsInstanceOfType(list)) return null;
            var items = Get(types.List, list, "ErrorItems");
            return items != null && types.Items.IsInstanceOfType(items) && Marshal.IsComObject(items) ? items : null;
        }
        private static bool FindUnique(Types types, object items, Proof proof, out object selected, out Row row)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); selected = null; row = null;
            var countValue = Get(types.Items, items, "Count");
            if (!(countValue is int count) || count < 0 || count > MaxRows) return false;
            var method = types.Items.GetMethod("Item", new[] { typeof(object) });
            if (method == null) return false;
            for (int index = 1; index <= count; index++)
            {
                var candidate = method.Invoke(items, new object[] { index });
                if (candidate == null || !types.Item.IsInstanceOfType(candidate) || !Marshal.IsComObject(candidate)) return false;
                var snapshot = ReadRow(types, candidate);
                if (snapshot == null) return false;
                if (snapshot.Matches(proof))
                {
                    if (selected != null) return false;
                    selected = candidate; row = snapshot;
                }
            }
            var after = Get(types.Items, items, "Count");
            return selected != null && after is int n && n == count;
        }
        private static Row ReadRow(Types types, object item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var file = Get(types.Item, item, "FileName") as string;
            var project = Get(types.Item, item, "Project") as string;
            var description = Get(types.Item, item, "Description") as string;
            var lineValue = Get(types.Item, item, "Line"); var columnValue = Get(types.Item, item, "Column");
            var levelValue = Get(types.Item, item, "ErrorLevel");
            if (file == null || file.Length > 4096 || project == null || project.Length > 1024 || description == null || description.Length > 4096
                || !(lineValue is int line) || !(columnValue is int column) || levelValue == null) return null;
            var level = Convert.ToInt32(levelValue, CultureInfo.InvariantCulture);
            return new Row { File = file, Project = project, Description = description, Line = line, Column = column, Level = level };
        }
        private bool ProjectMatches(string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(name) || name.Length > 1024 || dte.Solution == null || dte.Solution.Projects == null) return false;
            int projects = 0, items = 0, matches = 0; string path = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Project project in dte.Solution.Projects)
                if (!Visit(project, name, seen, ref projects, ref items, ref matches, ref path)) return false;
            return matches == 1 && SamePath(path, binding.ProjectFile);
        }
        private static bool Visit(Project project, string name, HashSet<string> seen, ref int projects, ref int items, ref int matches, ref string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null || ++projects > MaxProjects) return false;
            var unique = project.UniqueName;
            if (string.IsNullOrEmpty(unique) || unique.Length > 2048 || !seen.Add(unique)) return false;
            var projectName = project.Name;
            if (string.Equals(projectName, name, StringComparison.OrdinalIgnoreCase)) { matches++; path = project.FullName; }
            var children = project.ProjectItems;
            if (children == null) return true;
            foreach (ProjectItem item in children)
            {
                if (item == null || ++items > MaxItems) return false;
                var nested = item.SubProject;
                if (nested != null && !Visit(nested, name, seen, ref projects, ref items, ref matches, ref path)) return false;
            }
            return true;
        }
        private sealed class Row
        {
            internal string File, Project, Description;
            internal int Line, Column, Level;
            internal bool Matches(Proof p) { return p != null && File == p.File && Line == p.Line && Column == p.Column && Level == p.Level && Description == p.Description && !string.IsNullOrEmpty(Project); }
            internal bool Same(Row r) { return r != null && File == r.File && Project == r.Project && Description == r.Description && Line == r.Line && Column == r.Column && Level == r.Level; }
        }
        private sealed class Proof
        {
            internal string Request, Build, Diagnostic, File, Description;
            internal int Line, Column, Level;
        }
        private static Proof Parse(string json, EngineeringBinding binding)
        {
            if (binding == null || string.IsNullOrEmpty(json) || json.Length > 16384 || new UTF8Encoding(false, true).GetByteCount(json) > 16384 || !StrictJsonLexical(json)) return null;
            var root = new JavaScriptSerializer { MaxJsonLength = 16384, RecursionLimit = 12 }.DeserializeObject(json) as Dictionary<string, object>;
            if (!Keys(root, "protocolVersion", "sessionId", "requestOperationId", "buildOperationId", "diagnosticId", "expected", "diagnostic", "baseline") || Integer(root, "protocolVersion") != 1
                || String(root, "sessionId", 36) != binding.SessionId.ToString("D")) return null;
            var request = String(root, "requestOperationId", 36); var build = String(root, "buildOperationId", 36); var diagnosticId = String(root, "diagnosticId", 64);
            if (!Uuid(request) || !Uuid(build) || !Digest(diagnosticId)) return null;
            var expected = root["expected"] as Dictionary<string, object>; var diagnostic = root["diagnostic"] as Dictionary<string, object>; var baseline = root["baseline"] as Dictionary<string, object>;
            if (!Keys(expected, "idePid", "ideStartedUtc", "solutionFile", "projectFile", "contextEpoch")
                || !Keys(diagnostic, "file", "line", "column", "severity", "code", "message")
                || !Keys(baseline, "projectFile", "projectDigest", "sourceFile", "sourceDigest", "objectId", "qualifiedName", "memberPath", "section", "coordinateKind")) return null;
            DateTime started;
            if (Integer(expected, "idePid") != binding.IdePid || !DateTime.TryParse(String(expected, "ideStartedUtc", 64), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out started) || started != binding.IdeStartedUtc
                || !SamePath(String(expected, "solutionFile", 4096), binding.SolutionFile) || !SamePath(String(expected, "projectFile", 4096), binding.ProjectFile)
                || String(expected, "contextEpoch", 256) != binding.ContextEpoch || !SamePath(String(baseline, "projectFile", 4096), binding.ProjectFile)
                || !Digest(String(baseline, "projectDigest", 64)) || !Digest(String(baseline, "sourceDigest", 64))
                || baseline["section"] != null || String(baseline, "coordinateKind", 64) != "nativeErrorItem") return null;
            var file = String(diagnostic, "file", 4096); var source = String(baseline, "sourceFile", 4096);
            var member = NullableString(baseline, "memberPath", 512); var qualified = NullableString(baseline, "qualifiedName", 512); var objectId = NullableString(baseline, "objectId", 64);
            if (!Canonical(source) || file == null || (baseline["memberPath"] != null && (!(baseline["memberPath"] is string) || member == null)) || !NullableValid(baseline, "qualifiedName", qualified) || !NullableValid(baseline, "objectId", objectId)) return null;
            if (member != null && member.Length == 0) member = null;
            if (member == null ? !SamePath(file, source) : !MemberMoniker(file, source, member)) return null;
            if (member != null && (qualified == null || objectId == null || !Uuid(objectId))) return null;
            var line = Integer(diagnostic, "line"); var column = Integer(diagnostic, "column"); var severity = String(diagnostic, "severity", 16);
            var code = String(diagnostic, "code", 64); var message = String(diagnostic, "message", 4096);
            if (line < 1 || line > 1000000 || column < 1 || column > 100000 || (severity != "error" && severity != "warning") || code == null || string.IsNullOrEmpty(message)) return null;
            return new Proof { Request = request, Build = build, Diagnostic = diagnosticId, File = file, Line = line, Column = column, Level = severity == "error" ? 4 : 2, Description = code.Length == 0 ? message : code + ": " + message };
        }
        private static bool MemberMoniker(string file, string source, string member)
        {
            if (file == null || source == null || member == null) return false;
            var separator = file.LastIndexOf(';');
            return separator > 0 && file.Substring(separator + 1) == member && SamePath(file.Substring(0, separator), source);
        }
        private static bool StrictJsonLexical(string json)
        {
            int index = 0;
            return JsonValue(json, ref index, 0) && JsonSpace(json, ref index) && index == json.Length;
        }
        private static bool JsonSpace(string s, ref int i)
        { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++; return true; }
        private static bool JsonString(string s, ref int i)
        {
            if (i >= s.Length || s[i++] != '"') return false;
            while (i < s.Length)
            {
                char c = s[i++]; if (c == '"') return true; if (c < 0x20) return false;
                if (c != '\\') continue;
                if (i >= s.Length) return false;
                c = s[i++];
                if (c == 'u') { for (int j = 0; j < 4; j++) if (i >= s.Length || !Uri.IsHexDigit(s[i++])) return false; }
                else if (c != '"' && c != '\\' && c != '/' && c != 'b' && c != 'f' && c != 'n' && c != 'r' && c != 't') return false;
            }
            return false;
        }
        private static bool JsonValue(string s, ref int i, int depth)
        {
            if (depth > 12 || !JsonSpace(s, ref i) || i >= s.Length) return false;
            if (s[i] == '"') return JsonString(s, ref i);
            if (s[i] == '{' || s[i] == '[')
            {
                bool obj = s[i++] == '{'; char end = obj ? '}' : ']'; JsonSpace(s, ref i);
                var names = obj ? new HashSet<string>(StringComparer.Ordinal) : null;
                if (i < s.Length && s[i] == end) { i++; return true; }
                while (i < s.Length)
                {
                    if (obj)
                    {
                        int start = i;
                        if (!JsonString(s, ref i)) return false;
                        var name = new JavaScriptSerializer().Deserialize<string>(s.Substring(start, i - start));
                        if (name == null || !names.Add(name) || !JsonSpace(s, ref i) || i >= s.Length || s[i++] != ':') return false;
                    }
                    if (!JsonValue(s, ref i, depth + 1) || !JsonSpace(s, ref i) || i >= s.Length) return false;
                    if (s[i] == end) { i++; return true; }
                    if (s[i++] != ',') return false;
                    JsonSpace(s, ref i);
                }
                return false;
            }
            foreach (string literal in new[] { "true", "false", "null" })
                if (i + literal.Length <= s.Length && string.CompareOrdinal(s, i, literal, 0, literal.Length) == 0) { i += literal.Length; return true; }
            if (s[i] == '-') i++;
            if (i >= s.Length || s[i] < '0' || s[i] > '9') return false;
            if (s[i] == '0') i++; else while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (i < s.Length && s[i] == '.') { i++; int start = i; while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++; if (start == i) return false; }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E')) { i++; if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++; int start = i; while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++; if (start == i) return false; }
            return true;
        }
        private static bool Keys(Dictionary<string, object> obj, params string[] names) { return obj != null && obj.Count == names.Length && names.All(obj.ContainsKey); }
        private static string String(Dictionary<string, object> obj, string key, int max) { object value; var text = obj != null && obj.TryGetValue(key, out value) ? value as string : null; return text != null && text.Length <= max ? text : null; }
        private static string NullableString(Dictionary<string, object> obj, string key, int max) { return obj[key] == null ? null : String(obj, key, max); }
        private static bool NullableValid(Dictionary<string, object> obj, string key, string value) { return obj[key] == null || !string.IsNullOrEmpty(value); }
        private static int Integer(Dictionary<string, object> obj, string key) { object value; return obj != null && obj.TryGetValue(key, out value) && value is int number ? number : -1; }
        private static bool Uuid(string value) { Guid id; return Guid.TryParseExact(value, "D", out id) && id != Guid.Empty && value == id.ToString("D"); }
        private static bool Digest(string value) { if (value == null || value.Length != 64) return false; return value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')); }
        private static bool Canonical(string path) { try { return !string.IsNullOrEmpty(path) && Path.IsPathRooted(path) && Path.GetFullPath(path) == path; } catch { return false; } }
        private static bool SamePath(string a, string b) { return Canonical(a) && Canonical(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static object Get(Type type, object instance, string property) { return type.GetProperty(property).GetValue(instance, null); }
        private static bool SameCom(object a, object b)
        {
            if (a == null || b == null || !Marshal.IsComObject(a) || !Marshal.IsComObject(b)) return false;
            var x = Marshal.GetIUnknownForObject(a); var y = Marshal.GetIUnknownForObject(b);
            try { return x == y; } finally { Marshal.Release(x); Marshal.Release(y); }
        }
        private sealed class Types
        {
            internal Type Dte, Windows, List, Items, Item;
            internal static Types Create()
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = assembly.GetName().Name;
                    if (name != "EnvDTE80" && name != "Microsoft.VisualStudio.Interop") continue;
                    var result = new Types { Dte = assembly.GetType("EnvDTE80.DTE2"), Windows = assembly.GetType("EnvDTE80.ToolWindows"), List = assembly.GetType("EnvDTE80.ErrorList"), Items = assembly.GetType("EnvDTE80.ErrorItems"), Item = assembly.GetType("EnvDTE80.ErrorItem") };
                    if (result.Dte != null && result.Windows != null && result.List != null && result.Items != null && result.Item != null
                        && result.Item.GUID == new Guid("f49a191f-7258-493b-9310-5f7771ddf3d7") && result.Items.GUID == new Guid("dcf93a30-d013-42f4-8aee-9f5ba215fb8b")) return result;
                }
                return null;
            }
        }
    }
}
