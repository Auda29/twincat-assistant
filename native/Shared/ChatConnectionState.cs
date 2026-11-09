namespace TwinCatAssistant.Native
{
    public sealed class ChatConnectionState
    {
        public long Generation { get; private set; }
        public ulong NavigationId { get; private set; }
        public string Phase { get; private set; } = "idle";
        public string Message { get; private set; } = HostText.Get("ConnectionIdle");
        public bool PageLoaded { get { return Phase == "loaded"; } }

        public long Begin()
        {
            Generation++;
            NavigationId = 0;
            Phase = "starting";
            Message = HostText.Get("ConnectionStarting");
            return Generation;
        }
        public void Loading(long generation)
        {
            if (generation != Generation) return;
            Phase = "loading";
            Message = HostText.Get("ConnectionReady");
        }
        public void Navigating(ulong navigationId)
        {
            NavigationId = navigationId;
            Phase = "loading";
            Message = HostText.Get("ConnectionLoading");
        }
        public void Completed(ulong navigationId, bool success, int httpStatus, string error)
        {
            if (navigationId != NavigationId || NavigationId == 0) return;
            if (!success || httpStatus < 200 || httpStatus >= 400)
            {
                Fail(Generation, HostText.Format("ConnectionFailed", error));
                return;
            }
            Phase = "loaded";
            Message = HostText.Get("ConnectionLoaded");
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
