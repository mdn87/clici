using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Clici.PromptNavigation.Probe;

namespace Clici.App.Tests;

public sealed class NativeAdmissionTests
{
    [Fact]
    public void ReadOnlyChildSeesRevocationAndCannotRestoreOldEligibility()
    {
        using var mapping = MemoryMappedFile.CreateNew(null, 32);
        using var writer = mapping.CreateViewAccessor();
        writer.Write(0, NativeAdmission.Magic);
        writer.Write(8, (long)Environment.ProcessId);
        writer.Write(16, 123UL);
        writer.Write(24, 3UL);
        Assert.True(DuplicateHandle(GetCurrentProcess(), mapping.SafeMemoryMappedFileHandle.DangerousGetHandle(),
            GetCurrentProcess(), out var readOnly, 4, false, 0));
        Assert.Equal(0, MapViewOfFile(readOnly, 2, 0, 0, 32));
        using var child = new NativeAdmission(readOnly, Environment.ProcessId, 123);
        Assert.True(child.Allows(3));
        writer.Write(24, 4UL); // Native revocation can overtake a queued positive message.
        Assert.False(child.Allows(3));
        Assert.False(child.Allows(4));
        writer.Write(24, 5UL); // Fresh eligible epoch cannot revive epoch 1.
        Assert.False(child.Allows(3));
        Assert.True(child.Allows(5));
        writer.Write(24, 5UL); // Heartbeats do not revoke the current admission epoch.
        Assert.True(child.Allows(5));
    }

    [Fact]
    public void DifferentParentOrIncarnationCannotAttach()
    {
        using var mapping = MemoryMappedFile.CreateNew(null, 32);
        using var writer = mapping.CreateViewAccessor();
        writer.Write(0, NativeAdmission.Magic);
        writer.Write(8, (long)Environment.ProcessId);
        writer.Write(16, 123UL);
        Assert.True(DuplicateHandle(GetCurrentProcess(), mapping.SafeMemoryMappedFileHandle.DangerousGetHandle(),
            GetCurrentProcess(), out var readOnly, 4, false, 0));
        Assert.Throws<InvalidOperationException>(() => new NativeAdmission(readOnly, Environment.ProcessId, 124));
    }

    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool DuplicateHandle(nint source, nint handle, nint target, out nint duplicate, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll")] private static extern nint MapViewOfFile(nint mapping, uint access, uint high, uint low, nuint bytes);
}
