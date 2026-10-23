using System.Diagnostics;
using System.Runtime.InteropServices;
using Clici.Core.Navigation;

namespace Clici.PromptNavigation.Probe;

// The hook thread does no pipe I/O, UI Automation, scrolling, logging, or input
// replay. It owns only two navigation-key states and a bounded queue. A stalled renderer
// loses its monotonic lease; the parent also kills a stalled helper after 1s.
internal sealed class RendererKeyboardHook : IDisposable
{
    internal readonly RendererNavigationSession Session = new();
    private static readonly NavigationBindings Bindings = NavigationBindings.Default;
    private readonly Process parent;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly HookCallback keyboardCallback;
    private readonly HookCallback mouseCallback;
    private readonly WinEventCallback focusCallback;
    private readonly bool testInput;
    private readonly NativeAdmission? admission;
    private long admissionWord;
    private long layoutRevision;
    private readonly long[] timingSamples = new long[256];
    private long timingCount, maximumTicks;
    internal object? Timing
    {
        get
        {
            if (!testInput) return null;
            var count = Volatile.Read(ref timingCount);
            var samples = timingSamples.Take((int)Math.Min(count, timingSamples.Length)).Order().ToArray();
            return new
            {
                count,
                maxMilliseconds = Volatile.Read(ref maximumTicks) * 1000.0 / Stopwatch.Frequency,
                p95Milliseconds = samples.Length == 0 ? 0 : samples[(int)((samples.Length - 1) * .95)] * 1000.0 / Stopwatch.Frequency
            };
        }
    }
    private nint boundWindow;
    private long revision;
    private uint threadId;
    private Exception? failure;
    internal long Revision => Interlocked.Read(ref revision);
    internal long LayoutRevision => Interlocked.Read(ref layoutRevision);
    internal object? LastAdmission { get; private set; }
    internal int LastInvalidation { get; private set; }

    internal RendererKeyboardHook(Process parent, bool proofOnly, NativeAdmission? admission = null)
    {
        this.parent = parent;
        this.admission = admission;
        testInput = proofOnly && Environment.GetEnvironmentVariable("CLICI_PROOF_INJECTED_KEYS") == "1";
        keyboardCallback = Keyboard;
        mouseCallback = Mouse;
        focusCallback = (_, _, _, _, _, _, _) => Invalidate(1);
        thread = new Thread(Pump) { IsBackground = true, Name = "Clici renderer key admission" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(2000) || failure is not null)
            throw new InvalidOperationException("Renderer keyboard hook unavailable.", failure);
    }

    internal void Invalidate(int reason = 0)
    {
        LastInvalidation = reason;
        Interlocked.Increment(ref revision);
        if (reason != 4 && reason != 5) Interlocked.Increment(ref layoutRevision);
        Session.Invalidate();
    }

    internal bool Publish(nint window, NavigationInputContext context, long observedAt, long expectedRevision, ulong expectedAdmission = 0)
    {
        if (Revision != expectedRevision || admission is not null && !admission.Allows(expectedAdmission) || GetForegroundWindow() != window ||
            Down(1) || Down(2) || Down(4) || Down(0x11) || Down(0x5B) || Down(0x5C))
        {
            Invalidate(5);
            return false;
        }
        boundWindow = window;
        Volatile.Write(ref admissionWord, unchecked((long)expectedAdmission));
        Session.Publish(context, observedAt);
        // A concurrent focus/mouse event must never be undone by publication.
        if (Revision != expectedRevision || admission is not null && !admission.Allows(expectedAdmission)) { Session.Invalidate(); return false; }
        return true;
    }

    private nint Keyboard(int code, nuint message, nint data)
    {
        if (!testInput) return KeyboardCore(code, message, data);
        var start = Stopwatch.GetTimestamp();
        try { return KeyboardCore(code, message, data); }
        finally
        {
            var elapsed = Stopwatch.GetTimestamp() - start;
            timingSamples[timingCount % timingSamples.Length] = elapsed;
            maximumTicks = Math.Max(maximumTicks, elapsed);
            Volatile.Write(ref timingCount, timingCount + 1);
        }
    }

    private nint KeyboardCore(int code, nuint message, nint data)
    {
        if (code < 0) return CallNextHookEx(0, code, message, data);
        try
        {
            var key = Marshal.PtrToStructure<KeyboardData>(data);
            var injected = (key.Flags & 0x10) != 0;
            // Test-only marked SendInput is allowed only after the bridge has
            // bound a CLICI-PROOF portable terminal. Ordinary injected keys pass.
            if (injected && !(testInput && key.ExtraInfo == 0xC11C1))
                return CallNextHookEx(0, code, message, data);
            var down = message is 0x100 or 0x104;
            if (GetForegroundWindow() != boundWindow || WaitForSingleObject(parent.Handle, 0) != 0x102)
                Invalidate(2);
            if (admission is not null && !admission.Allows(unchecked((ulong)Volatile.Read(ref admissionWord))))
                Invalidate(4); // Native input revocation does not imply reflow.
            if (key.VirtualKey != Bindings.Previous.VirtualKey && key.VirtualKey != Bindings.Next.VirtualKey)
            {
                // Both modifier orders and either side are valid. Modifiers
                // pass through; only an eligible punctuation press is consumed.
                if (down && key.VirtualKey is not (0x10 or 0x12 or 0xA0 or 0xA1 or 0xA4 or 0xA5)) Invalidate(4);
                return CallNextHookEx(0, code, message, data);
            }
            var modifiers = (Down(0x11) ? NavigationModifiers.Control : 0) |
                (Down(0x10) ? NavigationModifiers.Shift : 0) |
                (Down(0x12) ? NavigationModifiers.Alt : 0) |
                (Down(0x5B) || Down(0x5C) ? NavigationModifiers.Windows : 0);
            var result = Session.Handle(new((int)key.VirtualKey, modifiers, down), Environment.TickCount64);
            if (down && testInput) LastAdmission = new { result = result.ToString(), modifiers = modifiers.ToString(), revision = Revision, at = Environment.TickCount64 };
            if (result != NavigationKeyResult.PassThrough) return 1;
        }
        catch { Invalidate(); }
        return CallNextHookEx(0, code, message, data);
    }

    private nint Mouse(int code, nuint message, nint data)
    {
        if (code >= 0 && message != 0x200) Invalidate(3); // Buttons, wheel, drag start/end.
        return CallNextHookEx(0, code, message, data);
    }

    private void Pump()
    {
        nint keyboard = 0, mouse = 0, focus = 0, foreground = 0;
        try
        {
            threadId = GetCurrentThreadId();
            PeekMessage(out _, 0, 0, 0, 0); // Create the thread's queue before publishing readiness.
            foreach (var key in new[] { Bindings.Previous.VirtualKey, Bindings.Next.VirtualKey })
                if (Down(key)) Session.Handle(new(key, NavigationModifiers.None, true), Environment.TickCount64);
            keyboard = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
            mouse = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
            focus = SetWinEventHook(0x8005, 0x8005, 0, focusCallback, 0, 0, 0);
            foreground = SetWinEventHook(3, 3, 0, focusCallback, 0, 0, 0);
            if (keyboard == 0 || mouse == 0 || focus == 0 || foreground == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception) { failure = exception; ready.Set(); }
        finally
        {
            Invalidate();
            if (keyboard != 0) UnhookWindowsHookEx(keyboard);
            if (mouse != 0) UnhookWindowsHookEx(mouse);
            if (focus != 0) UnhookWinEvent(focus);
            if (foreground != 0) UnhookWinEvent(foreground);
        }
    }

    public void Dispose()
    {
        Invalidate();
        PostThreadMessage(threadId, 0x12, 0, 0);
        thread.Join(1000);
    }

    private static bool Down(int key) => GetAsyncKeyState(key) < 0;
    private delegate nint HookCallback(int code, nuint message, nint data);
    private delegate void WinEventCallback(nint hook, uint evt, nint window, int obj, int child, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData { public uint VirtualKey, ScanCode, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookCallback callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, nuint wparam, nint lparam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
}
