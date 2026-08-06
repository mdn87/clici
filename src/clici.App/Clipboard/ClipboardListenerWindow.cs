using System.ComponentModel;
using Clici.App.Native;

namespace Clici.App.Clipboard;

internal sealed class ClipboardListenerWindow : NativeWindow, IDisposable
{
    private bool _registered;
    private bool _disposed;

    public ClipboardListenerWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "clici clipboard listener"
        });

        if (!NativeMethods.AddClipboardFormatListener(Handle))
        {
            var error = new Win32Exception();
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
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_registered)
        {
            NativeMethods.RemoveClipboardFormatListener(Handle);
            _registered = false;
        }

        DestroyHandle();
        _disposed = true;
    }
}
