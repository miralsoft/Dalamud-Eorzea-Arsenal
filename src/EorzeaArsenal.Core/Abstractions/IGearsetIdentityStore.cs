using EorzeaArsenal.Model;

namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Persists the local gearset mapping cache — which <c>set_uid</c> the server last gave each gearset —
/// so the in-game comparison works before the first push of a session instead of only after it.
/// </summary>
/// <remarks>
/// A cache, not a second truth. Two properties make that so, and the service on top enforces both:
/// nothing here ever <b>mints</b> a uid (only the server does), and a miss shows <b>nothing</b> rather
/// than something wrong. Backed by the plugin config in production, an in-memory fake in tests. No
/// secrets pass through (R19/R20) — a uid is an opaque id, not a credential.
/// </remarks>
public interface IGearsetIdentityStore
{
    /// <summary>The cached rows per character, keyed by <c>cid_hash</c>.</summary>
    Dictionary<string, List<CachedGearsetIdentity>> Identities { get; set; }

    /// <summary>Persists the current contents.</summary>
    void Save();
}
