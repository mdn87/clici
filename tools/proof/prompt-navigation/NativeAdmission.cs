using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Clici.PromptNavigation.Probe;

// ABI v2, Windows x64: immutable magic/parent/incarnation at offsets 0/8/16;
// one aligned native-writer atomic word at 24. Child handle is FILE_MAP_READ.
internal sealed unsafe class NativeAdmission : IDisposable
{
    internal const long Magic = 0x324943494c43;
    private nint mapping;
    private nint view;

    internal NativeAdmission(nint mapping, int parent, ulong incarnation)
    {
        this.mapping = mapping;
        if (!Environment.Is64BitProcess)
        {
            Dispose();
            throw new InvalidOperationException("Admission requires Windows x64.");
        }
        view = MapViewOfFile(mapping, 4, 0, 0, 32);
        if (view == 0)
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error);
        }
        if (Marshal.ReadInt64(view) != Magic || Marshal.ReadInt64(view, 8) != parent ||
            unchecked((ulong)Marshal.ReadInt64(view, 16)) != incarnation)
        {
            Dispose();
            throw new InvalidOperationException("Admission incarnation mismatch.");
        }
    }

    internal ulong Word => unchecked((ulong)Volatile.Read(ref *(long*)(view + 24)));
    internal bool Allows(ulong expected) => (expected & 1) != 0 && Word == expected;

    public void Dispose()
    {
        if (view != 0) { UnmapViewOfFile(view); view = 0; }
        if (mapping != 0) { CloseHandle(mapping); mapping = 0; }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint MapViewOfFile(nint mapping, uint access, uint high, uint low, nuint bytes);
    [DllImport("kernel32.dll")] private static extern bool UnmapViewOfFile(nint view);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
