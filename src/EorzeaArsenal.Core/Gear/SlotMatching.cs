namespace EorzeaArsenal.Gear;

/// <summary>
/// Whether a slot's target piece is already being worn, with the one slot rule that is easy to get
/// wrong: <b>rings are interchangeable</b>.
/// </summary>
/// <remarks>
/// The game lets either ring sit on either finger and stores whichever order the player equipped them
/// in, so a finger-by-finger comparison reports both rings as missing whenever they are merely
/// swapped. That has now bitten two different views — the BiS "base owned" check and the team farm —
/// so the rule lives here once instead of being re-derived per caller.
/// </remarks>
public static class SlotMatching
{
    /// <summary>The left ring's contract slot key.</summary>
    public const string RingLeft = "RingLeft";

    /// <summary>The right ring's contract slot key.</summary>
    public const string RingRight = "RingRight";

    /// <summary>Whether a slot key names one of the two ring fingers.</summary>
    /// <param name="slot">The contract slot key.</param>
    /// <returns><see langword="true"/> for either ring.</returns>
    public static bool IsRingSlot(string? slot) =>
        string.Equals(slot, RingLeft, StringComparison.Ordinal) || string.Equals(slot, RingRight, StringComparison.Ordinal);

    /// <summary>
    /// Whether the piece a slot targets is already worn. For a ring it counts as worn when it sits on
    /// <i>either</i> finger; every other slot compares directly.
    /// </summary>
    /// <param name="slot">The contract slot key the target belongs to.</param>
    /// <param name="targetItemId">The item the slot is aiming at.</param>
    /// <param name="equipped">Resolves a slot key to the worn item id (0 when the slot is empty).</param>
    /// <returns><see langword="true"/> when the target is already on the character.</returns>
    public static bool IsWorn(string slot, long targetItemId, Func<string, long> equipped)
    {
        if (targetItemId <= 0)
        {
            return false;
        }

        return IsRingSlot(slot)
            ? equipped(RingLeft) == targetItemId || equipped(RingRight) == targetItemId
            : equipped(slot) == targetItemId;
    }
}
