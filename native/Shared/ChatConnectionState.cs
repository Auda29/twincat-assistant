namespace TwinCatAssistant.Native
{
    public sealed class ChatConnectionState
    {
        public long Generation { get; private set; }
        public ulong NavigationId { get; private set; }
        public string Phase { get; private set; } = "idle";
        public string Message { get; private set; } = "Der lokale Chat startet beim Öffnen automatisch.";
        public bool PageLoaded { get { return Phase == "loaded"; } }

        public long Begin()
        {
            Generation++;
            NavigationId = 0;
            Phase = "starting";
            Message = "Lokaler Chat-Dienst wird geprüft …";
            return Generation;
        }
        public void Loading(long generation)
        {
            if (generation != Generation) return;
            Phase = "loading";
            Message = "Dienst bereit. OpenChamber wird geladen …";
        }
        public void Navigating(ulong navigationId)
        {
            NavigationId = navigationId;
            Phase = "loading";
            Message = "OpenChamber wird geladen …";
        }
        public void Completed(ulong navigationId, bool success, int httpStatus, string error)
        {
            if (navigationId != NavigationId || NavigationId == 0) return;
            if (!success || httpStatus < 200 || httpStatus >= 400)
            {
                Fail(Generation, "Chat-Seite konnte nicht geladen werden (" + error + "). Dienst prüfen und neu verbinden.");
                return;
            }
            Phase = "loaded";
            Message = "Chat-Seite geladen. Anmeldung erfolgt automatisch; nativer Quelltext ist nicht angehängt.";
        }
        public void Fail(long generation, string message)
        {
            if (generation != Generation) return;
            Phase = "failed";
            NavigationId = 0;
            Message = message;
        }
        public void Dispose() { Generation++; NavigationId = 0; Phase = "disposed"; }
    }
}
