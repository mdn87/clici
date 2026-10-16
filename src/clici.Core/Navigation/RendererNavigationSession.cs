namespace Clici.Core.Navigation;

/// <summary>Requires a native input observation made after each intercepted press.
/// A cached lease admits the key; it never by itself authorizes scrolling.</summary>
public sealed class RendererNavigationSession
{
    private readonly object gate = new();
    private readonly NavigationInputQueue queue = new(NavigationBindings.Default);
    private NavigationInputRequest? waiting;
    private long observedAt = -1;

    public RendererNavigationSession() => queue.SetEnabled(true);

    public void Publish(NavigationInputContext context, long nativeObservedAt)
    {
        lock (gate)
        {
            if (nativeObservedAt <= observedAt) return;
            observedAt = nativeObservedAt;
            queue.UpdateContext(context);
        }
    }

    public void Invalidate()
    {
        lock (gate) { queue.Invalidate(); waiting = null; }
    }

    public NavigationKeyResult Handle(NavigationKeyInput input, long now)
    {
        lock (gate) { return queue.Handle(input, now); }
    }

    public NavigationInputRequest? TakeConfirmed(long now)
    {
        lock (gate)
        {
            if (waiting is not null && !queue.CanExecute(waiting, now)) waiting = null;
            waiting ??= queue.Take(now);
            return waiting is not null && observedAt > waiting.AcceptedAtMilliseconds ? waiting : null;
        }
    }

    public bool CanExecute(NavigationInputRequest request, long now) => queue.CanExecute(request, now);

    public bool Complete(NavigationInputRequest request, long now)
    {
        lock (gate)
        {
            var result = queue.Complete(request, now);
            waiting = null;
            return result;
        }
    }
}
