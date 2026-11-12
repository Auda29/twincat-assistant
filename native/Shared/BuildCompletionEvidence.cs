namespace TwinCatAssistant.Native
{
    // Missing, duplicated or unrelated project events never prove a completed run.
    public sealed class BuildCompletionEvidence
    {
        private int begins, ends;
        private bool invalid;
        public bool Succeeded { get; private set; }
        public bool CanPublish { get { return !invalid && begins == 1 && ends == 1; } }
        public void Begin(bool matches)
        { if (!matches || ++begins != 1 || ends != 0) invalid = true; }
        public void End(bool matches, bool success)
        { if (!matches || begins != 1 || ++ends != 1) invalid = true; Succeeded = success; }
    }
}
