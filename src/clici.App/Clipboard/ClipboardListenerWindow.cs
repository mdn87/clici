using System.ComponentModel;
using Clici.App.Native;

namespace Clici.App.Clipboard;

internal sealed class ClipboardListenerWindow : NativeWindow, IDisposable
{
    // WM_CLIPBOARDUPDATE must return promptly: it is dispatched to every
    // clipboard-format listener, and the Win+V flyout waits on that chain.
    // Reads and writes are deferred onto a short debounce timer so they run
    // from an idle WM_TIMER turn rather than synchronously inside WndProc,
    // where the message pump is stalled. The delay also coalesces the bursts
    // produced by our own writes and by peer clipboard tools.
    private static readonly int DebounceMilliseconds = 60;

    private readonly System.Windows.Forms.Timer _debounceTimer;
    private bool _registered;
    private bool _disposed;

    public ClipboardListenerWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "clici clipboard listener"
        });

        _debounceTimer = new System.Windows.Forms.Timer
        {
            Interval = DebounceMilliseconds
        };
        _debounceTimer.Tick += OnDebounceTick;

        if (!NativeMethods.AddClipboardFormatListener(Handle))
        {
            var error = new Win32Exception();
            _debounceTimer.Dispose();
            DestroyHandle();
            throw error;
        }

        _registered = true;
    }

    public event EventHandler? ClipboardChanged;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmClipboardUpdate)
        {
            // Restart the debounce window and return immediately so the
            // listener chain is not blocked by clipboard I/O.
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        base.WndProc(ref message);
    }

    private void OnDebounceTick(object? sender, EventArgs eventArgs)
    {
        _debounceTimer.Stop();
        ClipboardChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _debounceTimer.Stop();
        _debounceTimer.Tick -= OnDebounceTick;
        _debounceTimer.Dispose();

        if (_registered)
        {
            NativeMethods.RemoveClipboardFormatListener(Handle);
            _registered = false;
        }

        DestroyHandle();
        _disposed = true;
    }
}
