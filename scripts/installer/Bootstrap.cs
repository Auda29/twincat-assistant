using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

class Bootstrap {
 [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
 internal static string Quote(string s) {
  var b = new StringBuilder("\""); int slash = 0;
  foreach (char c in s) { if (c == '\\') { slash++; continue; } if (c == '"') { b.Append('\\', slash * 2 + 1); b.Append(c); } else { b.Append('\\', slash); b.Append(c); } slash = 0; }
  b.Append('\\', slash * 2); b.Append('"'); return b.ToString();
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
 readonly TextBox details = new TextBox();
 readonly ProgressBar progress = new ProgressBar();
 bool running; string logPath;
 public int ExitCode = 0;
 public InstallerWizard() {
  Text = "TwinCAT Assistant installieren"; Font = new Font("Segoe UI", 9F);
  AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(650, 540);
  FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
  var title = new Label { Text = "TwinCAT Assistant Setup", Font = new Font(Font.FontFamily, 17F), AutoSize = true, Location = new Point(24, 22) };
  var intro = new Label { Text = "Installation und Updates fuer TwinCAT 3.1.4024.78 / TcXaeShell x86.\r\nTwinCAT vor dem Start schliessen. Nutzerdaten bleiben erhalten.", Location = new Point(24, 65), Size = new Size(600, 45) };
  var actionLabel = new Label { Text = "Aktion", Location = new Point(24, 120), AutoSize = true };
  action.SetBounds(24, 143, 600, 27); action.DropDownStyle = ComboBoxStyle.DropDownList;
  action.Items.AddRange(new object[] { "Installieren / aktualisieren", "Vorherige installierte Version wiederherstellen" }); action.SelectedIndex = 0;
  var pathLabel = new Label { Text = "Installationsordner", Location = new Point(24, 182), AutoSize = true };
  destination.SetBounds(24, 205, 485, 27);
  destination.Text = Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TwinCatAssistant");
  browse.Text = "Durchsuchen ..."; browse.SetBounds(519, 203, 105, 29);
  browse.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "Installationsordner auswaehlen", SelectedPath = destination.Text }) { if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath; } };
  var prerequisites = new Label { Text = "Setup prueft den Host, das Paket und die .NET-8-Laufzeit. WebView2 und\r\n.NET Framework muessen vorhanden sein. Eine Wiederherstellung braucht\r\neine zuvor mit diesem Installer installierte, kompatible Version.", Location = new Point(24, 245), Size = new Size(600, 60) };
  status.Text = "Bereit zur Installation."; status.SetBounds(24, 315, 600, 42);
  progress.SetBounds(24, 360, 600, 12);
  details.SetBounds(24, 385, 600, 90); details.Multiline = true; details.ReadOnly = true; details.ScrollBars = ScrollBars.Vertical; details.BackColor = SystemColors.Window;
  openLog.Text = "Protokoll oeffnen"; openLog.SetBounds(24, 493, 135, 30); openLog.Visible = false;
  openLog.Click += delegate { try { Process.Start(new ProcessStartInfo("notepad.exe", Bootstrap.Quote(logPath)) { UseShellExecute = false }); } catch (Exception e) { MessageBox.Show(this, e.Message, "Protokoll"); } };
  install.Text = "Installieren"; install.SetBounds(354, 493, 130, 30); install.Click += BeginInstallation;
  close.Text = "Abbrechen"; close.SetBounds(494, 493, 130, 30); close.Click += delegate { Close(); };
  action.SelectedIndexChanged += delegate { install.Text = action.SelectedIndex == 0 ? "Installieren" : "Wiederherstellen"; };
  Controls.AddRange(new Control[] { title, intro, actionLabel, action, pathLabel, destination, browse, prerequisites, status, progress, details, openLog, install, close });
  AcceptButton = install; CancelButton = close;
  FormClosing += delegate(object sender, FormClosingEventArgs e) { if (running) { e.Cancel = true; MessageBox.Show(this, "Bitte warten, bis der laufende Vorgang abgeschlossen ist.", "Installation laeuft"); } };
 }
 void BeginInstallation(object sender, EventArgs args) {
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
