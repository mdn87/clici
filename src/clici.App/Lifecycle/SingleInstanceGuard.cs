namespace Clici.App.Lifecycle;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\clici";
    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstanceGuard? TryAcquire()
        => TryAcquire(MutexName);

    internal static SingleInstanceGuard? TryAcquire(string mutexName)
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(false, mutexName);
            // A zero timeout races the kernel: when a prior owner's thread/process has just
            // exited, Thread.Join()/process teardown returning does not guarantee the mutex is
            // yet marked abandoned, so WaitOne(0) can spuriously return false (~3% observed).
            // A short bounded wait lets a genuinely-abandoned mutex surface as
            // AbandonedMutexException, while a live second instance still returns false promptly.
            if (!mutex.WaitOne(TimeSpan.FromMilliseconds(250), false))
            {
                mutex.Dispose();
                return null;
            }

            return new SingleInstanceGuard(mutex);
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstanceGuard(mutex!);
        }
        catch
        {
            mutex?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        finally
        {
            _mutex.Dispose();
            _disposed = true;
        }
    }
}
