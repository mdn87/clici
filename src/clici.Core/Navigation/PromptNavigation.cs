namespace Clici.Core.Navigation;

public enum NavigationDirection { Previous, Next }
public enum PaneEligibility { Unknown, Ineligible, Eligible }
public enum NavigationAction { PassThrough, Jump, ReturnToLive, NoOp, Refused, CaptureLimit }
public enum NavigationReason { None, PaneNotEligible, InvalidSnapshot, StaleCursor, OutsideCapture, OldestPrompt, AlreadyLive }
public enum AgentKind { ClaudeCode, Codex }

// Native IDs are namespaced by agent and session. SessionEpoch still separates
// incarnations of a resumed/cleared session even if its native ID is reused.
public sealed record AgentSessionIdentity(AgentKind Agent, string SessionId);

// Positions are meaningful only within this pane/session/layout, never within a
// string slice. Reflow and eviction must advance LayoutEpoch in the adapter.
public sealed record NavigationIdentity(string PaneId, long SessionEpoch, long LayoutEpoch, AgentSessionIdentity Session);

// Only complete, positively recognized rendered user messages belong here.
// MessageId is the agent's immutable message ID, never prompt text or a text hash.
// ContextKey identifies the rendered occurrence's evidence, independently of that ID.
public sealed record PromptOccurrence(long Start, long End, string ContextKey, string MessageId);

public sealed record NavigationSnapshot(
    NavigationIdentity Identity,
    long ObservationRevision,
    PaneEligibility Eligibility,
    long CaptureStart,
    long CaptureEnd,
    bool ReachesHistoryStart,
    bool ReachesLiveEnd,
    bool BoundariesComplete,
    long ViewportStart,
    bool AtLiveOutput,
    IReadOnlyList<PromptOccurrence> Prompts);

// The caller may create a cursor only after reading back a successful jump.
// Clear it on manual scrolling, return to live, disable, or a session change.
public sealed record NavigationCursor(NavigationIdentity Identity, PromptOccurrence Prompt);

public sealed record NavigationDecision(
    NavigationAction Action,
    NavigationReason Reason = NavigationReason.None,
    PromptOccurrence? Target = null);

public static class PromptNavigation
{
    public static NavigationDecision Decide(NavigationSnapshot snapshot,
        NavigationDirection direction, NavigationCursor? cursor = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Eligibility != PaneEligibility.Eligible)
        {
            return new(NavigationAction.PassThrough, NavigationReason.PaneNotEligible);
        }

        if (!Enum.IsDefined(direction) || !Valid(snapshot))
        {
            return new(NavigationAction.Refused, NavigationReason.InvalidSnapshot);
        }

        var prompts = snapshot.Prompts;
        if (cursor is not null)
        {
            var index = -1;
            for (var i = 0; i < prompts.Count; i++)
            {
                if (prompts[i] == cursor.Prompt)
                {
                    index = i;
                    break;
                }
            }

            if (cursor.Identity != snapshot.Identity || index < 0)
            {
                return new(NavigationAction.Refused, NavigationReason.StaleCursor);
            }

            return direction == NavigationDirection.Previous
                ? index > 0 ? Jump(prompts[index - 1]) : BeforeFirst(snapshot)
                : index + 1 < prompts.Count ? Jump(prompts[index + 1]) : AfterLast(snapshot);
        }

        if (snapshot.AtLiveOutput)
        {
            if (direction == NavigationDirection.Next)
            {
                return new(NavigationAction.NoOp, NavigationReason.AlreadyLive);
            }

            if (!snapshot.ReachesLiveEnd)
            {
                return Limit();
            }

            return prompts.Count > 0 ? Jump(prompts[^1]) : BeforeFirst(snapshot);
        }

        if (snapshot.ViewportStart < snapshot.CaptureStart || snapshot.ViewportStart > snapshot.CaptureEnd)
        {
            return Limit();
        }

        if (direction == NavigationDirection.Previous)
        {
            for (var i = prompts.Count - 1; i >= 0; i--)
            {
                if (prompts[i].Start < snapshot.ViewportStart)
                {
                    return Jump(prompts[i]);
                }
            }

            return BeforeFirst(snapshot);
        }

        foreach (var prompt in prompts)
        {
            if (prompt.Start > snapshot.ViewportStart)
            {
                return Jump(prompt);
            }
        }

        return AfterLast(snapshot);
    }

    private static bool Valid(NavigationSnapshot snapshot)
    {
        if (!ValidIdentity(snapshot.Identity) ||
            !snapshot.BoundariesComplete || snapshot.Prompts is null ||
            snapshot.CaptureStart < 0 || snapshot.CaptureEnd < snapshot.CaptureStart)
        {
            return false;
        }

        var previousEnd = snapshot.CaptureStart;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prompt in snapshot.Prompts)
        {
            if (prompt is null || prompt.Start < previousEnd || prompt.End <= prompt.Start ||
                prompt.End > snapshot.CaptureEnd || string.IsNullOrWhiteSpace(prompt.ContextKey) ||
                string.IsNullOrWhiteSpace(prompt.MessageId) || !ids.Add(prompt.MessageId))
            {
                return false;
            }

            previousEnd = prompt.End;
        }

        return true;
    }

    internal static bool ValidIdentity(NavigationIdentity? identity) => identity is not null &&
        !string.IsNullOrWhiteSpace(identity.PaneId) && identity.SessionEpoch >= 0 && identity.LayoutEpoch >= 0 &&
        identity.Session is not null && Enum.IsDefined(identity.Session.Agent) &&
        !string.IsNullOrWhiteSpace(identity.Session.SessionId);

    private static NavigationDecision Jump(PromptOccurrence prompt) => new(NavigationAction.Jump, Target: prompt);
    private static NavigationDecision Limit() => new(NavigationAction.CaptureLimit, NavigationReason.OutsideCapture);
    private static NavigationDecision BeforeFirst(NavigationSnapshot snapshot) => snapshot.ReachesHistoryStart
        ? new(NavigationAction.NoOp, NavigationReason.OldestPrompt) : Limit();
    private static NavigationDecision AfterLast(NavigationSnapshot snapshot) => snapshot.ReachesLiveEnd
        ? new(NavigationAction.ReturnToLive) : Limit();
}
