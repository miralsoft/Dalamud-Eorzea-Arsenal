namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Stores the API key (a secret, R19/R20). Backed by the Dalamud plugin config in production
/// (plaintext on the user's own machine is the accepted trust boundary, P10) and by an
/// in-memory fake in tests. The key is never logged or committed.
/// </summary>
public interface ITokenStore
{
    /// <summary>Whether a non-empty key is currently stored.</summary>
    bool HasKey { get; }

    /// <summary>The stored API key, or <see langword="null"/> if disconnected.</summary>
    string? ApiKey { get; }

    /// <summary>Stores (and persists) a new API key.</summary>
    /// <param name="apiKey">The key to store; trimmed by the implementation.</param>
    void SetApiKey(string apiKey);

    /// <summary>Clears the stored key — the in-plugin "Disconnect" action (R42).</summary>
    void Clear();
}
