namespace COA.CodeSearch.McpServer.Infrastructure;

/// <summary>
/// Thread-safe async lock implementation that enforces timeout usage to prevent deadlocks.
/// Based on best practices from Lucene.NET concurrency guide.
/// </summary>
public sealed class AsyncLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _name;
    private bool _disposed;

    /// <summary>
    /// Creates a new AsyncLock instance with an optional name for debugging.
    /// </summary>
    /// <param name="name">Optional name for debugging and logging purposes</param>
    public AsyncLock(string? name = null)
    {
        _name = name ?? "unnamed";
    }

    /// <summary>
    /// Acquires the lock asynchronously with a required timeout.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for the lock</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A disposable that releases the lock when disposed</returns>
    /// <exception cref="ObjectDisposedException">If the lock has been disposed</exception>
    /// <exception cref="TimeoutException">If the lock cannot be acquired within the timeout</exception>
    /// <exception cref="OperationCanceledException">If the operation is cancelled</exception>
    public async Task<IDisposable> LockAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AsyncLock), $"AsyncLock '{_name}' has been disposed");

        // Combine timeout and cancellation token
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _semaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            return new Releaser(_semaphore, _name);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Failed to acquire lock '{_name}' within {timeout}");
        }
    }

    /// <summary>
    /// Attempts to acquire the lock without waiting.
    /// </summary>
    /// <returns>A disposable that releases the lock if acquired, or null if the lock is not available</returns>
    public IDisposable? TryLock()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AsyncLock), $"AsyncLock '{_name}' has been disposed");

        if (_semaphore.Wait(0))
        {
            return new Releaser(_semaphore, _name);
        }

        return null;
    }

    /// <summary>
    /// Gets the current count of the semaphore (0 if locked, 1 if available).
    /// Useful for debugging and monitoring.
    /// </summary>
    public int CurrentCount => _disposed ? -1 : _semaphore.CurrentCount;

    /// <summary>
    /// Gets the name of this lock for debugging purposes.
    /// </summary>
    public string Name => _name;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
    }

    /// <summary>
    /// Disposable that releases the lock when disposed.
    /// </summary>
    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly string _lockName;
        private bool _released;

        public Releaser(SemaphoreSlim semaphore, string lockName)
        {
            _semaphore = semaphore;
            _lockName = lockName;
        }

        public void Dispose()
        {
            if (_released) return;
            _released = true;

            try
            {
                _semaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // Semaphore was disposed while we held the lock
                // This is a programming error but we shouldn't throw from Dispose
            }
            catch (SemaphoreFullException)
            {
                // This indicates a double-release bug
                // Log this as a critical error in production
                throw new InvalidOperationException($"Attempted to release lock '{_lockName}' that was already released. This indicates a concurrency bug.");
            }
        }
    }
}

/// <summary>
/// Extension methods for AsyncLock to provide convenient usage patterns.
/// </summary>
public static class AsyncLockExtensions
{
    /// <summary>
    /// Default timeout for lock acquisition (30 seconds).
    /// </summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Acquires the lock with the default timeout.
    /// </summary>
    public static Task<IDisposable> LockAsync(this AsyncLock asyncLock, CancellationToken cancellationToken = default)
    {
        return asyncLock.LockAsync(DefaultLockTimeout, cancellationToken);
    }

    /// <summary>
    /// Executes an action while holding the lock.
    /// </summary>
    public static async Task ExecuteWithLockAsync(
        this AsyncLock asyncLock,
        Func<Task> action,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using (await asyncLock.LockAsync(timeout ?? DefaultLockTimeout, cancellationToken).ConfigureAwait(false))
        {
            await action().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes a function while holding the lock and returns the result.
    /// </summary>
    public static async Task<T> ExecuteWithLockAsync<T>(
        this AsyncLock asyncLock,
        Func<Task<T>> function,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using (await asyncLock.LockAsync(timeout ?? DefaultLockTimeout, cancellationToken).ConfigureAwait(false))
        {
            return await function().ConfigureAwait(false);
        }
    }
}