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
            if (!mutex.WaitOne(0, false))
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
