namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Abstracts "now" so throttling/back-off logic is deterministic in tests. The production
/// implementation is <see cref="SystemClock"/>.
/// </summary>
public interface IClock
{
    /// <summary>The current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Real wall-clock implementation of <see cref="IClock"/>.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
