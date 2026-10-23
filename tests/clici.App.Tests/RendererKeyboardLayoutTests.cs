using System.Diagnostics;
using Clici.PromptNavigation.Probe;

namespace Clici.App.Tests;

public sealed class RendererKeyboardLayoutTests
{
    [Fact]
    public void LegacyTypingRevokesInputWithoutDiscardingLayout()
    {
        using var parent = Process.GetCurrentProcess();
        using var hook = new RendererKeyboardHook(parent, proofOnly: true);
        var layout = hook.LayoutRevision;
        var revision = hook.Revision;

        hook.Invalidate(4);

        Assert.True(hook.Revision > revision);
        Assert.Equal(layout, hook.LayoutRevision);
        hook.Invalidate(3);
        Assert.True(hook.LayoutRevision > layout);
    }
}
