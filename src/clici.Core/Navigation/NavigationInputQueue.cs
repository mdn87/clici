namespace Clici.Core.Navigation;

[Flags]
public enum NavigationModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }
public enum NavigationKeyResult { PassThrough, Queued, Consumed, QueueFull }
public sealed record NavigationChord(int VirtualKey, NavigationModifiers Modifiers);
public sealed record NavigationBindings(NavigationChord Previous, NavigationChord Next)
{
    // VK_OEM_COMMA / VK_OEM_PERIOD: Alt+< and Alt+> on a US keyboard.
    public static NavigationBindings Default { get; } = new(
        new(0xBC, NavigationModifiers.Alt | NavigationModifiers.Shift),
        new(0xBE, NavigationModifiers.Alt | NavigationModifiers.Shift));
}

// Only a positively eligible, foreground, focused pane may publish a context.
// The adapter must withdraw it on focus loss, agent exit, a dialog, or uncertainty.
// Times are monotonic milliseconds, not wall-clock timestamps.
public sealed record NavigationInputContext(NavigationIdentity Identity, long EligibilityRevision, long ValidUntilMilliseconds);
public readonly record struct NavigationKeyInput(int VirtualKey, NavigationModifiers Modifiers, bool IsDown,
    bool IsRepeat = false, bool IsInjected = false);

public sealed class NavigationInputRequest
{
    internal NavigationInputRequest(long generation, NavigationInputContext context, NavigationDirection direction,
        long acceptedAt, long deadline)
    {
        Generation = generation;
        Context = context;
        Direction = direction;
        AcceptedAtMilliseconds = acceptedAt;
        DeadlineMilliseconds = deadline;
    }

    internal long Generation { get; }
    public NavigationInputContext Context { get; }
    public NavigationDirection Direction { get; }
    public long AcceptedAtMilliseconds { get; }
    public long DeadlineMilliseconds { get; }
}

/// <summary>
/// Input admission and a bounded queue, with no OS hooks, UI Automation, input
/// replay, or clipboard access. Calls are serialized by a short lock. A future
/// input adapter must supply repeat/up events (including keys already held when
/// attaching), current modifier state, and qualified pane eligibility.
/// </summary>
public sealed class NavigationInputQueue
{
    private readonly object _gate = new();
    private readonly NavigationBindings _bindings;
    private readonly int _capacity;
    private readonly int _timeoutMilliseconds;
    private readonly Queue<NavigationInputRequest> _pending = new();
    // At most the two configured virtual keys; never a history of typed keys.
    private readonly Dictionary<int, HeldKey> _held = new();
    private readonly record struct HeldKey(long Generation);
    private NavigationInputContext? _context;
    private NavigationInputRequest? _active;
    private bool _enabled;
    private long _generation = 1;
    private long _lastTime;

    public NavigationInputQueue(NavigationBindings bindings, int capacity = 8, int timeoutMilliseconds = 1000)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (!ValidChord(bindings.Previous) || !ValidChord(bindings.Next) || bindings.Previous == bindings.Next)
        {
            throw new ArgumentException("Navigation needs two different modified key chords.", nameof(bindings));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeoutMilliseconds, 1000);
        _bindings = bindings;
        _capacity = capacity;
        _timeoutMilliseconds = timeoutMilliseconds;
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_enabled != enabled)
            {
                InvalidateCore();
                _enabled = enabled;
            }
        }
    }

    public void UpdateContext(NavigationInputContext? context)
    {
        lock (_gate)
        {
            if (!_enabled || context is null || !PromptNavigation.ValidIdentity(context.Identity) ||
                context.EligibilityRevision < 0 || context.ValidUntilMilliseconds <= _lastTime)
            {
                InvalidateCore();
                return;
            }
            if (_context?.Identity != context.Identity || _context.EligibilityRevision != context.EligibilityRevision)
            {
                InvalidateCore();
            }
            // Renewing the same observation's lease does not cancel a gesture.
            _context = context;
        }
    }

    // Call on manual scrolling and any transition the adapter cannot classify.
    public void Invalidate()
    {
        lock (_gate) { InvalidateCore(); }
    }

    public NavigationKeyResult Handle(NavigationKeyInput input, long nowMilliseconds)
    {
        lock (_gate)
        {
            if (input.IsInjected || (input.VirtualKey != _bindings.Previous.VirtualKey && input.VirtualKey != _bindings.Next.VirtualKey))
            {
                return NavigationKeyResult.PassThrough;
            }
            var eligible = CheckTimeAndContext(nowMilliseconds);
            if (!input.IsDown)
            {
                var owned = _held.Remove(input.VirtualKey, out var held) && held.Generation == _generation;
                return owned && eligible ? NavigationKeyResult.Consumed : NavigationKeyResult.PassThrough;
            }

            var direction = Match(input);
            if (_held.TryGetValue(input.VirtualKey, out var heldKey))
            {
                // Keep an admitted press until release, even if a modifier is
                // released first. Otherwise punctuation repeats leak into the draft.
                if (eligible && heldKey.Generation == _generation)
                {
                    return NavigationKeyResult.Consumed;
                }
                _held[input.VirtualKey] = new(0);
                return NavigationKeyResult.PassThrough;
            }

            _held[input.VirtualKey] = new(0);
            if (!eligible || direction is null || input.IsRepeat)
            {
                return NavigationKeyResult.PassThrough;
            }
            _held[input.VirtualKey] = new(_generation);
            if (_pending.Count + (_active is null ? 0 : 1) >= _capacity)
            {
                return NavigationKeyResult.QueueFull;
            }
            var deadline = Math.Min(_context!.ValidUntilMilliseconds,
                nowMilliseconds > long.MaxValue - _timeoutMilliseconds ? long.MaxValue : nowMilliseconds + _timeoutMilliseconds);
            _pending.Enqueue(new(_generation, _context, direction.Value, nowMilliseconds, deadline));
            return NavigationKeyResult.Queued;
        }
    }

    public NavigationInputRequest? Take(long nowMilliseconds)
    {
        lock (_gate)
        {
            if (!CheckTimeAndContext(nowMilliseconds) || _active is not null)
            {
                return null;
            }
            while (_pending.TryDequeue(out var request))
            {
                if (nowMilliseconds < request.DeadlineMilliseconds)
                {
                    _active = request;
                    return request;
                }
            }
            return null;
        }
    }

    // A newer native observation confirms an already admitted press. Use that
    // observation's lease without reviving expired work or extending the total
    // time allowed since the key was pressed.
    internal NavigationInputRequest? Confirm(NavigationInputRequest request, long observedAt, long now)
    {
        lock (_gate)
        {
            if (!IsCurrent(request, now) || observedAt <= request.AcceptedAtMilliseconds || observedAt > now)
                return null;
            var maximum = request.AcceptedAtMilliseconds > long.MaxValue - _timeoutMilliseconds
                ? long.MaxValue : request.AcceptedAtMilliseconds + _timeoutMilliseconds;
            _active = new(request.Generation, _context!, request.Direction, request.AcceptedAtMilliseconds,
                Math.Min(_context!.ValidUntilMilliseconds, maximum));
            return _active;
        }
    }

    // Recheck immediately before the OS scroll and again before accepting its
    // readback. This cannot cancel an OS call that has already started.
    public bool CanExecute(NavigationInputRequest request, long nowMilliseconds)
    {
        lock (_gate) { return IsCurrent(request, nowMilliseconds); }
    }

    // Completion acknowledges queue work only. A caller may commit a navigation
    // cursor solely after separately verifying the requested viewport change.
    public bool Complete(NavigationInputRequest request, long nowMilliseconds)
    {
        lock (_gate)
        {
            if (!IsCurrent(request, nowMilliseconds)) { return false; }
            _active = null;
            return true;
        }
    }

    private bool IsCurrent(NavigationInputRequest request, long now) => CheckTimeAndContext(now) &&
        ReferenceEquals(request, _active) && request.Generation == _generation &&
        request.Context.Identity == _context!.Identity && request.Context.EligibilityRevision == _context.EligibilityRevision;

    private bool CheckTimeAndContext(long now)
    {
        if (now < _lastTime)
        {
            InvalidateCore();
            return false;
        }
        _lastTime = now;
        if (!_enabled || _context is null || now >= _context.ValidUntilMilliseconds ||
            (_active is not null && now >= _active.DeadlineMilliseconds))
        {
            InvalidateCore();
            return false;
        }
        return true;
    }

    private void InvalidateCore()
    {
        _generation++;
        _context = null;
        _active = null;
        _pending.Clear();
        // Preserve held-key entries so re-enable or focus entry cannot steal a
        // press that began elsewhere. The next release removes each entry.
    }

    private NavigationDirection? Match(NavigationKeyInput input) =>
        input.VirtualKey == _bindings.Previous.VirtualKey && input.Modifiers == _bindings.Previous.Modifiers ? NavigationDirection.Previous :
        input.VirtualKey == _bindings.Next.VirtualKey && input.Modifiers == _bindings.Next.Modifiers ? NavigationDirection.Next : null;

    private static bool ValidChord(NavigationChord? chord) => chord is not null && chord.VirtualKey is > 0 and <= 255 &&
        chord.VirtualKey is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5) &&
        chord.Modifiers != NavigationModifiers.None && ((int)chord.Modifiers & ~15) == 0;
}
