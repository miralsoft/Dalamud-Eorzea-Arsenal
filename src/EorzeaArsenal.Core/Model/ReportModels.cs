namespace EorzeaArsenal.Model;

/// <summary>Where a drop was seen, as <c>seen</c> of a drop report names it.</summary>
public static class DropSeen
{
    /// <summary>In the duty's loot chest to roll on, whoever wins it.</summary>
    public const string Chest = "chest";

    /// <summary>Straight into the inventory, without a chest.</summary>
    public const string Received = "received";
}

/// <summary>One drop: an item from a duty on a day, and where it was seen.</summary>
public sealed record DropReport
{
    /// <summary>The item that dropped (a coffer is reported as itself).</summary>
    public required int ItemId { get; init; }

    /// <summary>How many, 1 to 999.</summary>
    public required int Qty { get; init; }

    /// <summary>The duty's InstanceContent row id.</summary>
    public required int ContentId { get; init; }

    /// <summary>The day in UTC, <c>YYYY-MM-DD</c>.</summary>
    public required string Day { get; init; }

    /// <summary><see cref="DropSeen.Chest"/> or <see cref="DropSeen.Received"/>.</summary>
    public required string Seen { get; init; }
}

/// <summary>Body of <c>POST /loot/reports</c> (server 1.5). No character, world or account, ever.</summary>
public sealed class DropReportRequest
{
    /// <summary>The installation's random id; the server keeps only a keyed hash of it.</summary>
    public required string InstallationId { get; init; }

    /// <summary>The client's build string, at most 32 characters.</summary>
    public required string GameVersion { get; init; }

    /// <summary>1 to 50 reports.</summary>
    public required IReadOnlyList<DropReport> Reports { get; init; }
}

/// <summary>A thing from the wanted list seen at a spot in a zone on a day.</summary>
public sealed record WorldReport
{
    /// <summary><c>npc</c>, <c>mob</c> or <c>fate</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The game's id of that kind, from the wanted list.</summary>
    public required int Id { get; init; }

    /// <summary>The territory's PlaceName row id.</summary>
    public required int ZoneId { get; init; }

    /// <summary>Map coordinate x, one decimal.</summary>
    public required double X { get; init; }

    /// <summary>Map coordinate y, one decimal.</summary>
    public required double Y { get; init; }

    /// <summary>The day in UTC, <c>YYYY-MM-DD</c>.</summary>
    public required string Day { get; init; }
}

/// <summary>Body of <c>POST /world/reports</c> (server 1.5). Never the player's own position.</summary>
public sealed class WorldReportRequest
{
    /// <summary>The installation's random id, the same as for drops.</summary>
    public required string InstallationId { get; init; }

    /// <summary>The client's build string, at most 32 characters.</summary>
    public required string GameVersion { get; init; }

    /// <summary>1 to 100 reports.</summary>
    public required IReadOnlyList<WorldReport> Reports { get; init; }
}

/// <summary>The 202 answer of both report calls.</summary>
public sealed class ReportAck
{
    /// <summary>How many were taken.</summary>
    public int Accepted { get; init; }

    /// <summary>The ones refused, by 0-based index; never sent again.</summary>
    public List<RejectedReport> Rejected { get; init; } = [];
}

/// <summary>One refused report.</summary>
public sealed class RejectedReport
{
    /// <summary>Its position in the call.</summary>
    public int Index { get; init; }

    /// <summary>Why, such as <c>unknown_item</c>.</summary>
    public string? Reason { get; init; }
}

/// <summary>Response of <c>GET /world/wanted</c> (server 1.5).</summary>
public sealed class WorldWantedResponse
{
    /// <summary>A hash of the lists that changes whenever they do.</summary>
    public string? Version { get; init; }

    /// <summary>The lists.</summary>
    public WorldWanted? Wanted { get; init; }
}

/// <summary>What the world database still wants placed, per kind, sorted ascending.</summary>
public sealed class WorldWanted
{
    /// <summary>ENpcResident rows.</summary>
    public List<int> Npc { get; init; } = [];

    /// <summary>BNpcName rows.</summary>
    public List<int> Mob { get; init; } = [];

    /// <summary>Fate rows.</summary>
    public List<int> Fate { get; init; } = [];
}
