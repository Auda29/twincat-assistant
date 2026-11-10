using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Web.Script.Serialization;

class Bootstrap {
 [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
 internal static string Quote(string s) {
  var b = new StringBuilder("\""); int slash = 0;
  foreach (char c in s) { if (c == '\\') { slash++; continue; } if (c == '"') { b.Append('\\', slash * 2 + 1); b.Append(c); } else { b.Append('\\', slash); b.Append(c); } slash = 0; }
  b.Append('\\', slash * 2); b.Append('"'); return b.ToString();
 }
 internal static string DefaultInstallRoot() {
  return Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TwinCatAssistant");
 }
 internal static bool ValidVersion(object value) {
  var version = value as string;
  return version != null && version.Length <= 128 && !version.EndsWith(".") && System.Text.RegularExpressions.Regex.IsMatch(version, @"^[0-9A-Za-z][0-9A-Za-z.+-]*$");
 }
 internal static Dictionary<string, object> ReadMetadata(Stream stream) {
  if (stream == null || stream.Length > 65536) throw new IOException("Versionsinformation konnte nicht gelesen werden.");
  using (var reader = new StreamReader(stream, new UTF8Encoding(false, true))) {
   var json = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 8 };
   var value = json.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
   if (value == null) throw new IOException("Versionsinformation konnte nicht gelesen werden.");
   return value;
  }
 }
 internal static string ReadSetupVersion() {
  using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup-info.json")) {
   var value = ReadMetadata(stream);
   if (value.Count != 2 || !value.ContainsKey("schemaVersion") || !(value["schemaVersion"] is int) || (int)value["schemaVersion"] != 1 || !value.ContainsKey("version") || !ValidVersion(value["version"])) throw new IOException("Setup-Version konnte nicht gelesen werden.");
   return (string)value["version"];
  }
 }
 internal static void NoRedirects(string path) {
  for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current)) {
   try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Verknuepfte Installationspfade werden nicht unterstuetzt."); }
   catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
  }
 }
 internal sealed class InstallationInfo {
  internal string Current, Previous;
  internal bool Pending;
 }
 // Display-only metadata. The transaction repeats all ownership, Host and hash checks.
 internal static InstallationInfo ReadInstallation(string directory) {
  if (!System.Text.RegularExpressions.Regex.IsMatch(directory, @"^[A-Za-z]:[\\/]")) throw new IOException("Bitte einen absoluten lokalen Installationsordner waehlen.");
  var root = Path.GetFullPath(directory);
  NoRedirects(root);
  try { if ((File.GetAttributes(root) & FileAttributes.Directory) == 0) throw new IOException("Bitte einen Installationsordner waehlen."); }
  catch (FileNotFoundException) { return null; } catch (DirectoryNotFoundException) { return null; }
  var marker = Path.Combine(root, ".assistant-install-root");
  var state = Path.Combine(root, "state.json");
  if (!File.Exists(marker) && !File.Exists(state)) {
   using (var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator()) {
    if (!entries.MoveNext()) return null;
   }
   throw new IOException("Der Ordner enthaelt keine erkannte Assistant-Installation. Bitte einen anderen Ordner waehlen.");
  }
  NoRedirects(marker); NoRedirects(state);
  using (var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read)) {
   if (stream.Length > 128) throw new IOException("Installationsstatus ist unvollstaendig oder beschaedigt.");
   using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
    if (reader.ReadToEnd() != "TwinCatAssistant installation v1") throw new IOException("Installationsstatus ist unvollstaendig oder beschaedigt.");
  }
  Dictionary<string, object> value;
  using (var stream = new FileStream(state, FileMode.Open, FileAccess.Read, FileShare.Read)) value = ReadMetadata(stream);
  if (!value.ContainsKey("current") || !ValidVersion(value["current"])) throw new IOException("Installierte Version konnte nicht gelesen werden.");
  object previous; value.TryGetValue("previous", out previous);
  if (previous != null && !ValidVersion(previous)) throw new IOException("Vorherige Version konnte nicht gelesen werden.");
  var pending = Path.Combine(root, "transaction.pending");
  return new InstallationInfo { Current = (string)value["current"], Previous = previous as string, Pending = File.Exists(pending) || Directory.Exists(pending) };
 }
 // Shared with payload verification; this helper never starts installation.
 internal static void ExtractPayload(Stream input, string dir) {
  Directory.CreateDirectory(dir);
  using (var archive = new ZipArchive(input, ZipArchiveMode.Read)) {
   foreach (var e in archive.Entries) {
    // Framework ZIPs on Windows can use backslashes, including empty folders.
    // Classify and resolve the same normalized name before opening any file.
    var name = e.FullName.Replace('\\', '/');
    var target = Path.GetFullPath(Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar)));
    if (!target.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid archive path");
    if (name.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
    Directory.CreateDirectory(Path.GetDirectoryName(target));
    using (var src = e.Open()) using (var dst = new FileStream(target, FileMode.CreateNew)) src.CopyTo(dst);
   }
  }
 }
 // Wizard and command line share the existing package/host transaction.
 internal static int Run(string[] args, Action<string> report) {
  string dir = Path.Combine(Path.GetTempPath(), "twincat-installer-" + Guid.NewGuid().ToString("N"));
  try {
   report("Anwendungspaket wird entpackt. Das kann einige Minuten dauern ...");
   using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")) ExtractPayload(input, dir);
   report("Voraussetzungen und Paket werden geprueft ...");
   var arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(Path.Combine(dir, "Invoke-Installer.ps1"));
   foreach (var arg in args) arguments += " " + Quote(arg);
   var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"), arguments) {
    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
   };
   start.EnvironmentVariables.Remove("PSModulePath");
   using (var p = new Process { StartInfo = start }) {
    p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) report(e.Data); };
    p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) report(e.Data); };
    p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine(); p.WaitForExit(); return p.ExitCode;
   }
  } catch (Exception e) { report(e.Message); return 1; }
  finally { try { Directory.Delete(dir, true); } catch { report("Temporaeres Verzeichnis konnte nicht entfernt werden: " + dir); } }
 }
 [STAThread]
 static int Main(string[] args) {
  if (args.Length != 0) { AttachConsole(-1); return Run(args, Console.WriteLine); }
  Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
  using (var wizard = new InstallerWizard()) { Application.Run(wizard); return wizard.ExitCode; }
 }
}

class InstallerWizard : Form {
 readonly TextBox destination = new TextBox();
 readonly ComboBox action = new ComboBox();
 readonly Button browse = new Button(), install = new Button(), close = new Button(), openLog = new Button();
 readonly Label status = new Label();
 readonly Label packageVersion = new Label(), installedVersion = new Label(), operation = new Label();
 readonly string offeredVersion;
 readonly TextBox details = new TextBox();
 readonly ProgressBar progress = new ProgressBar();
 bool running; string logPath;
 public int ExitCode = 0;
 public InstallerWizard() : this(Bootstrap.DefaultInstallRoot()) { }
 public InstallerWizard(string initialRoot) {
  using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("setup.ico"))
  using (var icon = new Icon(stream)) { Icon = (Icon)icon.Clone(); }
  Text = "TwinCAT Assistant installieren"; Font = new Font("Segoe UI", 9F);
  AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(650, 620);
  FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
  var title = new Label { Text = "TwinCAT Assistant Setup", Font = new Font(Font.FontFamily, 17F), AutoSize = true, Location = new Point(24, 22) };
  var intro = new Label { Text = "Installation und Updates fuer TwinCAT 3.1.4024.78 / TcXaeShell x86.\r\nTwinCAT vor dem Start schliessen. Nutzerdaten bleiben erhalten.", Location = new Point(24, 65), Size = new Size(600, 45) };
  try { offeredVersion = Bootstrap.ReadSetupVersion(); } catch { offeredVersion = null; }
  packageVersion.Text = offeredVersion == null ? "Setup-Version: nicht verfuegbar" : "Setup enthaelt: " + offeredVersion;
  packageVersion.Font = new Font(Font, FontStyle.Bold); packageVersion.SetBounds(24, 116, 600, 20);
  installedVersion.SetBounds(24, 138, 600, 20); operation.SetBounds(24, 160, 600, 32);
  var actionLabel = new Label { Text = "Aktion", Location = new Point(24, 200), AutoSize = true };
  action.SetBounds(24, 223, 600, 27); action.DropDownStyle = ComboBoxStyle.DropDownList;
  action.Items.AddRange(new object[] { "Installieren / aktualisieren", "Vorherige installierte Version wiederherstellen" }); action.SelectedIndex = 0;
  var pathLabel = new Label { Text = "Installationsordner", Location = new Point(24, 262), AutoSize = true };
  destination.SetBounds(24, 285, 485, 27);
  destination.Text = initialRoot;
  browse.Text = "Durchsuchen ..."; browse.SetBounds(519, 283, 105, 29);
  browse.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "Installationsordner auswaehlen", SelectedPath = destination.Text }) { if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath; } };
  var prerequisites = new Label { Text = "Setup prueft den Host, das Paket und die .NET-8-Laufzeit. WebView2 und\r\n.NET Framework muessen vorhanden sein. Eine Wiederherstellung braucht\r\neine zuvor mit diesem Installer installierte, kompatible Version.", Location = new Point(24, 325), Size = new Size(600, 60) };
  status.Text = "Bereit zur Installation."; status.SetBounds(24, 395, 600, 42);
  progress.SetBounds(24, 440, 600, 12);
  details.SetBounds(24, 465, 600, 90); details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Vertical; details.BackColor = SystemColors.Window;
  openLog.Text = "Protokoll oeffnen"; openLog.SetBounds(24, 573, 135, 30); openLog.Visible = false;
  openLog.Click += delegate { try { Process.Start(new ProcessStartInfo("notepad.exe", Bootstrap.Quote(logPath)) { UseShellExecute = false }); } catch (Exception e) { MessageBox.Show(this, e.Message, "Protokoll"); } };
  install.Text = "Installieren"; install.SetBounds(354, 573, 130, 30); install.Click += BeginInstallation;
  close.Text = "Abbrechen"; close.SetBounds(494, 573, 130, 30); close.Click += delegate { Close(); };
  action.SelectedIndexChanged += delegate { RefreshSelection(); };
  destination.TextChanged += delegate { RefreshSelection(); };
  Controls.AddRange(new Control[] { title, intro, packageVersion, installedVersion, operation, actionLabel, action, pathLabel, destination, browse, prerequisites, status, progress, details, openLog, install, close });
  AcceptButton = install; CancelButton = close;
  RefreshSelection();
  FormClosing += delegate(object sender, FormClosingEventArgs e) { if (running) { e.Cancel = true; MessageBox.Show(this, "Bitte warten, bis der laufende Vorgang abgeschlossen ist.", "Installation laeuft"); } };
 }
 void RefreshSelection() {
  if (running) return;
  install.Enabled = false;
  install.Text = action.SelectedIndex == 0 ? "Installieren" : "Wiederherstellen";
  installedVersion.Text = "Vorhandene Version: wird geprueft ...";
  try {
   if (offeredVersion == null) throw new IOException("Setup-Version ist nicht verfuegbar. Bitte Setup erneut herunterladen.");
   var root = Path.GetFullPath(destination.Text.Trim());
   var existing = Bootstrap.ReadInstallation(destination.Text.Trim());
   installedVersion.Text = existing == null ? "Im gewaehlten Ordner ist noch keine Installation vorhanden." : "Installiert: " + existing.Current;
   if (existing != null && existing.Pending) { operation.Text = "Eine unterbrochene Installation muss zuerst wiederhergestellt werden."; return; }
   if (action.SelectedIndex == 1) {
    if (existing == null || existing.Previous == null) { operation.Text = "Keine vorherige installierte Version zur Wiederherstellung vorhanden."; return; }
    operation.Text = "Wiederherstellung: " + existing.Current + " -> " + existing.Previous;
   } else if (existing == null) {
    operation.Text = "Neuinstallation von " + offeredVersion;
   } else {
    if (existing.Current == offeredVersion) { operation.Text = "Diese Version ist bereits installiert."; return; }
    var retained = Path.Combine(root, "versions", offeredVersion);
    if (File.Exists(retained) || Directory.Exists(retained)) { operation.Text = "Die Setup-Version ist bereits vorhanden. Gegebenenfalls Wiederherstellung waehlen."; return; }
    operation.Text = "Update: " + existing.Current + " -> " + offeredVersion;
    install.Text = "Aktualisieren";
   }
   install.Enabled = true;
  } catch {
   installedVersion.Text = "Vorhandene Version: nicht sicher ermittelbar.";
   operation.Text = "Installationsordner oder gespeicherten Installationsstatus pruefen.";
  }
 }
 void BeginInstallation(object sender, EventArgs args) {
  RefreshSelection();
  if (!install.Enabled) return;
  string root = destination.Text.Trim();
  try {
   if (!System.Text.RegularExpressions.Regex.IsMatch(root, @"^[A-Za-z]:[\\/]")) throw new Exception("Bitte einen absoluten lokalen Installationsordner angeben.");
   root = Path.GetFullPath(root);
  } catch (Exception e) { MessageBox.Show(this, e.Message, "Installationsordner"); return; }
  string selectedAction = action.SelectedIndex == 0 ? "Install" : "Rollback";
  StreamWriter log;
  try {
   string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinCatAssistant", "installer-logs");
   Directory.CreateDirectory(logDir);
   logPath = Path.Combine(logDir, "setup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".log");
   log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
   log.WriteLine("TwinCAT Assistant Setup: " + selectedAction + " / " + root);
  } catch (Exception e) { MessageBox.Show(this, "Protokoll konnte nicht angelegt werden: " + e.Message, "Setup"); return; }
  running = true; destination.Enabled = action.Enabled = browse.Enabled = install.Enabled = close.Enabled = false;
  progress.Style = ProgressBarStyle.Marquee; status.Text = "Installation wird vorbereitet ...";
  var worker = new BackgroundWorker();
  worker.DoWork += delegate(object s, DoWorkEventArgs e) {
   Action<string> report = delegate(string line) {
    lock (log) { log.WriteLine(line); }
    BeginInvoke((Action)delegate { details.AppendText(line + Environment.NewLine); status.Text = "Vorgang laeuft. Bitte warten; die Paketpruefung kann einige Minuten dauern."; });
   };
   try { e.Result = Bootstrap.Run(new string[] { "-Action", selectedAction, "-InstallRoot", root }, report); }
   finally { log.Dispose(); }
  };
  worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e) {
   running = false; ExitCode = e.Error == null ? (int)e.Result : 1;
   progress.Style = ProgressBarStyle.Blocks; progress.Value = ExitCode == 0 ? 100 : 0;
   status.Text = ExitCode == 0 ? "Abgeschlossen. Sie koennen TcXaeShell jetzt starten und TwinCAT Assistant oeffnen." : "Der Vorgang ist fehlgeschlagen. Details stehen unten und im gespeicherten Protokoll.";
   if (e.Error != null) details.AppendText(e.Error.Message + Environment.NewLine);
   details.AppendText("Protokoll: " + logPath + Environment.NewLine);
   close.Enabled = true; close.Text = "Schliessen"; openLog.Visible = true; install.Visible = false; AcceptButton = close; worker.Dispose();
  };
  worker.RunWorkerAsync();
 }
}
