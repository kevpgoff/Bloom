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

/// <summary>
/// Everything about a run in progress. This is what the save file holds, so it must contain all state
/// and nothing that can be derived from content. Bump <see cref="CurrentSchemaVersion"/> when the shape changes.
/// </summary>
public sealed class RunState
{
    public const int CurrentSchemaVersion = 1;

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

    public CombatState? Combat { get; set; }

    public RunResult Result { get; set; }

    public PieceInstance NewPiece(string defId) => new(NextPieceId++, defId);
}
