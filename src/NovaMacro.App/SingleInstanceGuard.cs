namespace NovaMacro.App;

internal sealed class SingleInstanceGuard : IDisposable
{
    internal const string ProductionMutexName =
        @"Local\NovaMacro.SingleInstance";

    private static readonly object ProcessOwnershipGate = new();
    private static readonly Dictionary<string, Thread> ProcessOwners = new(
        StringComparer.Ordinal);

    private readonly string _mutexName;
    private readonly Thread _owningThread;
    private Mutex? _mutex;

    private SingleInstanceGuard(string mutexName, Mutex mutex)
    {
        _mutexName = mutexName;
        _mutex = mutex;
        _owningThread = Thread.CurrentThread;
    }

    internal static bool TryAcquire(out SingleInstanceGuard? guard)
    {
        return TryAcquire(ProductionMutexName, out guard);
    }

    internal static bool TryAcquire(
        string mutexName,
        out SingleInstanceGuard? guard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        guard = null;

        lock (ProcessOwnershipGate)
        {
            // A Windows mutex is recursive for its owning thread. This local
            // check preserves non-reentrant TryAcquire semantics in one process.
            if (ProcessOwners.TryGetValue(
                    mutexName,
                    out Thread? existingOwner))
            {
                if (existingOwner.IsAlive)
                {
                    return false;
                }

                ProcessOwners.Remove(mutexName);
            }

            var mutex = new Mutex(initiallyOwned: false, mutexName);
            bool acquired = false;
            bool registeredInProcess = false;

            try
            {
                try
                {
                    acquired = mutex.WaitOne(millisecondsTimeout: 0);
                }
                catch (AbandonedMutexException)
                {
                    // WaitOne grants ownership before reporting abandonment.
                    acquired = true;
                }

                if (!acquired)
                {
                    mutex.Dispose();
                    return false;
                }

                ProcessOwners.Add(mutexName, Thread.CurrentThread);
                registeredInProcess = true;
                guard = new SingleInstanceGuard(mutexName, mutex);
                return true;
            }
            catch
            {
                if (registeredInProcess)
                {
                    ProcessOwners.Remove(mutexName);
                }

                if (acquired)
                {
                    mutex.ReleaseMutex();
                }

                mutex.Dispose();
                throw;
            }
        }
    }

    public void Dispose()
    {
        Mutex? mutex = _mutex;
        if (mutex is null)
        {
            return;
        }

        if (!ReferenceEquals(Thread.CurrentThread, _owningThread))
        {
            throw new InvalidOperationException(
                "The single-instance guard must be disposed by the thread " +
                "that acquired it.");
        }

        mutex.ReleaseMutex();

        lock (ProcessOwnershipGate)
        {
            if (ProcessOwners.TryGetValue(
                    _mutexName,
                    out Thread? registeredOwner) &&
                ReferenceEquals(registeredOwner, _owningThread))
            {
                ProcessOwners.Remove(_mutexName);
            }
        }

        _mutex = null;
        mutex.Dispose();
    }
}
