using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;

namespace Bloom.Core.Run;

/// <summary>The fitted pieces, the cargo hold and the salvage before one refit step, for Undo. Salvage is kept because composting pays it.</summary>
public sealed record ShipSnapshot(IReadOnlyList<FittedPiece> Fitted, IReadOnlyList<PieceInstance> Cargo, int Salvage);

/// <summary>What would happen if a piece were placed at a spot: whether it fits, and the bonuses it would give or receive there.</summary>
public sealed record PlacementPreview(FitError Error, IReadOnlyList<ActiveBonus> Bonuses)
{
    public bool Fits => Error == FitError.None;
}

/// <summary>
/// Refitting: moving, turning, fitting and unfitting pieces, composting them from the cargo hold, undoing
/// those steps, and starting the fight the player committed to. The spec: rearranging is free at any time
/// outside combat, and the layout locks when a fight starts. Refitting is allowed on the map and after
/// choosing a fight node (<see cref="RunPhase.Map"/> and <see cref="RunPhase.Prepare"/>).
/// </summary>
/// <remarks>
/// Each method that changes the ship validates first, then saves a <see cref="ShipSnapshot"/> to
/// <see cref="RunState.UndoStack"/>, then changes it. The undo stack is cleared when a node is entered and
/// when a fight starts. Fights start through <paramref name="combat"/>, so a session passes its own engine
/// to get the fight's first events.
/// </remarks>
public sealed class RefitEngine(ContentDb content, CombatEngine? combat = null)
{
    private readonly ShipFitter _fitter = new(content);
    private readonly CombatEngine _combat = combat ?? new(content);

    /// <summary>The enemy of the fight the player has committed to, or null when no fight is chosen.</summary>
    public EnemyDef? NextEnemy(RunState run) =>
        run.Encounter is { } encounter ? content.Enemy(encounter.EnemyId) : null;

    /// <summary>Pieces that must be fitted or composted before the next fight can start.</summary>
    public int CargoExcess(RunState run) => run.Cargo.Excess;

    /// <summary>Whether the ship can be changed now: on the map, or before a chosen fight.</summary>
    public static bool CanRefit(RunState run) => run.Phase is RunPhase.Map or RunPhase.Prepare;

    public bool CanUndo(RunState run) => CanRefit(run) && run.UndoStack.Count > 0;

    /// <summary>Whether <see cref="Rotate"/> would succeed: the run is between fights and the turned piece has room.</summary>
    public bool CanRotate(RunState run, int pieceId) =>
        CanRefit(run) && run.Ship.Find(pieceId) is { } fitted && RotatedOrigin(run.Ship, fitted) is not null;

    public bool CanStartFight(RunState run) =>
        run.Phase == RunPhase.Prepare && run.Cargo.Excess == 0;

    /// <summary>
    /// Whether a fitted or held piece would fit at a spot, and which adjacency bonuses it would give or
    /// receive there. Nothing changes; this is for the preview while the player drags a piece.
    /// </summary>
    public PlacementPreview PreviewPlacement(RunState run, int pieceId, Cell origin, Rotation rotation)
    {
        var fitted = run.Ship.Find(pieceId);
        var piece = fitted?.Piece ?? FindInCargo(run, pieceId) ?? throw NotOwned(pieceId);
        var error = _fitter.CanPlace(run.Ship, piece.DefId, origin, rotation, fitted is null ? null : pieceId);
        if (error != FitError.None)
        {
            return new PlacementPreview(error, []);
        }

        // A copy of the layout with the piece at the new spot, keeping its place in the layout order.
        var placed = new FittedPiece(piece, origin, rotation);
        var pieces = run.Ship.Fitted.ToList();
        int index = pieces.FindIndex(f => f.Piece.Id == pieceId);
        if (index >= 0)
        {
            pieces[index] = placed;
        }
        else
        {
            pieces.Add(placed);
        }

        var copy = new ShipLayout { Grid = run.Ship.Grid, Fitted = pieces };
        var bonuses = _fitter.ActiveBonuses(copy, [])
            .Where(b => b.GiverId == pieceId || b.ReceiverId == pieceId)
            .ToList();
        return new PlacementPreview(FitError.None, bonuses);
    }

    /// <summary>Moves a fitted piece to another spot or rotation.</summary>
    public void Move(RunState run, int pieceId, Cell origin, Rotation rotation)
    {
        RequireRefit(run);
        var fitted = run.Ship.Find(pieceId) ?? throw NotFitted(pieceId);
        if (fitted.Origin == origin && fitted.Rotation == rotation)
        {
            throw new InvalidOperationException($"Piece {pieceId} is already there.");
        }

        ThrowIfCannotPlace(_fitter.CanPlace(run.Ship, fitted.Piece.DefId, origin, rotation, pieceId), fitted.Piece.DefId);
        PushUndo(run);
        _fitter.Move(run.Ship, pieceId, origin, rotation);
    }

    /// <summary>
    /// Turns a fitted piece a quarter turn clockwise, keeping it centered where it was. If it doesn't fit
    /// there, the nearest spot within one cell is used. When none fits, the turn is refused.
    /// </summary>
    public void Rotate(RunState run, int pieceId)
    {
        RequireRefit(run);
        var fitted = run.Ship.Find(pieceId) ?? throw NotFitted(pieceId);
        var origin = RotatedOrigin(run.Ship, fitted)
                     ?? throw new InvalidOperationException($"There is no room to turn '{fitted.Piece.DefId}' where it is.");
        PushUndo(run);
        _fitter.Move(run.Ship, pieceId, origin, fitted.Rotation.Clockwise());
    }

    /// <summary>Fits a piece from the cargo hold.</summary>
    public void FitFromCargo(RunState run, int pieceId, Cell origin, Rotation rotation)
    {
        RequireRefit(run);
        var piece = FindInCargo(run, pieceId) ?? throw NotInCargo(pieceId);
        ThrowIfCannotPlace(_fitter.CanPlace(run.Ship, piece.DefId, origin, rotation), piece.DefId);
        PushUndo(run);
        run.Cargo.Take(pieceId);
        _fitter.Place(run.Ship, piece, origin, rotation);
    }

    /// <summary>Takes a fitted piece off the ship into the cargo hold, even past its capacity. The bridge stays.</summary>
    public void Unfit(RunState run, int pieceId)
    {
        RequireRefit(run);
        if (run.Ship.Find(pieceId) is null)
        {
            throw NotFitted(pieceId);
        }

        if (!_fitter.CanRemove(run.Ship, pieceId))
        {
            throw new InvalidOperationException("The bridge can be moved but not removed.");
        }

        PushUndo(run);
        run.Cargo.Add(_fitter.Remove(run.Ship, pieceId));
    }

    /// <summary>Gets rid of a piece in the cargo hold for <see cref="Rules.CompostSalvage"/> salvage.</summary>
    public void Compost(RunState run, int pieceId)
    {
        RequireRefit(run);
        if (FindInCargo(run, pieceId) is null)
        {
            throw NotInCargo(pieceId);
        }

        PushUndo(run);
        run.Cargo.Take(pieceId);
        run.Salvage += Rules.CompostSalvage;
    }

    /// <summary>Takes back the last refit step.</summary>
    public void Undo(RunState run)
    {
        RequireRefit(run);
        if (run.UndoStack.Count == 0)
        {
            throw new InvalidOperationException("There is nothing to undo.");
        }

        var snapshot = run.UndoStack[^1];
        run.UndoStack.RemoveAt(run.UndoStack.Count - 1);
        run.Ship.Fitted = snapshot.Fitted.ToList();
        run.Cargo.Pieces = snapshot.Cargo.ToList();
        run.Salvage = snapshot.Salvage;
    }

    /// <summary>Starts the fight the player committed to on the map. The cargo hold must be within its capacity.</summary>
    public CombatState StartFight(RunState run)
    {
        RequireRefit(run);
        if (run.Encounter is not { } encounter)
        {
            throw new InvalidOperationException("Choose a fight on the map first.");
        }

        if (run.Cargo.Excess > 0)
        {
            throw new InvalidOperationException(
                $"The cargo hold holds {Rules.CargoCapacity} pieces; fit or compost {run.Cargo.Excess} more before the next fight.");
        }

        var combat = _combat.StartFight(run, encounter.EnemyId);
        run.UndoStack.Clear();
        return combat;
    }

    /// <summary>
    /// Where a piece goes when it turns clockwise: the spot that keeps the center of its bounding box, or
    /// failing that the nearest spot within one cell (front rows first, then left), or null.
    /// </summary>
    private Cell? RotatedOrigin(ShipLayout layout, FittedPiece fitted)
    {
        var shape = content.Piece(fitted.Piece.DefId).Shape;
        var before = shape.Rotated(fitted.Rotation);
        var rotation = fitted.Rotation.Clockwise();
        var after = shape.Rotated(rotation);

        // Doubled coordinates keep a center that falls between cells in whole numbers.
        int idealX2 = (2 * fitted.Origin.X) + before.Width - after.Width;
        int idealY2 = (2 * fitted.Origin.Y) + before.Height - after.Height;
        var candidates = new List<(int Distance, Cell Origin)>();
        for (int y = (idealY2 / 2) - 2; y <= (idealY2 / 2) + 2; y++)
        {
            for (int x = (idealX2 / 2) - 2; x <= (idealX2 / 2) + 2; x++)
            {
                int dx = Math.Abs((2 * x) - idealX2);
                int dy = Math.Abs((2 * y) - idealY2);
                if (dx <= 2 && dy <= 2)
                {
                    candidates.Add((dx + dy, new Cell(x, y)));
                }
            }
        }

        foreach (var (_, origin) in candidates.OrderBy(c => c.Distance).ThenBy(c => c.Origin.Y).ThenBy(c => c.Origin.X))
        {
            if (_fitter.CanPlace(layout, fitted.Piece.DefId, origin, rotation, fitted.Piece.Id) == FitError.None)
            {
                return origin;
            }
        }

        return null;
    }

    private static void PushUndo(RunState run)
    {
        run.UndoStack.Add(new ShipSnapshot(run.Ship.Fitted.ToList(), run.Cargo.Pieces.ToList(), run.Salvage));
        if (run.UndoStack.Count > Rules.MaxUndoSteps)
        {
            run.UndoStack.RemoveAt(0);
        }
    }

    private static void RequireRefit(RunState run)
    {
        switch (run.Phase)
        {
            case RunPhase.Map or RunPhase.Prepare:
                return;
            case RunPhase.Fight:
                throw new InvalidOperationException("The layout is locked during a fight.");
            case RunPhase.Reward:
                throw new InvalidOperationException("Take or skip the reward first.");
            case RunPhase.Grove:
                throw new InvalidOperationException("Finish at the grove first.");
            default:
                throw new InvalidOperationException("The run is over.");
        }
    }

    private static PieceInstance? FindInCargo(RunState run, int pieceId) => run.Cargo.Pieces.Find(p => p.Id == pieceId);

    private static void ThrowIfCannotPlace(FitError error, string defId)
    {
        if (error != FitError.None)
        {
            throw new InvalidOperationException($"'{defId}' does not fit there: {error}.");
        }
    }

    private static InvalidOperationException NotFitted(int pieceId) => new($"Piece {pieceId} is not fitted.");

    private static InvalidOperationException NotInCargo(int pieceId) => new($"Piece {pieceId} is not in the cargo hold.");

    private static InvalidOperationException NotOwned(int pieceId) => new($"Piece {pieceId} is neither fitted nor in the cargo hold.");
}
