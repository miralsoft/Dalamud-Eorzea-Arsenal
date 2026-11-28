using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// The one hourly limit of <c>PUT /me/unlocks</c> (30 calls an hour per account, <c>contract-goals.md</c>), shared
/// by the goals mode and the collection: the server counts both together, so the plugin does too (R23), and a
/// <c>Retry-After</c> one of them was told holds the other as well. Seen in the game on 2026-11-28, when each kept
/// its own count and the server's limit was reached first. Thread-safe.
/// </summary>
public sealed class UnlockCallBudget
{
    private readonly Lock _gate = new();
    private readonly Queue<DateTimeOffset> _calls = new();
    private DateTimeOffset _heldUntil = DateTimeOffset.MinValue;

    /// <summary>Until when no call is to be made, or <see cref="DateTimeOffset.MinValue"/>.</summary>
    public DateTimeOffset HeldUntil
    {
        get
        {
            lock (_gate)
            {
                return _heldUntil;
            }
        }
    }

    /// <summary>Whether calls are held now (the hour is used up, or the server said to wait).</summary>
    /// <param name="now">The time now.</param>
    /// <returns><see langword="true"/> while held.</returns>
    public bool IsHeld(DateTimeOffset now) => now < HeldUntil;

    /// <summary>Takes one call of the hour, or says none is left; then holds until the oldest call is an hour old.</summary>
    /// <param name="now">The time now.</param>
    /// <returns>Whether the call may be made.</returns>
    public bool TryTake(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (now < _heldUntil)
            {
                return false;
            }

            while (_calls.Count > 0 && now - _calls.Peek() >= TimeSpan.FromHours(1))
            {
                _calls.Dequeue();
            }

            if (_calls.Count >= UnlockProtocol.MaxCallsPerHour)
            {
                _heldUntil = _calls.Peek() + TimeSpan.FromHours(1);
                return false;
            }

            _calls.Enqueue(now);
            return true;
        }
    }

    /// <summary>Holds every call until then, as a <c>429</c> said; an earlier time does not shorten a longer hold.</summary>
    /// <param name="until">The end of the hold.</param>
    public void HoldUntil(DateTimeOffset until)
    {
        lock (_gate)
        {
            if (until > _heldUntil)
            {
                _heldUntil = until;
            }
        }
    }
}
