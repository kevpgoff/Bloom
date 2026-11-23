using Bloom.Core.Map;
using Bloom.Core.Pieces;

namespace Bloom.Core;

/// <summary>
/// Tuning numbers proposed in the design spec. All of them are placeholders for testing.
/// Content-specific numbers (hull points, card values) live in game/data instead.
/// </summary>
public static class Rules
{
    /// <summary>The grid is at most 6 columns wide so that each cell stays at least 48 points wide on a phone.</summary>
    public const int GridColumns = 6;

    public const int GridRows = 8;

    public const int EnergyPerTurn = 3;

    public const int CardsDrawnPerTurn = 5;

    /// <summary>Unfitted pieces that can wait in the cargo hold.</summary>
    public const int CargoCapacity = 3;

    public const int MinEquipmentCells = 2;
    public const int MaxEquipmentCells = 6;
    public const int MinCrewCells = 1;
    public const int MaxCrewCells = 3;

    public const int MinCardsPerPiece = 1;
    public const int MaxCardsPerPiece = 4;

    /// <summary>Pieces offered after a won fight; the player takes one or skips.</summary>
    public const int RewardChoices = 3;

    /// <summary>The chance, in percent, that each offered piece is of a rarity. Decided by the owner on 2026-11-21 as placeholders.</summary>
    public static readonly IReadOnlyList<(Rarity Rarity, int Percent)> RewardRarityOdds =
    [
        (Rarity.Common, 60),
        (Rarity.Uncommon, 30),
        (Rarity.Rare, 10),
    ];

    /// <summary>How much more likely a reward is when it shares a tag with a fitted piece (the spec: rewards favor the ship's tags).</summary>
    public const int RewardTagWeight = 3;

    /// <summary>Refit steps that Undo can take back. The oldest are dropped past this, to keep the save small.</summary>
    public const int MaxUndoSteps = 50;

    // The sector map. Decided by the owner on 2026-11-22: one sector for now, built so that more is mostly content.

    /// <summary>Sectors in a run. Beating the boss of the last one wins the run.</summary>
    public const int SectorsPerRun = 1;

    /// <summary>Rows of choices below the boss. A path visits one node per row, then the boss.</summary>
    public const int MapRows = 7;

    /// <summary>Columns of the map. Four keep every node large enough to tap on a phone.</summary>
    public const int MapColumns = 4;

    /// <summary>Paths walked up the map to make it; where they meet, they share nodes.</summary>
    public const int MapPaths = 4;

    /// <summary>The first rows hold only skirmishes, so a run opens with fights.</summary>
    public const int MapOpeningRows = 2;

    /// <summary>Skirmishes in the rows below this draw early enemies; above it, late ones.</summary>
    public const int MapEarlyRows = 3;

    /// <summary>The type of every node on the row just before the boss, or null for weighted types.</summary>
    public static readonly NodeType? MapRowBeforeBoss = NodeType.Grove;

    /// <summary>
    /// How often each type appears on the other rows. A type with no weight is never placed; shipyards and
    /// events get theirs when they are built.
    /// </summary>
    public static readonly IReadOnlyList<(NodeType Type, int Weight)> MapNodeWeights =
    [
        (NodeType.Skirmish, 40),
        (NodeType.HardFight, 15),
        (NodeType.Dock, 10),
        (NodeType.Grove, 5),
    ];

    /// <summary>Types that never follow themselves on a path.</summary>
    public static readonly IReadOnlyList<NodeType> MapNoRepeatTypes = [NodeType.HardFight, NodeType.Dock, NodeType.Shipyard, NodeType.Grove];

    /// <summary>Types that every map has at least once, among those with a weight.</summary>
    public static readonly IReadOnlyList<NodeType> MapRequiredTypes = [NodeType.Dock, NodeType.Shipyard];

    /// <summary>Salvage for a won fight, from the first number up to and including the second.</summary>
    public static readonly (int Min, int Max) SkirmishSalvage = (20, 30);

    public static readonly (int Min, int Max) HardFightSalvage = (40, 55);

    /// <summary>For the boss of a sector that is not the last; the last one ends the run.</summary>
    public static readonly (int Min, int Max) BossSalvage = (60, 80);

    /// <summary>Salvage for composting a piece from the cargo hold.</summary>
    public const int CompostSalvage = 10;

    /// <summary>A grove repairs this share of the maximum hull, rounded up.</summary>
    public const int GroveRepairPercent = 30;

    // Docks. Prices are the spec's starting prices for testing.

    /// <summary>What a piece costs at a dock, by rarity.</summary>
    public static int PiecePrice(Rarity rarity) => rarity switch
    {
        Rarity.Common => 40,
        Rarity.Uncommon => 70,
        _ => 120,
    };

    /// <summary>A dock buys a piece for this share of its price, rounded down.</summary>
    public const int SellPercent = 50;

    /// <summary>Pieces for sale at each dock, rolled like a reward's.</summary>
    public const int DockPieces = 3;

    /// <summary>Hull points one repair at a dock restores, for <see cref="DockRepairPrice"/>. It can be bought again.</summary>
    public const int DockRepairHull = 10;

    public const int DockRepairPrice = 20;
}
