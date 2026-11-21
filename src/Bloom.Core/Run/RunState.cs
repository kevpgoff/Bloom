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
    /// <summary>Between fights: the player may rearrange the ship, then starts the next fight.</summary>
    Refit,
    Fight,
    /// <summary>A fight was won and the player picks a piece from <see cref="RunState.Reward"/> or skips it.</summary>
    Reward,
    Won,
    Lost,
}

/// <summary>
/// Everything about a run in progress. This is what the save file holds, so it must contain all state
/// and nothing that can be derived from content. Bump <see cref="CurrentSchemaVersion"/> when the shape changes.
/// </summary>
public sealed class RunState
{
    public const int CurrentSchemaVersion = 3;

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

    public SectorMap? Map { get; set; }

    public int? MapNodeId { get; set; }

    /// <summary>
    /// The enemies of this run, in order, fixed when the run starts. A placeholder until the sector map
    /// exists: the run is won when the last of them is beaten.
    /// </summary>
    public List<string> Encounters { get; set; } = [];

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
        _ => RunPhase.Refit,
    };

    public PieceInstance NewPiece(string defId) => new(NextPieceId++, defId);
}
