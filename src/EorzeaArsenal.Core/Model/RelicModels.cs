namespace EorzeaArsenal.Model;

/// <summary>Response of <c>GET /relics?lang=</c> (server 1.5): the relic catalog.</summary>
public sealed class RelicCatalogResponse
{
    /// <summary>The catalog.</summary>
    public RelicCatalog? Data { get; init; }
}

/// <summary>Every relic series, in the server's order.</summary>
public sealed class RelicCatalog
{
    /// <summary>The series, from the Zodiac weapons on.</summary>
    public List<RelicSeries> Series { get; init; } = [];
}

/// <summary>One relic series: its stages in order and each job's pieces of each stage.</summary>
public sealed class RelicSeries
{
    /// <summary>The series key, such as <c>phantom</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary><c>combat</c> or <c>craft</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>The expansion code, such as <c>dt</c>.</summary>
    public string? Expansion { get; init; }

    /// <summary>The series name in the language asked.</summary>
    public string? Name { get; init; }

    /// <summary>The series' jobs, in the server's order.</summary>
    public List<string> Jobs { get; init; } = [];

    /// <summary>The stages in order.</summary>
    public List<RelicStage> Stages { get; init; } = [];
}

/// <summary>One stage of a series.</summary>
public sealed class RelicStage
{
    /// <summary>The stage key, such as <c>penumbrae</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The stage's item level.</summary>
    public int Ilvl { get; init; }

    /// <summary>
    /// Each job's pieces of the stage, keyed by job code. A job with no entry has no piece in this stage,
    /// so the stage does not exist for it. A paladin's combat stage has two pieces.
    /// </summary>
    public Dictionary<string, List<RelicItem>> Items { get; init; } = [];
}

/// <summary>One relic piece.</summary>
public sealed class RelicItem
{
    /// <summary>The item id.</summary>
    public long Id { get; init; }

    /// <summary>The name in the language asked, or <c>#&lt;id&gt;</c> without one.</summary>
    public string? Name { get; init; }
}

/// <summary>Response of <c>GET /me/relics</c> and <c>PUT /me/relics</c>.</summary>
public sealed class MyRelicsResponse
{
    /// <summary>The character's progress, or <see langword="null"/> when the account has no character.</summary>
    public MyRelics? Data { get; init; }
}

/// <summary>How far one character is in every series.</summary>
public sealed class MyRelics
{
    /// <summary>The character the answer is for.</summary>
    public string? CharacterId { get; init; }

    /// <summary>Progress per series.</summary>
    public List<MyRelicSeries> Series { get; init; } = [];

    /// <summary>Whether the inventory sync ever sent this character's storages.</summary>
    public bool InventorySynced { get; init; }
}

/// <summary>Progress in one series.</summary>
public sealed class MyRelicSeries
{
    /// <summary>The series key.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Jobs whose last stage is reached.</summary>
    public int Done { get; init; }

    /// <summary>Jobs that have at least one stage in the series.</summary>
    public int Of { get; init; }

    /// <summary>Progress per job code.</summary>
    public Dictionary<string, MyRelicJob> Jobs { get; init; } = [];
}

/// <summary>One job's progress in a series; every stage key refers to the catalog.</summary>
public sealed class MyRelicJob
{
    /// <summary>The highest stage of which the character holds every piece.</summary>
    public string? Held { get; init; }

    /// <summary>The stage the player ticked by hand.</summary>
    public string? Ticked { get; init; }

    /// <summary>The higher of the two; every stage up to it is done.</summary>
    public string? Reached { get; init; }

    /// <summary>The stage after <see cref="Reached"/>; <see langword="null"/> when the job is done.</summary>
    public string? Next { get; init; }

    /// <summary>Whether the job's last stage is reached.</summary>
    public bool Done { get; init; }
}

/// <summary>
/// Body of <c>PUT /me/relics</c>: tick a stage by hand, or clear the tick with an empty stage. A tick is the
/// player's word and is sent only when the player asks for it.
/// </summary>
public sealed class RelicTickRequest
{
    /// <summary>The character; without one the tick goes to the account's active character.</summary>
    public long? CharacterId { get; init; }

    /// <summary>The series key.</summary>
    public required string Series { get; init; }

    /// <summary>The job code.</summary>
    public required string Job { get; init; }

    /// <summary>
    /// The stage key, or <c>""</c> to clear the tick. Never <see langword="null"/>: a null member is left out
    /// of the body, and the contract names <c>null</c> or <c>""</c> as the way to clear.
    /// </summary>
    public required string Stage { get; init; }
}
