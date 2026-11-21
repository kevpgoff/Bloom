using Bloom.Core.Grid;

namespace Bloom.Core.Pieces;

/// <summary>A piece on the ship. <see cref="Origin"/> is the top-left corner of the rotated shape.</summary>
public sealed record FittedPiece(PieceInstance Piece, Cell Origin, Rotation Rotation);

/// <summary>The hull and the pieces fitted on it. Change it through <see cref="ShipFitter"/>.</summary>
public sealed class ShipLayout
{
    public required ShipGrid Grid { get; set; }

    public List<FittedPiece> Fitted { get; set; } = [];

    public FittedPiece? Find(int pieceId) => Fitted.Find(f => f.Piece.Id == pieceId);
}

/// <summary>
/// Unfitted pieces. The hold may go over <see cref="Rules.CargoCapacity"/> while the player refits, but
/// the next fight cannot start until the extra pieces are fitted or composted.
/// </summary>
public sealed class CargoHold
{
    public List<PieceInstance> Pieces { get; set; } = [];

    public bool IsFull => Pieces.Count >= Rules.CargoCapacity;

    /// <summary>Pieces over the capacity.</summary>
    public int Excess => Math.Max(0, Pieces.Count - Rules.CargoCapacity);

    /// <summary>Adds a piece even past the capacity.</summary>
    public void Add(PieceInstance piece) => Pieces.Add(piece);

    public bool TryAdd(PieceInstance piece)
    {
        if (IsFull)
        {
            return false;
        }

        Pieces.Add(piece);
        return true;
    }

    public PieceInstance Take(int pieceId)
    {
        int index = Pieces.FindIndex(p => p.Id == pieceId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Piece {pieceId} is not in the cargo hold.");
        }

        var piece = Pieces[index];
        Pieces.RemoveAt(index);
        return piece;
    }
}
