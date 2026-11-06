using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
class Bootstrap {
 static string Quote(string s) {
  var b = new System.Text.StringBuilder("\""); int slash = 0;
  foreach (char c in s) { if (c == '\\') { slash++; continue; } if (c == '"') { b.Append('\\', slash * 2 + 1); b.Append(c); } else { b.Append('\\', slash); b.Append(c); } slash = 0; }
  b.Append('\\', slash * 2); b.Append('"'); return b.ToString();
 }
 static int Main(string[] args) {
  string dir = Path.Combine(Path.GetTempPath(), "twincat-installer-" + Guid.NewGuid().ToString("N"));
  try {
   Directory.CreateDirectory(dir);
   using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
   using (var archive = new ZipArchive(input, ZipArchiveMode.Read)) {
    foreach (var e in archive.Entries) {
     var target = Path.GetFullPath(Path.Combine(dir, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
     if (!target.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid archive path");
     if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
     Directory.CreateDirectory(Path.GetDirectoryName(target));
     using (var src = e.Open()) using (var dst = new FileStream(target, FileMode.CreateNew)) src.CopyTo(dst);
    }
   }
   var arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(dir, "Invoke-Installer.ps1"));
   foreach (var arg in args) arguments += " " + Quote(arg);
   var start = new ProcessStartInfo("powershell.exe", arguments) { UseShellExecute = false }; start.EnvironmentVariables.Remove("PSModulePath"); var p = Process.Start(start); p.WaitForExit(); return p.ExitCode;
  } catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
  finally { try { Directory.Delete(dir, true); } catch { } }
 }
}
