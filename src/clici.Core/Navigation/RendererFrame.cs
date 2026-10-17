namespace Clici.Core.Navigation;

// One passive publication from an agent that cannot speak the stdin protocol
// directly (Claude Code plugins have no child stdin stream). The agent stamps
// its own wall clock; the helper translates it to its monotonic clock and
// refuses frames that are too old to carry a fresh input lease.
public sealed record RendererFrame(int Version, long Sequence, long ObservedAt, string Agent,
    string SessionId, long Epoch, bool ComposerOwnsInput, bool HistoryComplete,
    IReadOnlyList<RenderedPromptBinding> Prompts);

public static class RendererFrameClock
{
    public const long MaximumAgeMilliseconds = 750;
    private const long MaximumFutureMilliseconds = 1000;

    // The monotonic time the agent observed its state, or null when the frame
    // is older than one input lease or stamped in the future (clock skew).
    public static long? ObservedTick(long observedAtUnixMilliseconds, long nowUnixMilliseconds, long nowTick)
    {
        var age = nowUnixMilliseconds - observedAtUnixMilliseconds;
        if (age > MaximumAgeMilliseconds || age < -MaximumFutureMilliseconds) return null;
        return nowTick - Math.Max(0, age);
    }
}
