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
        public Guid OwnerId { get; private set; }
        public string OwnerName { get; private set; }
        public string Kind { get; private set; }

        public static ActivePouSource Parse(string text)
        {
            return Parse(text, new string[0]);
        }

        // Follow only the exact member chain inside this POU. A same-named
        // method in another POU, or the POU body itself, is never a substitute.
        public static ActivePouSource Parse(string text, string[] members)
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
                Guid ownerId;
                if (!Guid.TryParse((string)pou.Attribute("Id"), out ownerId) || ownerId == Guid.Empty
                    || string.IsNullOrWhiteSpace((string)pou.Attribute("Name"))) return null;
                var node = pou;
                foreach (var member in members)
                {
                    var matches = node.Elements().Where(child =>
                        (node == pou && (child.Name == "Action" || child.Name == "Method" || child.Name == "Property")
                            || node.Name == "Property" && (child.Name == "Get" || child.Name == "Set"))
                        && string.Equals((string)child.Attribute("Name") ?? child.Name.LocalName, member, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length != 1) return null;
                    node = matches[0];
                }
                Guid id;
                if (!Guid.TryParse((string)node.Attribute("Id"), out id) || id == Guid.Empty) return null;
                var declaration = DeclarationOf(node);
                var implementation = node.Name == "Property" ? PropertyBody(node) : ImplementationOf(node);
                if (declaration == null || implementation == null) return null;
                return new ActivePouSource { Id = id, OwnerId = ownerId, OwnerName = (string)pou.Attribute("Name"),
                    Name = (string)node.Attribute("Name") ?? node.Name.LocalName, Kind = node.Name.LocalName,
                    Declaration = declaration, Implementation = implementation };
            }
        }

        private static string DeclarationOf(XElement node)
        {
            var declarations = node.Elements("Declaration").ToArray();
            return declarations.Length == 1 ? declarations[0].Value : declarations.Length == 0
                && (node.Name == "Action" || node.Name == "Get" || node.Name == "Set") ? "" : null;
        }

        private static string ImplementationOf(XElement node)
        {
            var implementation = node.Elements("Implementation").ToArray();
            if (implementation.Length != 1) return null;
            var body = implementation[0].Elements().ToArray();
            return body.Length == 1 && body[0].Name == "ST" ? body[0].Value : null;
        }

        private static string PropertyBody(XElement property)
        {
            var parts = new string[4];
            for (var index = 0; index < 2; index++)
            {
                var nodes = property.Elements(index == 0 ? "Get" : "Set").ToArray();
                if (nodes.Length > 1) return null;
                if (nodes.Length == 0) continue;
                parts[index * 2] = DeclarationOf(nodes[0]);
                parts[index * 2 + 1] = ImplementationOf(nodes[0]);
                if (parts[index * 2] == null || parts[index * 2 + 1] == null) return null;
            }
            return PropertyImplementation(parts[0], parts[1], parts[2], parts[3]);
        }

        internal static string PropertyImplementation(string getDeclaration, string getBody, string setDeclaration, string setBody)
        {
            return string.Join("\n\n", new[] {
                getBody == null ? null : "// GET declaration\n" + getDeclaration + "\n// GET implementation\n" + getBody,
                setBody == null ? null : "// SET declaration\n" + setDeclaration + "\n// SET implementation\n" + setBody
            }.Where(part => part != null));
        }

        public static void Correlate(DirtyEvidence evidence, ActivePouSource live, ActivePouSource saved,
            string objectName, string declaration, string implementation)
        {
            evidence.SavedSourceAvailable = false;
            evidence.LiveMatchesSaved = false;
            if (live == null || saved == null || live.Id != saved.Id || live.OwnerId != saved.OwnerId
                || live.OwnerName != saved.OwnerName || live.Kind != saved.Kind || live.Name != objectName || saved.Name != objectName) return;
            // IDs alone do not prove that XML and the independently captured AI
            // text came from one stable read. Never label a torn snapshot clean.
            if (Normalize(declaration) != Normalize(live.Declaration)
                || Normalize(implementation) != Normalize(live.Implementation))
            { evidence.EvidenceFailed = true; return; }
            evidence.SavedSourceAvailable = true;
            evidence.LiveMatchesSaved = Normalize(declaration) == Normalize(saved.Declaration)
                && Normalize(implementation) == Normalize(saved.Implementation);
        }

        private static string Normalize(string text) { return text.Replace("\r\n", "\n"); }
    }
}
