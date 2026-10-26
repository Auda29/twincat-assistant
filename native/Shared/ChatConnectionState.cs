namespace TwinCatAssistant.Native
{
    public sealed class ChatConnectionState
    {
        public long Generation { get; private set; }
        public ulong NavigationId { get; private set; }
        public string Phase { get; private set; } = "idle";
        public string Message { get; private set; } = "Connect chat starts the protected local service if needed.";
        public bool PageLoaded { get { return Phase == "loaded"; } }

        public long Begin()
        {
            Generation++;
            NavigationId = 0;
            Phase = "starting";
            Message = "Checking the local chat service...";
            return Generation;
        }
        public void Loading(long generation)
        {
            if (generation != Generation) return;
            Phase = "loading";
            Message = "Local service ready. Loading OpenChamber...";
        }
        public void Navigating(ulong navigationId)
        {
            NavigationId = navigationId;
            Phase = "loading";
            Message = "Loading OpenChamber...";
        }
        public void Completed(ulong navigationId, bool success, int httpStatus, string error)
        {
            if (navigationId != NavigationId || NavigationId == 0) return;
            if (!success || httpStatus < 200 || httpStatus >= 400)
            {
                Fail(Generation, "Chat page could not load (" + error + "). Check the service and retry Connect chat.");
                return;
            }
            Phase = "loaded";
            Message = "Chat page loaded. Sign in in OpenChamber. Native source is not attached.";
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
