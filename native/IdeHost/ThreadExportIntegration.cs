using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using Microsoft.Win32;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private const int ThreadExportLimit = 8 * 1024 * 1024;
        private bool _threadExporting;

        private sealed class ThreadExportResult
        {
            public ThreadExportResult() { }
            public string id { get; set; }
            public string filename { get; set; }
            public string markdown { get; set; }
        }

        private async Task ExportThreadAsync(bool save)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || _threadExporting) return;
            _threadExporting = true;
            var requestId = Guid.NewGuid().ToString("N");
            var generation = _chatDocumentGeneration;
            try
            {
                var selection = await ReadChatSelectionAsync();
                if (selection?.IsSelected != true || !selection.canExport)
                    throw new InvalidOperationException("Der Chatverlauf ist noch nicht für den Export verfügbar.");
                var serializer = new JavaScriptSerializer { MaxJsonLength = ThreadExportLimit * 8 + 4096 };
                var expected = new { selection.documentId, selection.sessionId, selection.epoch };
                await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatThreadExport={id:'" + requestId
                    + "',phase:'pending'};window.__twinCatChat.exportThread(" + serializer.Serialize(expected)
                    + ").then(value=>{if(window.__twinCatThreadExport?.id==='" + requestId
                    + "')window.__twinCatThreadExport={...value,id:'" + requestId
                    + "',phase:'ready'};},()=>{if(window.__twinCatThreadExport?.id==='" + requestId
                    + "')window.__twinCatThreadExport={id:'" + requestId + "',phase:'failed'};});");
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (!_disposed && generation == _chatDocumentGeneration && DateTime.UtcNow < deadline)
                {
                    var phase = await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatThreadExport?.id==='"
                        + requestId + "'?window.__twinCatThreadExport.phase:null");
                    if (phase == "\"failed\"") throw new InvalidOperationException("Der vollständige Chatverlauf konnte nicht geladen werden. Es wurde nichts exportiert.");
                    if (phase == "\"ready\"")
                    {
                        var current = await ReadChatSelectionAsync();
                        if (_disposed || generation != _chatDocumentGeneration || current?.IsSelected != true
                            || current.documentId != selection.documentId || current.sessionId != selection.sessionId
                            || current.epoch != selection.epoch || !SameDirectory(current.directory, selection.directory))
                            throw new InvalidOperationException("Die Unterhaltung wurde gewechselt. Bitte den Export im gewünschten Chat erneut starten.");
                        var raw = await _browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.__twinCatThreadExport)");
                        if (raw == null || raw.Length > serializer.MaxJsonLength) throw new InvalidOperationException("Der Chatverlauf ist für diesen Export zu groß.");
                        var result = serializer.Deserialize<ThreadExportResult>(serializer.Deserialize<string>(raw));
                        if (result?.id != requestId || result.markdown == null || result.markdown.Length > ThreadExportLimit
                            || string.IsNullOrEmpty(result.filename) || result.filename.Length > 200
                            || Path.GetFileName(result.filename) != result.filename || result.filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                            || !result.filename.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Der Chat-Export ist ungültig. Es wurde nichts gespeichert oder kopiert.");
                        if (save)
                        {
                            var dialog = new SaveFileDialog { Title = "Chatverlauf als Markdown speichern", FileName = result.filename,
                                DefaultExt = ".md", Filter = "Markdown (*.md)|*.md", AddExtension = true, OverwritePrompt = true };
                            if (dialog.ShowDialog() != true) return;
                            var path = dialog.FileName;
                            if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("Bitte eine Markdown-Datei mit der Endung .md wählen.");
                            await Task.Run(() => SaveThreadMarkdown(path, result.markdown));
                            Notify("Chatverlauf als Markdown gespeichert.");
                        }
                        else
                        {
                            Clipboard.SetText(result.markdown);
                            Notify("Gesamten gespeicherten Chatverlauf kopiert.");
                        }
                        return;
                    }
                    await Task.Delay(150);
                }
                if (!_disposed) throw new InvalidOperationException("Der Chat-Export wurde durch einen Seitenwechsel oder eine Zeitüberschreitung beendet.");
            }
            catch (Exception error)
            {
                if (!_disposed) Notify(error is InvalidOperationException ? error.Message
                    : "Der Chatverlauf konnte nicht kopiert oder gespeichert werden. Bitte erneut versuchen.", "error");
            }
            finally
            {
                _threadExporting = false;
                if (!_disposed && generation == _chatDocumentGeneration && _browser.CoreWebView2 != null)
                {
                    try { await _browser.CoreWebView2.ExecuteScriptAsync("if(window.__twinCatThreadExport?.id==='" + requestId + "')delete window.__twinCatThreadExport;"); }
                    catch { }
                }
            }
        }

        private static void SaveThreadMarkdown(string path, string markdown)
        {
            var temporary = Path.Combine(Path.GetDirectoryName(path), ".twincat-chat-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                { writer.Write(markdown); writer.Flush(); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
