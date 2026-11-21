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
}
