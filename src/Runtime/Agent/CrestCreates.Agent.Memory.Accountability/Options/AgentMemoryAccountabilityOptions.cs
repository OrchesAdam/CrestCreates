namespace CrestCreates.Agent.Memory.Accountability.Options;

/// <summary>
/// Bounded options for the Memory Accountability write bridge. Each producer
/// attempt owns one independent finite write budget; the value must be finite,
/// positive, and small enough that a host cannot configure an unbounded block.
/// </summary>
public sealed class AgentMemoryAccountabilityOptions
{
    /// <summary>Default per-attempt write budget: 5 seconds.</summary>
    public static readonly TimeSpan DefaultWriteTimeout = TimeSpan.FromSeconds(5);

    private TimeSpan _writeTimeout = DefaultWriteTimeout;

    public TimeSpan WriteTimeout
    {
        get => _writeTimeout;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "WriteTimeout must be positive.");
            if (value == Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(value), "WriteTimeout must be finite.");
            _writeTimeout = value;
        }
    }

    public bool IsValidWriteTimeout => _writeTimeout > TimeSpan.Zero
        && _writeTimeout != Timeout.InfiniteTimeSpan
        && _writeTimeout <= TimeSpan.FromMilliseconds(uint.MaxValue - 1);
}
