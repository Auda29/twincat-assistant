using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace TwinCatAssistant.Native
{
    // One presentation locale. Protocol names, source code and chat contents
    // never pass through this catalog.
    public static partial class HostText
    {
        internal static readonly ResourceManager Resources = new ResourceManager("TwinCatAssistant.Native.Localization.Strings", typeof(HostText).Assembly);
        private static readonly ConditionalWeakTable<string, Message> Messages = new ConditionalWeakTable<string, Message>();
        public static readonly LanguageSource Source = new LanguageSource();
        public static event Action Changed;
        public static string Language { get { return Source.Language; } }
        public static CultureInfo Culture { get { return CultureInfo.GetCultureInfo(Language == "en" ? "en-US" : "de-DE"); } }

        public static void SetLanguage(string language)
        {
            if (language != "de" && language != "en") throw new ArgumentException("Supported UI languages are de and en.", nameof(language));
            if (language == Source.Language) return;
            Source.Set(language);
            Changed?.Invoke();
        }

        public static string Get(string key) { return Own(new Message(key, null)); }
        public static string Format(string key, params object[] arguments) { return Own(new Message(key, (object[])arguments.Clone())); }

        // Metadata is attached by reference, never by matching the displayed
        // text. A user title equal to a translated label remains user content.
        private static string Own(Message message)
        {
            var value = new string(message.Render().ToCharArray());
            Messages.Add(value, message);
            return value;
        }
        internal static Message Description(string value) { return value != null && Messages.TryGetValue(value, out var message) ? message : null; }
        internal static string Resolve(object value)
        {
            var text = value as string;
            return text != null && Messages.TryGetValue(text, out var message) ? message.Render() : Convert.ToString(value, Culture);
        }

        internal sealed class Message
        {
            internal readonly string Key;
            private readonly object[] _arguments;
            internal Message(string key, object[] arguments) { Key = key; _arguments = arguments; }
            internal string Render()
            {
                var text = Resources.GetString(Key, Culture);
                if (text == null) throw new InvalidOperationException("Missing native UI translation: " + Key);
                if (_arguments == null) return text;
                var values = new object[_arguments.Length];
                for (var i = 0; i < values.Length; i++) values[i] = _arguments[i] is string ? Resolve(_arguments[i]) : _arguments[i];
                return string.Format(Culture, text, values);
            }
        }

        public sealed class LanguageSource : INotifyPropertyChanged
        {
            public string Language { get; private set; } = "de";
            public event PropertyChangedEventHandler PropertyChanged;
            internal void Set(string language) { Language = language; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language))); }
        }
    }
}
