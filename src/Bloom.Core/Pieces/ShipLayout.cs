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

/// <summary>Unfitted pieces. The player must sell or compost any piece beyond the capacity.</summary>
public sealed class CargoHold
{
    public List<PieceInstance> Pieces { get; set; } = [];

    public bool IsFull => Pieces.Count >= Rules.CargoCapacity;

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
