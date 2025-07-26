namespace COA.CodeSearch.McpServer.Infrastructure;

/// <summary>
/// Thread-safe counter implementation using atomic operations.
/// Prevents race conditions when tracking counts across multiple threads.
/// </summary>
public sealed class ThreadSafeCounter
{
    private int _value;

    /// <summary>
    /// Gets the current value of the counter.
    /// </summary>
    public int Value => Volatile.Read(ref _value);

    /// <summary>
    /// Atomically increments the counter and returns the new value.
    /// </summary>
    /// <returns>The incremented value</returns>
    public int Increment() => Interlocked.Increment(ref _value);

    /// <summary>
    /// Atomically decrements the counter and returns the new value.
    /// </summary>
    /// <returns>The decremented value</returns>
    public int Decrement() => Interlocked.Decrement(ref _value);

    /// <summary>
    /// Atomically adds a value to the counter and returns the new value.
    /// </summary>
    /// <param name="value">The value to add (can be negative)</param>
    /// <returns>The new value after addition</returns>
    public int Add(int value) => Interlocked.Add(ref _value, value);

    /// <summary>
    /// Atomically sets the counter to a specific value.
    /// </summary>
    /// <param name="value">The value to set</param>
    /// <returns>The previous value</returns>
    public int Set(int value) => Interlocked.Exchange(ref _value, value);

    /// <summary>
    /// Atomically resets the counter to zero.
    /// </summary>
    /// <returns>The previous value before reset</returns>
    public int Reset() => Interlocked.Exchange(ref _value, 0);

    /// <summary>
    /// Atomically compares the counter value and sets it to a new value if it matches the expected value.
    /// </summary>
    /// <param name="value">The value to set if the comparison succeeds</param>
    /// <param name="comparand">The expected current value</param>
    /// <returns>The original value (whether or not the exchange occurred)</returns>
    public int CompareExchange(int value, int comparand) => 
        Interlocked.CompareExchange(ref _value, value, comparand);

    /// <summary>
    /// Attempts to increment the counter only if it's below a maximum value.
    /// </summary>
    /// <param name="maxValue">The maximum allowed value</param>
    /// <returns>True if incremented, false if already at or above max</returns>
    public bool TryIncrementWithMax(int maxValue)
    {
        int currentValue;
        do
        {
            currentValue = Value;
            if (currentValue >= maxValue)
                return false;
        }
        while (Interlocked.CompareExchange(ref _value, currentValue + 1, currentValue) != currentValue);

        return true;
    }

    /// <summary>
    /// Gets a string representation of the counter value.
    /// </summary>
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Implicit conversion to int for convenience.
    /// </summary>
    public static implicit operator int(ThreadSafeCounter counter) => counter.Value;
}