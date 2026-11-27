using TwinCatAssistant.Native;

internal static class ChatProfileRequestTests
{
    internal static int Run()
    {
        var document = Guid.NewGuid().ToString();
        ChatSelection Selection(long id = 12, string profile = "twincat-debug", string session = "chat",
            string directory = "workspace", long epoch = 2) => new ChatSelection
        {
            version = 1, ready = true, authenticated = true, documentId = document, epoch = epoch,
            sessionId = session, directory = directory, profileRequest = new ChatProfileRequest { id = id, profile = profile }
        };
        var initial = Selection();
        var request = initial.profileRequest;
        var otherDocument = Selection();
        otherDocument.documentId = Guid.NewGuid().ToString();
        var noProfile = Selection();
        noProfile.profileRequest = null;
        var unready = Selection();
        unready.authenticated = false;
        var cases = new[] {
            ("profile request accepts the same fresh request", Selection(), true),
            ("profile request rejects an older request", Selection(id: 11), false),
            ("profile request rejects a superseding request", Selection(id: 13), false),
            ("profile request rejects a different profile under the same ID", Selection(profile: "twincat-editor"), false),
            ("profile request rejects a stale epoch", Selection(epoch: 1), false),
            ("profile request rejects another session", Selection(session: "other"), false),
            ("profile request rejects another directory", Selection(directory: "other"), false),
            ("profile request rejects another document", otherDocument, false),
            ("profile request rejects a cleared request", noProfile, false),
            ("profile request rejects an unauthenticated selection", unready, false)
        };
        foreach (var (name, selection, expected) in cases)
        {
            if (request.Matches(initial, selection) != expected) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }
        return cases.Length;
    }
}
