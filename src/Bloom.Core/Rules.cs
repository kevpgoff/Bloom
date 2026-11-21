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
}
