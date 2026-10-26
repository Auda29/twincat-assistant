using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace TwinCatAssistant.Native
{
    public sealed class ActivePouSource
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; }
        public string Declaration { get; private set; }
        public string Implementation { get; private set; }

        public static ActivePouSource Parse(string text)
        {
            if (text == null || text.Length > 1048576) return null;
            using (var input = new StringReader(text))
            using (var xml = XmlReader.Create(input, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576
            }))
            {
                var root = XDocument.Load(xml).Root;
                var pous = root?.Name == "POU" ? new[] { root } : root?.Elements("POU").ToArray();
                if (pous?.Length != 1) return null;
                var pou = pous[0];
                Guid id;
                if (!Guid.TryParse((string)pou.Attribute("Id"), out id) || id == Guid.Empty
                    || string.IsNullOrWhiteSpace((string)pou.Attribute("Name"))) return null;
                var declaration = pou.Elements("Declaration").ToArray();
                var implementation = pou.Elements("Implementation").ToArray();
                if (declaration.Length != 1 || implementation.Length != 1) return null;
                var st = implementation[0].Elements().ToArray();
                if (st.Length != 1 || st[0].Name != "ST") return null;
                return new ActivePouSource { Id = id, Name = (string)pou.Attribute("Name"),
                    Declaration = declaration[0].Value, Implementation = st[0].Value };
            }
        }

        public static void Correlate(DirtyEvidence evidence, ActivePouSource live, ActivePouSource saved,
            string objectName, string declaration, string implementation)
        {
            if (live == null || saved == null || live.Id != saved.Id || live.Name != objectName || saved.Name != objectName) return;
            evidence.SavedSourceAvailable = true;
            evidence.LiveMatchesSaved = Normalize(declaration) == Normalize(saved.Declaration)
                && Normalize(implementation) == Normalize(saved.Implementation);
        }

        private static string Normalize(string text) { return text.Replace("\r\n", "\n"); }
    }
}
