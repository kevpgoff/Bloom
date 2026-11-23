using System.Text.Json.Serialization;
using Bloom.Core.Combat;
using Bloom.Core.Map;
using Bloom.Core.Pieces;
using Bloom.Core.Rng;

namespace Bloom.Core.Run;

public enum RunResult
{
    InProgress,
    Won,
    Lost,
}

/// <summary>Where the run is in its loop. It is worked out from the state; see <see cref="RunState.Phase"/>.</summary>
public enum RunPhase
{
    /// <summary>On the sector map, choosing the next node. The player may refit the ship.</summary>
    Map,

    /// <summary>A fight node was chosen and its enemy is known (<see cref="RunState.Encounter"/>). The player may refit, then starts the fight.</summary>
    Prepare,
    Fight,

    /// <summary>A fight was won and the player picks a piece from <see cref="RunState.Reward"/> or skips it.</summary>
    Reward,

    /// <summary>At a dock (<see cref="RunState.Dock"/>): buying, selling and repairing until the player leaves. The player may refit.</summary>
    Dock,

    /// <summary>At a shipyard, or after a hard fight or a boss gave a hull section (<see cref="RunState.Shipyard"/>): attaching one offered section or none.</summary>
    Shipyard,

    /// <summary>At a grove (<see cref="RunState.Grove"/>), choosing what it does for the ship.</summary>
    Grove,
    Won,
    Lost,
}

/// <summary>
/// Everything about a run in progress. This is what the save file holds, so it must contain all state
/// and nothing that can be derived from content. Bump <see cref="CurrentSchemaVersion"/> when the shape changes.
/// </summary>
public sealed class RunState
{
    public const int CurrentSchemaVersion = 7;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ulong Seed { get; set; }

    /// <summary>All randomness in the run comes from here.</summary>
    public required Pcg32 Rng { get; set; }

    public required string ShipClassId { get; set; }

    public int Hull { get; set; }

    public int MaxHull { get; set; }

    public int Salvage { get; set; }

    public required ShipLayout Ship { get; set; }

    public CargoHold Cargo { get; set; } = new();

    public int NextPieceId { get; set; } = 1;

    public int Sector { get; set; } = 1;

    /// <summary>Sectors in this run, fixed when it starts (<see cref="Rules.SectorsPerRun"/>). Beating the boss of the last one wins it.</summary>
    public int Sectors { get; set; } = Rules.SectorsPerRun;

    /// <summary>The map of the current sector. Every run has one; it is null only while a run is being made.</summary>
    public SectorMap? Map { get; set; }

    /// <summary>The nodes entered in this sector, in order. The last one is where the ship is.</summary>
    public List<int> Path { get; set; } = [];

    /// <summary>Every enemy chosen for a fight this run, so that a pool repeats an enemy only once it runs out.</summary>
    public List<string> EnemiesMet { get; set; } = [];

    /// <summary>The fight the player has committed to, from choosing its node until the fight ends.</summary>
    public Encounter? Encounter { get; set; }

    /// <summary>Set while the ship is at a dock, until the player leaves it.</summary>
    public DockVisit? Dock { get; set; }

    /// <summary>Set while hull sections are on offer, until the player attaches one or leaves.</summary>
    public ShipyardVisit? Shipyard { get; set; }

    /// <summary>Set while the ship is at a grove and the player has not yet chosen what it does.</summary>
    public GroveVisit? Grove { get; set; }

    public int FightsWon { get; set; }

    public CombatState? Combat { get; set; }

    /// <summary>The most recent finished fight, or null before the first one ends.</summary>
    public FightRecord? LastFight { get; set; }

    /// <summary>The pieces offered after the last won fight, until the player takes one or skips.</summary>
    public RewardOffer? Reward { get; set; }

    /// <summary>The ship before each refit step since the last fight, newest last. Undo restores the newest.</summary>
    public List<ShipSnapshot> UndoStack { get; set; } = [];

    public RunResult Result { get; set; }

    [JsonIgnore]
    public RunPhase Phase => Result switch
    {
        RunResult.Won => RunPhase.Won,
        RunResult.Lost => RunPhase.Lost,
        _ when Combat is not null => RunPhase.Fight,
        _ when Reward is not null => RunPhase.Reward,
        _ when Encounter is not null => RunPhase.Prepare,
        _ when Dock is not null => RunPhase.Dock,
        _ when Shipyard is not null => RunPhase.Shipyard,
        _ when Grove is not null => RunPhase.Grove,
        _ => RunPhase.Map,
    };

    /// <summary>The node the ship is at, or null before the first node of a sector.</summary>
    [JsonIgnore]
    public MapNode? CurrentNode => Map is { } map && Path.Count > 0 ? map.Node(Path[^1]) : null;

    public PieceInstance NewPiece(string defId) => new(NextPieceId++, defId);
}
