using Clici.App.Lifecycle;

namespace Clici.App.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void RejectsAConcurrentOwnerAndReacquiresAfterDispose()
    {
        var mutexName = $"Local\\clici.tests.{Guid.NewGuid():N}";
        using (var primary = SingleInstanceGuard.TryAcquire(mutexName))
        {
            Assert.NotNull(primary);

            SingleInstanceGuard? secondary = null;
            var contender = new Thread(
                () => secondary = SingleInstanceGuard.TryAcquire(mutexName));
            contender.Start();
            Assert.True(contender.Join(TimeSpan.FromSeconds(5)));

            Assert.Null(secondary);
        }

        using var reacquired = SingleInstanceGuard.TryAcquire(mutexName);
        Assert.NotNull(reacquired);
    }

    [Fact]
    public void AcquiresAnAbandonedMutex()
    {
        var mutexName = $"Local\\clici.tests.{Guid.NewGuid():N}";
        using var acquired = new ManualResetEventSlim();
        using var observer = new Mutex(false, mutexName);
        var owner = new Thread(() =>
        {
            using var ownedMutex = Mutex.OpenExisting(mutexName);
            ownedMutex.WaitOne();
            acquired.Set();
        });

        owner.Start();
        Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(owner.Join(TimeSpan.FromSeconds(5)));

        using var guard = SingleInstanceGuard.TryAcquire(mutexName);

        Assert.NotNull(guard);
    }
}
