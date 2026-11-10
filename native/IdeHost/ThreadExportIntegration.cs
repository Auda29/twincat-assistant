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
                    throw new InvalidOperationException(HostText.Get("H334"));
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
                    if (phase == "\"failed\"") throw new InvalidOperationException(HostText.Get("H335"));
                    if (phase == "\"ready\"")
                    {
                        var current = await ReadChatSelectionAsync();
                        if (_disposed || generation != _chatDocumentGeneration || current?.IsSelected != true
                            || current.documentId != selection.documentId || current.sessionId != selection.sessionId
                            || current.epoch != selection.epoch || !SameDirectory(current.directory, selection.directory))
                            throw new InvalidOperationException(HostText.Get("H336"));
                        var raw = await _browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.__twinCatThreadExport)");
                        if (raw == null || raw.Length > serializer.MaxJsonLength) throw new InvalidOperationException(HostText.Get("H337"));
                        var result = serializer.Deserialize<ThreadExportResult>(serializer.Deserialize<string>(raw));
                        if (result?.id != requestId || result.markdown == null || result.markdown.Length > ThreadExportLimit
                            || string.IsNullOrEmpty(result.filename) || result.filename.Length > 200
                            || Path.GetFileName(result.filename) != result.filename || result.filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                            || !result.filename.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(HostText.Get("H338"));
                        if (save)
                        {
                            var dialog = new SaveFileDialog { Title = HostText.Get("H339"), FileName = result.filename,
                                DefaultExt = ".md", Filter = HostText.Get("H340"), AddExtension = true, OverwritePrompt = true };
                            if (dialog.ShowDialog() != true) return;
                            var path = dialog.FileName;
                            if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException(HostText.Get("H341"));
                            await Task.Run(() => SaveThreadMarkdown(path, result.markdown));
                            Notify(HostText.Get("H342"));
                        }
                        else
                        {
                            Clipboard.SetText(result.markdown);
                            Notify(HostText.Get("H343"));
                        }
                        return;
                    }
                    await Task.Delay(150);
                }
                if (!_disposed) throw new InvalidOperationException(HostText.Get("H344"));
            }
            catch (Exception error)
            {
                if (!_disposed) Notify(error is InvalidOperationException ? error.Message
                    : HostText.Get("H345"), "error");
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
