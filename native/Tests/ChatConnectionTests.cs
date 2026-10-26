using TwinCatAssistant.Native;

internal static class ChatConnectionTests
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool condition, string label) {
            if (!condition) throw new InvalidOperationException("Chat state regression: " + label);
            Console.WriteLine("PASS " + label); count++;
        }
        var state = new ChatConnectionState();
        var first = state.Begin();
        Check(!state.PageLoaded, "a connect click is not a loaded page");
        state.Loading(first);
        Check(!state.PageLoaded, "runtime readiness is not a loaded page");
        state.Navigating(10);
        state.Completed(10, false, 0, "ConnectionRefused");
        Check(!state.PageLoaded && state.Message.Contains("ConnectionRefused"), "refused navigation displays an error instead of success");
        var second = state.Begin();
        state.Loading(second); state.Navigating(20);
        state.Completed(10, true, 200, "Unknown");
        Check(!state.PageLoaded, "stale navigation completion ignored");
        state.Fail(first, "stale startup failure");
        Check(state.Phase == "loading", "stale service failure cannot replace a newer connection");
        state.Completed(20, true, 500, "Unknown");
        Check(!state.PageLoaded, "HTTP error cannot become success");
        state.Begin(); state.Navigating(30); state.Completed(30, true, 200, "Unknown");
        Check(state.PageLoaded && state.Message.Contains("Anmeldung"), "successful load announces automatic sign-in without claiming authentication");
        state.Fail(state.Generation, "service stopped");
        state.Completed(30, true, 200, "Unknown");
        Check(!state.PageLoaded, "late completion cannot revive a stopped service");
        state.Dispose(); state.Completed(30, true, 200, "Unknown");
        Check(state.Phase == "disposed", "disposed view cannot publish completion");
        return count;
    }
}
