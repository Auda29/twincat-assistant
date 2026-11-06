using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TwinCatAssistant.Native
{
    // The installer owns this selector. It is colocated with the loaded host,
    // so the extension snapshot and its package selection roll back together.
    internal static class NativeInstallationSelection
    {
        internal const string TargetId = "tcxaeshell-x86-tc4024";
        internal static string SelectRoot(string selectorPath)
        {
            if (!File.Exists(selectorPath)) {
                if (Directory.Exists(selectorPath)) throw new InvalidOperationException("Invalid installation selection.");
                try { File.GetAttributes(selectorPath); }
                catch (FileNotFoundException) { return null; }
                catch (DirectoryNotFoundException) { return null; }
                throw new InvalidOperationException("Invalid installation selection.");
            }
            var selector = Parse(ReadRegular(selectorPath, 65536));
            if (selector.Count != 4 || !selector.ContainsKey("schemaVersion") || !selector.ContainsKey("targetId")
                || !selector.ContainsKey("version") || !selector.ContainsKey("applicationRoot")
                || !(selector["schemaVersion"] is int) || (int)selector["schemaVersion"] != 1
                || !(selector["targetId"] is string) || (string)selector["targetId"] != TargetId
                || !(selector["version"] is string) || string.IsNullOrWhiteSpace((string)selector["version"])
                || !(selector["applicationRoot"] is string)) throw new InvalidOperationException("Invalid installation selection.");
            var root = (string)selector["applicationRoot"];
            if (root.Length > 2048 || !Path.IsPathRooted(root) || !string.Equals(Path.GetFullPath(root), root, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Invalid application directory.");
            var manifest = Parse(ReadRegular(Path.Combine(root, "application-manifest.json"), 32 * 1024 * 1024));
            if (manifest.Count != 5 || !manifest.ContainsKey("schemaVersion") || !(manifest["schemaVersion"] is int) || (int)manifest["schemaVersion"] != 1
                || !manifest.ContainsKey("version") || !(manifest["version"] is string) || (string)manifest["version"] != (string)selector["version"]
                || !manifest.ContainsKey("platform") || !(manifest["platform"] is string) || (string)manifest["platform"] != "win32"
                || !manifest.ContainsKey("architecture") || !(manifest["architecture"] is string)
                || !new[] { "x64", "arm64", "ia32" }.Contains((string)manifest["architecture"])
                || !manifest.ContainsKey("files") || !(manifest["files"] is object[])) throw new InvalidOperationException("Invalid application manifest.");
            var entries = (object[])manifest["files"];
            foreach (var name in new[] { "runtime/node/node.exe", "runtime/mcp/default-settings.json", "runtime/mcp/knowledge/twincat-knowledge.exe", "runtime/mcp/engineering/packages/mcp/dist/index.js", "src/native-chat.mjs", "src/native-mcp.mjs", "src/native-knowledge.mjs", "src/native-chat-model.mjs", "src/package-runtime.json", "src/package-integrity.mjs", "scripts/application-manifest.mjs", "native/WindowsHost/bin/Release/net8.0-windows/WindowsHost.exe" })
                VerifyEntry(root, name, entries);
            return root;
        }
        private static void VerifyEntry(string root, string name, object[] entries)
        {
            var found = entries.OfType<Dictionary<string, object>>().Where(x => x.ContainsKey("path") && x["path"] is string && (string)x["path"] == name).ToArray();
            if (found.Length != 1 || found[0].Count != 3 || !found[0].ContainsKey("sha256") || !(found[0]["sha256"] is string)
                || !found[0].ContainsKey("size")) throw new InvalidOperationException("Missing package entry.");
            var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            var parts = name.Split('/'); var current = root;
            foreach (var part in parts) { current = Path.Combine(current, part); if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected package path."); }
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != Convert.ToInt64(found[0]["size"])) throw new InvalidOperationException("Package entry size mismatch.");
            using (var sha = SHA256.Create()) using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(digest, (string)found[0]["sha256"], StringComparison.Ordinal)) throw new InvalidOperationException("Package entry hash mismatch.");
            }
        }
        private static Dictionary<string, object> Parse(byte[] bytes)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 32 };
            var result = serializer.DeserializeObject(new UTF8Encoding(false, true).GetString(bytes)) as Dictionary<string, object>;
            if (result == null) throw new InvalidOperationException("Invalid JSON document.");
            return result;
        }
        private static byte[] ReadRegular(string path, int limit)
        {
            var info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || info.Length > limit)
                throw new IOException("Invalid installation metadata.");
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192]; int read;
                while ((read = file.Read(buffer, 0, buffer.Length)) > 0) { if (output.Length + read > limit) throw new IOException(); output.Write(buffer, 0, read); }
                return output.ToArray();
            }
        }
    }
}
