using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using TwinCatAssistant.Native;

internal static class HostTextTests
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); count++; }
        var de = HostText.Resources.GetResourceSet(CultureInfo.GetCultureInfo("de"), true, true)!;
        var en = HostText.Resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)!;
        Check(en != null, "English satellite resources exist without German fallback");
        var deKeys = de.Cast<System.Collections.DictionaryEntry>().Select(x => (string)x.Key).ToHashSet();
        var enKeys = en.Cast<System.Collections.DictionaryEntry>().Select(x => (string)x.Key).ToHashSet();
        Check(deKeys.SetEquals(enKeys) && deKeys.Count >= 329, "Native resource keys have full DE/EN parity");
        foreach (var key in deKeys)
        {
            var german = de.GetString(key)!; var english = en.GetString(key)!;
            Check(!string.IsNullOrWhiteSpace(german) && !string.IsNullOrWhiteSpace(english), "Nonempty translation: " + key);
            string[] Slots(string text) => Regex.Matches(text, @"(?<!\{)\{\d+\}").Select(m => m.Value).Order().ToArray();
            Check(Slots(german).SequenceEqual(Slots(english)), "Format parameter parity: " + key);
        }
        try
        {
            HostText.SetLanguage("de");
            var saved = HostText.Get("H060");
            var userTitle = new string(saved.ToCharArray());
            var code = "\tIF Sprache = 'Deutsch' THEN\r\n\tEND_IF";
            var message = HostText.Format("H050", code);
            HostText.SetLanguage("en");
            Check(HostText.Get("H060") == "Saved", "English native label");
            Check(HostText.Description(saved)!.Render() == "Saved", "An existing UI label resolves the new language");
            Check(HostText.Description(userTitle) == null && userTitle == "Gespeichert", "An identical user title is never treated as a UI resource");
            Check(HostText.Description(message)!.Render().Contains(code, StringComparison.Ordinal), "Code parameter stays byte-identical across a language switch");
            Check(HostText.Get("H033").Contains("TWINCAT_READ_ONLY", StringComparison.Ordinal), "HTTP error protocol identity is not translated");
            Check(HostText.Culture.Name == "en-US", "English number/date culture");
            HostText.SetLanguage("de");
            Check(HostText.Get("H060") == "Gespeichert" && HostText.Culture.Name == "de-DE", "German round trip");
            Check(HostText.DisplayStatus("Shell executable version 16.0 differs from the alpha target.").Contains("16.0", StringComparison.Ordinal), "Host-version diagnostic keeps its dynamic value");
            Check(HostText.DisplayStatus("TwinCAT 3.1.4024.79 is not a verified build of the alpha target.").StartsWith("TwinCAT 3.1.4024.79 ist", StringComparison.Ordinal), "Host-build diagnostic is localized without translating its version");
            Check(HostText.Description(message)!.Render().Contains(code, StringComparison.Ordinal), "German round trip retains source parameter");
            try { HostText.SetLanguage("fr"); throw new InvalidOperationException("Unsupported locale accepted"); }
            catch (ArgumentException) { Check(HostText.Language == "de", "Invalid locale leaves selection unchanged"); }
        }
        finally { HostText.SetLanguage("de"); }
        Console.WriteLine("PASS " + count + " native localization checks");
        return count;
    }
}
