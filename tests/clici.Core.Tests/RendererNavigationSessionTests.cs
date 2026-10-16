using Clici.Core.Navigation;

namespace Clici.Core.Tests;

public class RendererNavigationSessionTests
{
    private static NavigationInputContext Context(long until = 1000, string pane = "pane") =>
        new(new(pane, 1, 1, new(AgentKind.Codex, "session")), 1, until);
    private static NavigationKeyInput Up(bool down = true) => new(0x26, NavigationModifiers.Control, down);

    [Fact]
    public void PressWaitsForNativeConfirmationAfterThePress()
    {
        var session = new RendererNavigationSession();
        session.Publish(Context(), 10);
        Assert.Equal(NavigationKeyResult.Queued, session.Handle(Up(), 20));
        Assert.Null(session.TakeConfirmed(21));
        session.Publish(Context(), 15); // A delayed frame from before the press is insufficient.
        Assert.Null(session.TakeConfirmed(22));
        session.Publish(Context(), 23);
        var request = Assert.IsType<NavigationInputRequest>(session.TakeConfirmed(24));
        Assert.True(session.CanExecute(request, 25));
        Assert.True(session.Complete(request, 26));
    }

    [Fact]
    public void ModalOrFocusChangeCancelsAlreadyConsumedPressWithoutReplay()
    {
        var session = new RendererNavigationSession();
        session.Publish(Context(), 10);
        session.Handle(Up(), 20);
        session.Invalidate();
        session.Publish(Context(pane: "other"), 30);
        Assert.Null(session.TakeConfirmed(31));
        Assert.Equal(NavigationKeyResult.PassThrough, session.Handle(Up(), 32));
        session.Handle(Up(false), 33);
        Assert.Equal(NavigationKeyResult.Queued, session.Handle(Up(), 34));
    }

    [Fact]
    public void ExpiredHeartbeatCannotConsumeOrExecute()
    {
        var session = new RendererNavigationSession();
        session.Publish(Context(30), 10);
        session.Handle(Up(), 20);
        Assert.Null(session.TakeConfirmed(30));
        session.Handle(Up(false), 31);
        Assert.Equal(NavigationKeyResult.PassThrough, session.Handle(Up(), 32));
    }

    [Fact]
    public void OldConfirmationCannotRenewTheLease()
    {
        var session = new RendererNavigationSession();
        session.Publish(Context(30), 10);
        session.Publish(Context(1000), 9);
        Assert.Equal(NavigationKeyResult.PassThrough, session.Handle(Up(), 31));
    }
}
