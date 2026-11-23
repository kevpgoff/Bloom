using Bloom.Core.Content;
using Bloom.Core.Pieces;

namespace Bloom.Core.Run;

/// <summary>
/// A dock: the spec's shop that sells pieces and hull repair and buys unwanted pieces. The player may buy,
/// sell and repair as often as salvage allows, refit in between, and leaves when done.
/// </summary>
/// <remarks>
/// Defaults taken on 2026-11-22: <see cref="Rules.DockPieces"/> pieces are for sale, rolled by the reward
/// rules when the node is chosen; prices are the spec's (<see cref="Rules.PiecePrice"/>); a dock buys any
/// piece but the bridge, fitted or held, for <see cref="Rules.SellPercent"/>% of its price; a repair restores
/// <see cref="Rules.DockRepairHull"/> hull for <see cref="Rules.DockRepairPrice"/> salvage. A bought piece
/// goes to the cargo hold, even past its capacity. Decided by the owner on 2026-11-23: each dock has a
/// variable number of repairs (<see cref="Rules.DockRepairs"/>), rolled with its pieces. Each trade clears Undo, since a
/// refit step undone past a trade would take back salvage or pieces the trade already moved.
/// </remarks>
public sealed class DockEngine(ContentDb content)
{
    private readonly ShipFitter _fitter = new(content);

    public int Price(string pieceDefId) => Rules.PiecePrice(content.Piece(pieceDefId).Rarity);

    /// <summary>What the dock pays for a piece: <see cref="Rules.SellPercent"/>% of its price, rounded down.</summary>
    public int SellPrice(PieceInstance piece) => Price(piece.DefId) * Rules.SellPercent / 100;

    /// <summary>Hull points a repair would restore now: <see cref="Rules.DockRepairHull"/>, but not past the maximum.</summary>
    public static int RepairAmount(RunState run) => Math.Min(Rules.DockRepairHull, run.MaxHull - run.Hull);

    public bool CanBuy(RunState run, int index) =>
        run.Phase == RunPhase.Dock && run.Dock is { } dock
        && index >= 0 && index < dock.Pieces.Count
        && run.Salvage >= Price(dock.Pieces[index]);

    public static bool CanRepair(RunState run) =>
        run.Phase == RunPhase.Dock && run.Dock is { Repairs: > 0 } && RepairAmount(run) > 0 && run.Salvage >= Rules.DockRepairPrice;

    /// <summary>Whether the dock would buy this piece now: it is held, or fitted and not the bridge.</summary>
    public bool CanSell(RunState run, int pieceId) =>
        run.Phase == RunPhase.Dock
        && (run.Cargo.Pieces.Exists(p => p.Id == pieceId) || (run.Ship.Find(pieceId) is not null && _fitter.CanRemove(run.Ship, pieceId)));

    /// <summary>Buys the piece for sale at <paramref name="index"/> into the cargo hold.</summary>
    public PieceInstance Buy(RunState run, int index)
    {
        var dock = RequireDock(run);
        if (index < 0 || index >= dock.Pieces.Count)
        {
            throw new InvalidOperationException($"There is no piece {index} for sale; the dock has {dock.Pieces.Count}.");
        }

        int price = Price(dock.Pieces[index]);
        if (run.Salvage < price)
        {
            throw new InvalidOperationException($"'{dock.Pieces[index]}' costs {price} salvage, but the ship has {run.Salvage}.");
        }

        run.Salvage -= price;
        var piece = run.NewPiece(dock.Pieces[index]);
        dock.Pieces.RemoveAt(index);
        run.Cargo.Add(piece);
        run.UndoStack.Clear();
        return piece;
    }

    /// <summary>Repairs <see cref="RepairAmount"/> hull for <see cref="Rules.DockRepairPrice"/> salvage, using one of the dock's repairs.</summary>
    public void Repair(RunState run)
    {
        var dock = RequireDock(run);
        if (dock.Repairs <= 0)
        {
            throw new InvalidOperationException("The dock has no repairs left.");
        }

        if (RepairAmount(run) == 0)
        {
            throw new InvalidOperationException("The hull is already whole.");
        }

        if (run.Salvage < Rules.DockRepairPrice)
        {
            throw new InvalidOperationException($"A repair costs {Rules.DockRepairPrice} salvage, but the ship has {run.Salvage}.");
        }

        run.Salvage -= Rules.DockRepairPrice;
        run.Hull += RepairAmount(run);
        run.Dock = dock with { Repairs = dock.Repairs - 1 };
        run.UndoStack.Clear();
    }

    /// <summary>Sells a held or fitted piece, never the bridge. Returns the salvage it paid.</summary>
    public int Sell(RunState run, int pieceId)
    {
        RequireDock(run);
        PieceInstance piece;
        if (run.Cargo.Pieces.Find(p => p.Id == pieceId) is { } held)
        {
            piece = held;
            run.Cargo.Take(pieceId);
        }
        else if (run.Ship.Find(pieceId) is not null)
        {
            if (!_fitter.CanRemove(run.Ship, pieceId))
            {
                throw new InvalidOperationException("The bridge stays on the ship.");
            }

            piece = _fitter.Remove(run.Ship, pieceId);
        }
        else
        {
            throw new InvalidOperationException($"Piece {pieceId} is neither fitted nor in the cargo hold.");
        }

        int price = SellPrice(piece);
        run.Salvage += price;
        run.UndoStack.Clear();
        return price;
    }

    /// <summary>Leaves the dock for the map. Pieces not bought stay behind.</summary>
    public void Leave(RunState run)
    {
        RequireDock(run);
        run.Dock = null;
        run.UndoStack.Clear();
        MapEngine.FinishNode(run);
    }

    private static DockVisit RequireDock(RunState run) =>
        run.Phase == RunPhase.Dock && run.Dock is { } dock
            ? dock
            : throw new InvalidOperationException("The ship is not at a dock.");
}
