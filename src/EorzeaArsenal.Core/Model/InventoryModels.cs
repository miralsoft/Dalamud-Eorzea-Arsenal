namespace EorzeaArsenal.Model;

/// <summary>
/// One owned, equippable item as scanned from a storage location. Sent to <c>POST /inventory</c>.
/// The <see cref="Container"/> is a display tag; the server derives the authoritative
/// <i>scope</i> from it (local containers → <c>character</c>; <c>retainer</c> → <c>retainer:&lt;source_id&gt;</c>).
/// Materia/consumables/materials are never included (only items with an equip-slot category).
/// </summary>
public sealed class InventoryItemDto
{
    /// <summary>Real item id in the range 1..9,999,999 (base id; HQ is carried by <see cref="Hq"/>).</summary>
    public required int ItemId { get; init; }

    /// <summary>
    /// Display tag for the storage location: one of <see cref="InventoryContainers"/>
    /// (<c>equipped|armoury|bags|saddlebag|glamour|armoire</c> → scope <c>character</c>;
    /// <c>retainer</c> → scope <c>retainer:&lt;source_id&gt;</c>).
    /// </summary>
    public required string Container { get; init; }

    /// <summary>Retainer id for <see cref="InventoryContainers.Retainer"/> items; otherwise <c>""</c>.</summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>Quantity owned (≥ 1).</summary>
    public int Qty { get; init; } = 1;

    /// <summary>Whether the owned item is high quality.</summary>
    public bool Hq { get; init; }
}

/// <summary>
/// A scoped snapshot of owned items read in one scan: the character it belongs to, the
/// <see cref="Scopes"/> that were <b>fully</b> scanned this time, and the <see cref="Items"/>
/// found within them. A reported-but-empty scope deletes that scope's items server-side.
/// Wrapped into an <see cref="InventoryPayload"/> before being sent.
/// </summary>
public sealed class InventoryData
{
    /// <summary>The character the items belong to (same identity/cid_hash as <c>PUT /gear</c>).</summary>
    public required CharacterDto Character { get; init; }

    /// <summary>The scopes fully scanned this time (the server replaces only these). Never <c>manual</c>.</summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>The owned items found within <see cref="Scopes"/> (may be empty to clear a scope).</summary>
    public required IReadOnlyList<InventoryItemDto> Items { get; init; }
}

/// <summary>The wire body of <c>POST /inventory</c> (protocol version 2).</summary>
public sealed class InventoryPayload
{
    /// <summary>Protocol version the plugin speaks for inventory (always <c>2</c>).</summary>
    public int ProtocolVersion { get; init; } = InventoryProtocol.ProtocolVersion;

    /// <summary>The character block.</summary>
    public required CharacterDto Character { get; init; }

    /// <summary>The scopes fully scanned this request (the server replaces only these).</summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>The owned items across the scanned scopes.</summary>
    public required IReadOnlyList<InventoryItemDto> Items { get; init; }

    /// <summary>Wraps an <see cref="InventoryData"/> snapshot into a sendable payload.</summary>
    /// <param name="data">The scanned snapshot.</param>
    /// <returns>A payload carrying the inventory protocol version.</returns>
    public static InventoryPayload From(InventoryData data) => new()
    {
        Character = data.Character,
        Scopes = data.Scopes,
        Items = data.Items,
    };
}

/// <summary>Success body of <c>POST /inventory</c>: <c>{ "status":"ok", "character_id":"…", "items": N }</c>.</summary>
public sealed class InventoryPushResult
{
    /// <summary>Always <c>"ok"</c> on success.</summary>
    public string? Status { get; init; }

    /// <summary>Server-side character id the items were linked to.</summary>
    public string? CharacterId { get; init; }

    /// <summary>Number of items stored.</summary>
    public int Items { get; init; }
}

/// <summary>Protocol-wide constants and scope helpers for the owned-items / inventory contract.</summary>
public static class InventoryProtocol
{
    /// <summary>The inventory protocol version this plugin implements.</summary>
    public const int ProtocolVersion = 2;

    /// <summary>Maximum number of items accepted in one request.</summary>
    public const int MaxItems = 4000;

    /// <summary>Maximum number of scopes accepted in one request.</summary>
    public const int MaxScopes = 50;

    /// <summary>Maximum serialized request body the server accepts (256 KB).</summary>
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>Maximum length of a retainer <c>source_id</c>.</summary>
    public const int MaxSourceIdLength = 64;

    /// <summary>The scope/least-privilege the key must carry for <c>POST /inventory</c> (R17).</summary>
    public const string RequiredScope = "inventory:write";

    /// <summary>Scope covering all locally readable storages together (umzieh-safe single scan).</summary>
    public const string ScopeCharacter = "character";

    /// <summary>The user's manual website markings — the plugin must <b>never</b> send this scope.</summary>
    public const string ScopeManual = "manual";

    /// <summary>Prefix of a per-retainer scope (<c>retainer:&lt;id&gt;</c>).</summary>
    public const string RetainerScopePrefix = "retainer:";

    /// <summary>Builds the scope string for one retainer.</summary>
    /// <param name="retainerId">The stable retainer id.</param>
    /// <returns><c>retainer:&lt;id&gt;</c>.</returns>
    public static string RetainerScope(string retainerId) => RetainerScopePrefix + retainerId;

    /// <summary>Whether a scope string is a well-formed <c>retainer:&lt;id&gt;</c> scope.</summary>
    /// <param name="scope">The scope string.</param>
    /// <returns><see langword="true"/> if it has the retainer prefix and a non-empty id.</returns>
    public static bool IsRetainerScope(string scope) =>
        scope.StartsWith(RetainerScopePrefix, StringComparison.Ordinal) && scope.Length > RetainerScopePrefix.Length;

    /// <summary>The scope an item belongs to, derived from its container/source (mirrors the server).</summary>
    /// <param name="item">The item.</param>
    /// <returns><c>retainer:&lt;source_id&gt;</c> for retainer items, otherwise <c>character</c>.</returns>
    public static string ScopeForItem(InventoryItemDto item) =>
        item.Container == InventoryContainers.Retainer ? RetainerScope(item.SourceId) : ScopeCharacter;
}

/// <summary>The display tags for the storage location an owned item was scanned from.</summary>
public static class InventoryContainers
{
    /// <summary>Currently worn gear.</summary>
    public const string Equipped = "equipped";

    /// <summary>Armoury chest.</summary>
    public const string Armoury = "armoury";

    /// <summary>Inventory bags.</summary>
    public const string Bags = "bags";

    /// <summary>Chocobo saddlebag (incl. premium).</summary>
    public const string Saddlebag = "saddlebag";

    /// <summary>Glamour dresser.</summary>
    public const string Glamour = "glamour";

    /// <summary>Armoire / cabinet.</summary>
    public const string Armoire = "armoire";

    /// <summary>A retainer's storage (requires <see cref="InventoryItemDto.SourceId"/>).</summary>
    public const string Retainer = "retainer";

    /// <summary>The local containers that together form the <c>character</c> scope.</summary>
    public static readonly IReadOnlySet<string> CharacterContainers = new HashSet<string>(
        [Equipped, Armoury, Bags, Saddlebag, Glamour, Armoire],
        StringComparer.Ordinal);
}
