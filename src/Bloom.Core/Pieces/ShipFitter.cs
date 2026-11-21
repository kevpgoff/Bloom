using Bloom.Core.Content;
using Bloom.Core.Grid;

namespace Bloom.Core.Pieces;

public enum FitError
{
    None,
    OutOfBounds,
    OffHull,
    Overlaps,
}

/// <summary>A bonus that is active this fight: <see cref="GiverId"/> gives it to <see cref="ReceiverId"/>.</summary>
public sealed record ActiveBonus(int ReceiverId, int GiverId, AdjacencyBonus Bonus);

/// <summary>
/// The rules of fitting: placement, moving, removal, adjacency and lanes. Stateless; it reads shapes
/// from the content and changes the <see cref="ShipLayout"/> it is given.
/// </summary>
public sealed class ShipFitter(ContentDb content)
{
    public IReadOnlyList<Cell> CellsOf(FittedPiece fitted) =>
        content.Piece(fitted.Piece.DefId).Shape.CellsAt(fitted.Origin, fitted.Rotation);

    /// <summary>
    /// Checks whether a piece fits: every cell in bounds, on the hull and free. Pass
    /// <paramref name="ignorePieceId"/> to check a move, so the piece does not collide with itself.
    /// </summary>
    public FitError CanPlace(ShipLayout layout, string defId, Cell origin, Rotation rotation, int? ignorePieceId = null)
    {
        var cells = content.Piece(defId).Shape.CellsAt(origin, rotation);
        if (cells.Any(c => !ShipGrid.InBounds(c)))
        {
            return FitError.OutOfBounds;
        }

        if (cells.Any(c => !layout.Grid.Contains(c)))
        {
            return FitError.OffHull;
        }

        var occupied = OccupiedCells(layout, ignorePieceId);
        return cells.Any(occupied.Contains) ? FitError.Overlaps : FitError.None;
    }

    public void Place(ShipLayout layout, PieceInstance piece, Cell origin, Rotation rotation)
    {
        if (layout.Find(piece.Id) is not null)
        {
            throw new InvalidOperationException($"Piece {piece.Id} is already fitted.");
        }

        ThrowIfInvalid(CanPlace(layout, piece.DefId, origin, rotation), piece.DefId, origin, rotation);
        layout.Fitted.Add(new FittedPiece(piece, origin, rotation));
    }

    public void Move(ShipLayout layout, int pieceId, Cell origin, Rotation rotation)
    {
        int index = IndexOf(layout, pieceId);
        var fitted = layout.Fitted[index];
        ThrowIfInvalid(CanPlace(layout, fitted.Piece.DefId, origin, rotation, pieceId), fitted.Piece.DefId, origin, rotation);
        layout.Fitted[index] = fitted with { Origin = origin, Rotation = rotation };
    }

    public bool CanRemove(ShipLayout layout, int pieceId) =>
        layout.Find(pieceId) is { } fitted && !content.Piece(fitted.Piece.DefId).IsBridge;

    /// <summary>Takes a piece off the ship. The caller decides where it goes (cargo, dock, compost).</summary>
    public PieceInstance Remove(ShipLayout layout, int pieceId)
    {
        int index = IndexOf(layout, pieceId);
        var fitted = layout.Fitted[index];
        if (content.Piece(fitted.Piece.DefId).IsBridge)
        {
            throw new InvalidOperationException("The bridge can be moved but not removed.");
        }

        layout.Fitted.RemoveAt(index);
        return fitted.Piece;
    }

    public bool AreAdjacent(ShipLayout layout, int pieceA, int pieceB)
    {
        if (pieceA == pieceB)
        {
            return false;
        }

        var a = layout.Find(pieceA) ?? throw NotFitted(pieceA);
        var b = layout.Find(pieceB) ?? throw NotFitted(pieceB);
        return Touches(CellsOf(a), CellsOf(b));
    }

    /// <summary>Fitted pieces that share an edge with the given piece, in layout order.</summary>
    public IReadOnlyList<FittedPiece> AdjacentTo(ShipLayout layout, int pieceId)
    {
        var self = layout.Find(pieceId) ?? throw NotFitted(pieceId);
        var selfCells = CellsOf(self);
        return layout.Fitted
            .Where(f => f.Piece.Id != pieceId && Touches(selfCells, CellsOf(f)))
            .ToList();
    }

    /// <summary>
    /// The adjacency bonuses that apply, in layout order. Disabled pieces neither give nor receive
    /// bonuses, and a piece receives each named bonus at most once.
    /// </summary>
    public IReadOnlyList<ActiveBonus> ActiveBonuses(ShipLayout layout, IReadOnlyCollection<int> disabledPieceIds)
    {
        var working = layout.Fitted
            .Where(f => !disabledPieceIds.Contains(f.Piece.Id))
            .Select(f => (Fitted: f, Def: content.Piece(f.Piece.DefId), Cells: CellsOf(f)))
            .ToList();

        var result = new List<ActiveBonus>();
        var received = new HashSet<(int ReceiverId, string BonusName)>();
        foreach (var giver in working)
        {
            if (giver.Def.Bonus is not { } bonus)
            {
                continue;
            }

            foreach (var receiver in working)
            {
                if (receiver.Fitted.Piece.Id == giver.Fitted.Piece.Id
                    || !receiver.Def.Tags.Contains(bonus.TargetTag)
                    || !Touches(giver.Cells, receiver.Cells)
                    || !received.Add((receiver.Fitted.Piece.Id, bonus.Name)))
                {
                    continue;
                }

                result.Add(new ActiveBonus(receiver.Fitted.Piece.Id, giver.Fitted.Piece.Id, bonus));
            }
        }

        return result;
    }

    /// <summary>
    /// The working piece closest to the front (lowest row) in a lane, or null when the lane has none.
    /// A heavy attack on that lane disables this piece.
    /// </summary>
    public FittedPiece? FrontmostInLane(ShipLayout layout, int lane, IReadOnlyCollection<int> disabledPieceIds)
    {
        FittedPiece? best = null;
        int bestRow = int.MaxValue;
        foreach (var fitted in layout.Fitted)
        {
            if (disabledPieceIds.Contains(fitted.Piece.Id))
            {
                continue;
            }

            foreach (var cell in CellsOf(fitted))
            {
                if (cell.X == lane && cell.Y < bestRow)
                {
                    best = fitted;
                    bestRow = cell.Y;
                }
            }
        }

        return best;
    }

    private HashSet<Cell> OccupiedCells(ShipLayout layout, int? ignorePieceId)
    {
        var occupied = new HashSet<Cell>();
        foreach (var fitted in layout.Fitted)
        {
            if (fitted.Piece.Id != ignorePieceId)
            {
                occupied.UnionWith(CellsOf(fitted));
            }
        }

        return occupied;
    }

    private static bool Touches(IReadOnlyList<Cell> a, IReadOnlyList<Cell> b)
    {
        var bSet = new HashSet<Cell>(b);
        return a.Any(cell => cell.EdgeNeighbors().Any(bSet.Contains));
    }

    private static int IndexOf(ShipLayout layout, int pieceId)
    {
        int index = layout.Fitted.FindIndex(f => f.Piece.Id == pieceId);
        return index >= 0 ? index : throw NotFitted(pieceId);
    }

    private static InvalidOperationException NotFitted(int pieceId) => new($"Piece {pieceId} is not fitted.");

    private static void ThrowIfInvalid(FitError error, string defId, Cell origin, Rotation rotation)
    {
        if (error != FitError.None)
        {
            throw new InvalidOperationException($"Cannot fit '{defId}' at {origin} {rotation}: {error}.");
        }
    }
}
