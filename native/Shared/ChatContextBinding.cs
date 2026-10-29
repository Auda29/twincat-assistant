using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace TwinCatAssistant.Native
{
    public sealed class ChatProviderChoice
    {
        public string id { get; set; }
        public string name { get; set; }
    }
    public sealed class ChatTheme
    {
        public bool dark { get; set; }
        public string background { get; set; }
        public string foreground { get; set; }
        public string muted { get; set; }
        public string border { get; set; }
        public string surface { get; set; }
        public string primary { get; set; }
        public string primaryForeground { get; set; }
        public string error { get; set; }
        public string warning { get; set; }
        public string success { get; set; }
    }
    public sealed class ChatSessionEntry
    {
        public string id { get; set; }
        public double updated { get; set; }
    }
    public sealed class ChatMcpStatus
    {
        public string name { get; set; }
        public string status { get; set; }
    }
    // A TwinCAT chip currently staged in the composer (reference ID and revision).
    public sealed class ChatAttachment
    {
        public string id { get; set; }
        public string title { get; set; }
        public string revision { get; set; }
        public string reference { get; set; }
    }

    public sealed class ChatSelection
    {
        public int version;
        public bool ready;
        public bool authenticated;
        public string sessionId;
        public string directory;
        public string documentId;
        public long epoch;
        public bool creating;
        public string attachedObject;
        public string attachedRevision;
        public ChatAttachment[] attachments;
        public string model;
        public ChatProviderChoice[] providers;
        public string[] allowedProviders;
        public ChatTheme theme;
        public ChatMcpStatus[] mcp;
        public bool IsSelected { get { return version == 1 && ready && authenticated && ValidSession(sessionId) && Guid.TryParse(documentId, out _) && epoch >= 0; } }
        public static bool ValidSession(string value) { return value != null && Regex.IsMatch(value, @"\A[a-zA-Z0-9_:-]{1,160}\z"); }
    }

    public sealed class ChatContextBinding
    {
        public int Version { get; set; } = 1;
        public string Origin { get; set; }
        public string SessionId { get; set; }
        public string IdeInstanceId { get; set; }
        public int IdeProcessId { get; set; }
        public string SolutionFile { get; set; }
        public string ProjectFile { get; set; }
        public bool IsGeneral { get; set; }

        public bool SameProject(ActiveEditorIdentity identity)
        {
            return SameProjectContext(identity?.ProjectIdentity);
        }
        public bool SameProjectContext(NativeProjectIdentity identity)
        {
            return Version == 1 && !IsGeneral && identity != null
                && string.Equals(SolutionFile, identity.SolutionFile, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ProjectFile, identity.ProjectFile, StringComparison.OrdinalIgnoreCase);
        }
        public bool MatchesGeneral(string instanceId, int processId, string origin, string sessionId)
        {
            return IsGeneral && Version == 1 && IdeInstanceId == instanceId && IdeProcessId == processId
                && Origin == origin && SessionId == sessionId && ChatSelection.ValidSession(sessionId);
        }
        public static ChatContextBinding CreateGeneral(string instanceId, int processId, string origin, string sessionId)
        {
            ValidateSession(processId, instanceId, origin, sessionId);
            return new ChatContextBinding { IsGeneral = true, Origin = origin, SessionId = sessionId,
                IdeInstanceId = instanceId, IdeProcessId = processId };
        }
        public bool Matches(ActiveEditorIdentity identity, string instanceId, string origin, string sessionId)
        {
            return MatchesProject(identity?.ProjectIdentity, instanceId, origin, sessionId);
        }
        public bool MatchesProject(NativeProjectIdentity identity, string instanceId, string origin, string sessionId)
        {
            return SameProjectContext(identity) && IdeProcessId == identity.IdeProcessId
                && IdeInstanceId == instanceId && Origin == origin && SessionId == sessionId && ChatSelection.ValidSession(sessionId);
        }
        public static ChatContextBinding Create(ActiveEditorIdentity identity, string instanceId, string origin, string sessionId)
        {
            return CreateProject(identity?.ProjectIdentity, instanceId, origin, sessionId);
        }
        public static ChatContextBinding CreateProject(NativeProjectIdentity identity, string instanceId, string origin, string sessionId)
        {
            if (identity == null) throw new InvalidOperationException("An exact project identity is required.");
            ValidateSession(identity.IdeProcessId, instanceId, origin, sessionId);
            return new ChatContextBinding { Origin = origin, SessionId = sessionId, IdeInstanceId = instanceId,
                IdeProcessId = identity.IdeProcessId, SolutionFile = identity.SolutionFile, ProjectFile = identity.ProjectFile };
        }
        private static void ValidateSession(int processId, string instanceId, string origin, string sessionId)
        {
            if (processId <= 0 || string.IsNullOrEmpty(instanceId) || !ChatSelection.ValidSession(sessionId)
                || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1"
                || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "")
                throw new InvalidOperationException("An exact project, IDE and local chat session are required.");
        }
    }

    public static class ChatContextPolicy
    {
        public static string Hash(string text)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        public static string Revision(ActiveEditorSnapshot snapshot)
        {
            if (snapshot?.Identity == null) return null;
            var identity = snapshot.Identity;
            return Hash(identity.IdeProcessId + "\0" + identity.SolutionFile + "\0" + identity.ProjectFile + "\0"
                + identity.DocumentMoniker + "\0" + identity.TreePath + "\0" + snapshot.Declaration + "\0" + snapshot.Implementation
                + "\0" + snapshot.Dirty?.State + "\0" + snapshot.SourceKind + "\0" + snapshot.ContextGeneration);
        }
        public static string AttachmentText(ActiveEditorSnapshot snapshot)
        {
            if (snapshot?.Identity == null) throw new InvalidOperationException("No active POU available.");
            var result = "TwinCAT context snapshot\nObject: " + snapshot.ObjectName
                + "\nIDE PID: " + snapshot.Identity.IdeProcessId + "\nSolution: " + snapshot.Identity.SolutionFile
                + "\nPLC project: " + snapshot.Identity.ProjectFile + "\nTree path: " + snapshot.Identity.TreePath
                + "\nSource: " + snapshot.SourceKind + "\nChanges: " + (snapshot.Dirty?.State ?? "unknown")
                + "\nUnsaved editor text is not verified. This snapshot is read-only and may be outdated."
                + "\nRevision: " + Revision(snapshot) + "\n\nDeclaration:\n" + snapshot.Declaration
                + "\n\nStructured Text implementation:\n" + snapshot.Implementation;
            if (result.Length > 16000) throw new InvalidOperationException("This POU exceeds the direct chat attachment limit. No partial source was attached.");
            return result;
        }
        // Automatic attachment always uses this chip ID. The composer then
        // replaces the chip in place when another POU becomes active.
        public const string ActiveReferenceId = "Aktiver Baustein";

        public static string ReferenceId(ActiveEditorSnapshot snapshot)
        {
            var name = snapshot.ObjectName ?? "POU";
            var project = Path.GetFileNameWithoutExtension(snapshot.Identity.ProjectFile);
            // This is only the composer's display/deduplication key. Native
            // authorization always uses full identity and the full revision.
            return name.Substring(0, Math.Min(name.Length, 24)) + "@" + project.Substring(0, Math.Min(project.Length, 24))
                + "#" + Hash(snapshot.Identity.DocumentMoniker + "\0" + snapshot.Identity.TreePath).Substring(0, 16);
        }
        // Connecting a provider in OpenChamber is the operator's consent. Enable
        // exactly one connected provider when none of them is enabled yet,
        // preferring the provider of the model already chosen in the composer.
        public static string ProviderToEnable(ChatSelection selection)
        {
            var connected = new System.Collections.Generic.List<string>();
            foreach (var choice in selection?.providers ?? new ChatProviderChoice[0])
                if (choice?.id != null && Regex.IsMatch(choice.id, @"\A[a-zA-Z0-9_-]{1,80}\z") && !connected.Contains(choice.id)) connected.Add(choice.id);
            if (connected.Count == 0) return null;
            foreach (var allowed in selection.allowedProviders ?? new string[0])
                if (connected.Contains(allowed)) return null;
            var slash = selection.model?.IndexOf('/') ?? -1;
            var modelProvider = slash > 0 ? selection.model.Substring(0, slash) : null;
            return modelProvider != null && connected.Contains(modelProvider) ? modelProvider : connected[0];
        }
        public static bool IsPromptRequest(string method, Uri uri, out string sessionId)
        {
            sessionId = null;
            if (method != "POST" || uri == null) return false;
            var match = Regex.Match(Uri.UnescapeDataString(uri.AbsolutePath), @"\A/api/session/([a-zA-Z0-9_:-]+)/(message|prompt|prompt_async|command|shell)/?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) return false;
            sessionId = match.Groups[1].Value;
            return true;
        }
        public static bool BlocksMutatingFeature(string method, Uri uri)
        {
            if (uri == null || method == "GET" || method == "HEAD" || method == "OPTIONS") return false;
            var path = Uri.UnescapeDataString(uri.AbsolutePath).ToLowerInvariant();
            foreach (var prefix in new[] { "/api/fs", "/api/git", "/api/source-control", "/api/terminal", "/api/shell", "/api/dev-servers" })
                if (path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal)) return true;
            return Regex.IsMatch(path, @"\A/api/session/[^/]+/shell/?\z")
                || path.StartsWith("/api/opencode/upgrade", StringComparison.Ordinal)
                || path.StartsWith("/api/opencode/install", StringComparison.Ordinal)
                || path.StartsWith("/api/openchamber/update", StringComparison.Ordinal);
        }
    }
}
