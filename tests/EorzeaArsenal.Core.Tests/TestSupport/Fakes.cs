using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Tests.TestSupport;

/// <summary>In-memory <see cref="ITokenStore"/> for tests.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    /// <inheritdoc />
    public bool HasKey => !string.IsNullOrEmpty(ApiKey);

    /// <inheritdoc />
    public string? ApiKey { get; private set; }

    /// <inheritdoc />
    public void SetApiKey(string apiKey) => ApiKey = apiKey.Trim();

    /// <inheritdoc />
    public void Clear() => ApiKey = null;
}

/// <summary>A delay provider that does not actually wait, but honors cancellation.</summary>
public sealed class FakeDelay : IDelayProvider
{
    /// <summary>Number of times <see cref="Delay"/> was invoked.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    public Task Delay(TimeSpan delay, CancellationToken ct)
    {
        Calls++;
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

/// <summary>A controllable clock for throttle/back-off tests.</summary>
public sealed class TestClock : IClock
{
    /// <summary>The current (settable) time.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Advances the clock by the given amount.</summary>
    /// <param name="by">How far to advance.</param>
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Captures log lines so tests can assert on them.</summary>
public sealed class CapturingLog : ILog
{
    /// <summary>All captured messages.</summary>
    public List<string> Messages { get; } = [];

    /// <inheritdoc />
    public void Info(string message) => Messages.Add("INFO " + message);

    /// <inheritdoc />
    public void Warning(string message) => Messages.Add("WARN " + message);

    /// <inheritdoc />
    public void Error(string message) => Messages.Add("ERR " + message);
}

/// <summary>A programmable <see cref="IGearSource"/>.</summary>
public sealed class FakeGearSource : IGearSource
{
    /// <inheritdoc />
    public bool IsAvailable { get; set; } = true;

    /// <summary>The snapshot returned by <see cref="ReadAsync"/>.</summary>
    public GearData? Snapshot { get; set; }

    /// <summary>Number of reads performed.</summary>
    public int Reads { get; private set; }

    /// <inheritdoc />
    public Task<GearData?> ReadAsync(CancellationToken ct)
    {
        Reads++;
        return Task.FromResult(Snapshot);
    }
}

/// <summary>A programmable <see cref="IInventorySource"/>.</summary>
public sealed class FakeInventorySource : IInventorySource
{
    /// <inheritdoc />
    public bool IsAvailable { get; set; } = true;

    /// <summary>The snapshot returned by <see cref="ReadCharacterAsync"/>.</summary>
    public InventoryData? Snapshot { get; set; }

    /// <summary>Number of character reads performed.</summary>
    public int Reads { get; private set; }

    /// <inheritdoc />
    public Task<InventoryData?> ReadCharacterAsync(CancellationToken ct)
    {
        Reads++;
        return Task.FromResult(Snapshot);
    }
}
