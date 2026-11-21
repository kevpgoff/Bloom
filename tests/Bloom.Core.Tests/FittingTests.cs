using Bloom.Core.Cards;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class FittingTests
{
    private readonly ShipFitter _fitter = new(Db);

    [Fact]
    public void APieceFitsOnFreeHullCells() =>
        Assert.Equal(FitError.None, _fitter.CanPlace(NewRun().Ship, "lance", new Cell(0, 0), Rotation.R0));

    [Fact]
    public void APieceOutsideTheGridIsRejected() =>
        Assert.Equal(FitError.OutOfBounds, _fitter.CanPlace(NewRun().Ship, "lance", new Cell(4, 0), Rotation.R0));

    [Fact]
    public void APieceOffTheHullIsRejected() =>
        // Column 5 is inside the grid but outside the 5-wide test hull.
        Assert.Equal(FitError.OffHull, _fitter.CanPlace(NewRun().Ship, "lance", new Cell(3, 0), Rotation.R0));

    [Fact]
    public void APieceOverlappingAnotherIsRejected() =>
        Assert.Equal(FitError.Overlaps, _fitter.CanPlace(NewRun().Ship, "lance", new Cell(0, 4), Rotation.R0));

    [Fact]
    public void RotationChangesTheCellsAPieceCovers()
    {
        var ship = NewRun().Ship;
        Assert.Equal(FitError.OutOfBounds, _fitter.CanPlace(ship, "lance", new Cell(4, 0), Rotation.R0));
        Assert.Equal(FitError.None, _fitter.CanPlace(ship, "lance", new Cell(4, 0), Rotation.R90));
    }

    [Fact]
    public void AMoveMayOverlapThePiecesOwnOldCells()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        _fitter.Move(run.Ship, lance.Id, new Cell(1, 0), Rotation.R0);
        Assert.Equal(new Cell(1, 0), run.Ship.Find(lance.Id)!.Origin);
    }

    [Fact]
    public void AMoveIntoAnotherPieceIsRejectedAndChangesNothing()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        var plating = Fit(run, "plating", 0, 1);
        Assert.Throws<InvalidOperationException>(() => _fitter.Move(run.Ship, plating.Id, new Cell(0, 0), Rotation.R0));
        Assert.Equal(new Cell(0, 1), run.Ship.Find(plating.Id)!.Origin);
    }

    [Fact]
    public void TheBridgeCanBeMovedButNotRemoved()
    {
        var run = NewRun();
        int bridge = BridgeId(run);
        Assert.False(_fitter.CanRemove(run.Ship, bridge));
        Assert.Throws<InvalidOperationException>(() => _fitter.Remove(run.Ship, bridge));
        _fitter.Move(run.Ship, bridge, new Cell(3, 0), Rotation.R0);
        Assert.Equal(new Cell(3, 0), run.Ship.Find(bridge)!.Origin);
    }

    [Fact]
    public void OtherPiecesCanBeRemoved()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        Assert.Equal(lance, _fitter.Remove(run.Ship, lance.Id));
        Assert.Null(run.Ship.Find(lance.Id));
    }

    [Fact]
    public void PiecesThatShareAnEdgeAreAdjacent()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        var plating = Fit(run, "plating", 0, 1);
        Assert.True(_fitter.AreAdjacent(run.Ship, lance.Id, plating.Id));
        Assert.Contains(_fitter.AdjacentTo(run.Ship, lance.Id), f => f.Piece.Id == plating.Id);
    }

    [Fact]
    public void PiecesThatTouchOnlyAtACornerAreNotAdjacent()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);     // (0,0) (1,0) (2,0)
        var grinder = Fit(run, "grinder", 3, 1); // (3,1) (4,1): meets (2,0) only at a corner
        Assert.False(_fitter.AreAdjacent(run.Ship, lance.Id, grinder.Id));
    }

    [Fact]
    public void ABonusGoesToAdjacentPiecesWithTheMatchingTag()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        var grinder = Fit(run, "grinder", 0, 1);
        var bonus = Assert.Single(_fitter.ActiveBonuses(run.Ship, []));
        Assert.Equal(lance.Id, bonus.ReceiverId);
        Assert.Equal(grinder.Id, bonus.GiverId);
        Assert.Equal(new DamageBonusEffect(2), bonus.Bonus.Effect);
    }

    [Fact]
    public void ABonusSkipsPiecesWithOtherTags()
    {
        var run = NewRun();
        Fit(run, "plating", 0, 0);
        Fit(run, "grinder", 0, 1);
        Assert.Empty(_fitter.ActiveBonuses(run.Ship, []));
    }

    [Fact]
    public void ANamedBonusIsReceivedOnlyOnce()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 1, 1);
        Fit(run, "grinder", 1, 0);
        Fit(run, "grinder", 1, 2);
        Assert.Single(_fitter.ActiveBonuses(run.Ship, []), b => b.ReceiverId == lance.Id);
    }

    [Fact]
    public void DisabledPiecesGiveNoBonus()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        var grinder = Fit(run, "grinder", 0, 1);
        Assert.Empty(_fitter.ActiveBonuses(run.Ship, [grinder.Id]));
    }

    [Fact]
    public void TheFrontmostWorkingPieceInALaneIsFound()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        var lance = Fit(run, "lance", 0, 1);
        Assert.Equal(plating.Id, _fitter.FrontmostInLane(run.Ship, 0, [])!.Piece.Id);
        Assert.Equal(lance.Id, _fitter.FrontmostInLane(run.Ship, 1, [plating.Id])!.Piece.Id);
        Assert.Null(_fitter.FrontmostInLane(run.Ship, 4, []));
    }

    [Fact]
    public void TheCargoHoldTakesThreePieces()
    {
        var run = NewRun();
        var cargo = run.Cargo;
        for (int i = 0; i < Rules.CargoCapacity; i++)
        {
            Assert.True(cargo.TryAdd(run.NewPiece("lance")));
        }

        var extra = run.NewPiece("lance");
        Assert.False(cargo.TryAdd(extra));
        Assert.True(cargo.IsFull);

        var first = cargo.Pieces[0];
        Assert.Equal(first, cargo.Take(first.Id));
        Assert.True(cargo.TryAdd(extra));
    }
}
