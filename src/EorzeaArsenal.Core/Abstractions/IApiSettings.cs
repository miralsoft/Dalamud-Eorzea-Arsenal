namespace EorzeaArsenal.Abstractions;

/// <summary>
/// Read-only view of the connection settings the <see cref="IApiClient"/> needs. The base URL
/// is always user-configurable and never hardcoded (P9); it is the full path including
/// <c>/api/v1</c> with any trailing slash trimmed.
/// </summary>
public interface IApiSettings
{
    /// <summary>
    /// The full API base URL including <c>/api/v1</c>, e.g. <c>http://127.0.0.1:8080/api/v1</c>,
    /// with no trailing slash. Endpoint paths are appended to this.
    /// </summary>
    string BaseUrl { get; }
}
