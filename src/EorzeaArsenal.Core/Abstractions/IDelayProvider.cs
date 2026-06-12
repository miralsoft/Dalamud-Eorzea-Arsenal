namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Abstracts asynchronous waiting so the device-flow polling loop can be tested without real
/// time passing. The production implementation is <see cref="RealDelay"/>.
/// </summary>
public interface IDelayProvider
{
    /// <summary>Waits for the given duration (or until cancelled).</summary>
    /// <param name="delay">How long to wait.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes after the delay.</returns>
    Task Delay(TimeSpan delay, CancellationToken ct);
}

/// <summary>Real <see cref="Task.Delay(TimeSpan, CancellationToken)"/>-backed implementation.</summary>
public sealed class RealDelay : IDelayProvider
{
    /// <inheritdoc />
    public Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
