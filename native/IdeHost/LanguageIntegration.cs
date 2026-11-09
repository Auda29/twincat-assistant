using System;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private string _languageAwaitingBrowser;
        private long _languageObservationVersion;

        private void InitializeLanguage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            HostText.SetLanguage(_chatPreferences.Language ?? "de");
            HostText.Changed += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            // Existing controls bind only strings produced by HostText. User
            // titles, source previews and drafts are never lookup candidates.
            _menuPopup.IsOpen = false;
            RenderChatBinding();
            RenderDiagnostics();
            _projectChatPicker?.RefreshLanguage();
            ApplyLanguageBindings();
        }

        private void ApplyLanguageBindings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_disposed) return;
            HostTextBindings.Apply(this);
            HostTextBindings.Apply(_menuItems);
            HostTextBindings.Apply(_previewPopup.Child);
        }

        private void RememberLanguage(string language)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (language != "de" && language != "en") return;
            HostText.SetLanguage(language);
            if (_chatPreferences.Language == language) return;
            _chatPreferences.Language = language;
            try { _chatPreferences.Save(); }
            catch { Notify(HostText.Get("LanguageSaveFailed"), "warning"); }
        }

        private async Task SelectLanguageAsync(string language)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || (language != "de" && language != "en")) return;
            _languageAwaitingBrowser = language;
            _languageObservationVersion++;
            RememberLanguage(language);
            if (_browser.CoreWebView2 != null && Allowed(_browser.CoreWebView2.Source))
            {
                var document = _chatDocumentGeneration;
                var value = new JavaScriptSerializer().Serialize(language);
                var acknowledged = await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatChat?.setLanguage(" + value + ")");
                if (_disposed || document != _chatDocumentGeneration) return;
                if (acknowledged == value && _languageAwaitingBrowser == language)
                {
                    _languageAwaitingBrowser = null;
                    // A sample started before this acknowledgement may still
                    // contain the previous locale. It must not undo the choice.
                    _languageObservationVersion++;
                }
            }
            RenderChatBinding();
            ApplyLanguageBindings();
        }

        private void ObserveChatLanguage(string language)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (language != "de" && language != "en") return;
            if (_languageAwaitingBrowser != null && language != _languageAwaitingBrowser) return;
            _languageAwaitingBrowser = null;
            RememberLanguage(language);
        }
    }
}
