using Bloom.Core.Combat;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class RefitTests
{
    private readonly RefitEngine _refit = new(Db);
    private readonly CombatEngine _combat = new(Db);
    private readonly RewardEngine _rewards = new(Db);

    private static string Json(RunState run) => SaveSerializer.Serialize(run);

    private static PieceInstance Hold(RunState run, string defId)
    {
        var piece = run.NewPiece(defId);
        run.Cargo.Add(piece);
        return piece;
    }

    private void WinFight(RunState run)
    {
        run.Combat!.Enemy.Hull = 1;
        _combat.PlayCard(run, Take(run.Combat, "ram"));
    }

    [Fact]
    public void ANewRunStartsBetweenFights()
    {
        var run = NewRun();
        Assert.Equal(RunPhase.Refit, run.Phase);
        Assert.Null(run.Combat);
        Assert.Equal(["dummy", "heavy", "blocker", "mixed"], run.Encounters);
        Assert.Equal("dummy", _refit.NextEnemy(run)!.Id);
        Assert.True(_refit.CanStartFight(run));
    }

    [Fact]
    public void FightsFollowTheEncountersInOrder()
    {
        var run = NewRun();
        run.Encounters = ["dummy", "blocker"];
        _refit.StartNextFight(run);
        Assert.Equal("dummy", run.Combat!.Enemy.DefId);

        WinFight(run);
        _rewards.Skip(run);
        Assert.Equal("blocker", _refit.NextEnemy(run)!.Id);
        _refit.StartNextFight(run);
        Assert.Equal("blocker", run.Combat!.Enemy.DefId);

        WinFight(run);
        Assert.Equal(RunPhase.Won, run.Phase);
        Assert.Null(_refit.NextEnemy(run));
        Assert.False(_refit.CanStartFight(run));
    }

    /// <summary>The spec: the layout locks when a fight starts. It also waits while a reward is offered.</summary>
    [Fact]
    public void TheShipCannotBeChangedDuringAFightOrAReward()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 1, 1);
        var held = Hold(run, "plating");
        Action<RunState>[] actions =
        [
            r => _refit.Move(r, lance.Id, new Cell(1, 2), Rotation.R0),
            r => _refit.Rotate(r, lance.Id),
            r => _refit.Unfit(r, lance.Id),
            r => _refit.FitFromCargo(r, held.Id, new Cell(0, 3), Rotation.R0),
            r => _refit.Compost(r, held.Id),
            r => _refit.Undo(r),
            r => _refit.StartNextFight(r),
        ];

        _refit.StartNextFight(run);
        AssertAllRefused(run, actions);

        WinFight(run);
        Assert.Equal(RunPhase.Reward, run.Phase);
        AssertAllRefused(run, actions);
    }

    private static void AssertAllRefused(RunState run, IEnumerable<Action<RunState>> actions)
    {
        string before = Json(run);
        foreach (var action in actions)
        {
            Assert.Throws<InvalidOperationException>(() => action(run));
            Assert.Equal(before, Json(run));
        }
    }

    [Fact]
    public void APieceCanBeMoved()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        _refit.Move(run, lance.Id, new Cell(2, 1), Rotation.R90);
        Assert.Equal(new FittedPiece(lance, new Cell(2, 1), Rotation.R90), run.Ship.Find(lance.Id));
    }

    [Theory]
    [InlineData(0, 0, Rotation.R0)] // where it already is
    [InlineData(0, 1, Rotation.R0)] // onto the plating
    [InlineData(3, 0, Rotation.R0)] // off the hull
    [InlineData(4, 3, Rotation.R0)] // out of the grid
    public void AMoveThatCannotHappenThrowsAndChangesNothing(int x, int y, Rotation rotation)
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        Fit(run, "plating", 0, 1);
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _refit.Move(run, lance.Id, new Cell(x, y), rotation));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void APieceFromTheHoldCanBeFitted()
    {
        var run = NewRun();
        var held = Hold(run, "lance");
        _refit.FitFromCargo(run, held.Id, new Cell(1, 1), Rotation.R0);
        Assert.Empty(run.Cargo.Pieces);
        Assert.Equal(new FittedPiece(held, new Cell(1, 1), Rotation.R0), run.Ship.Find(held.Id));
    }

    [Fact]
    public void AHeldPieceThatDoesNotFitStaysInTheHold()
    {
        var run = NewRun();
        var held = Hold(run, "lance");
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _refit.FitFromCargo(run, held.Id, new Cell(0, 4), Rotation.R0));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void UnfittingPutsThePieceInTheHoldEvenWhenItIsFull()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        for (int i = 0; i < Rules.CargoCapacity; i++)
        {
            Hold(run, "tinker");
        }

        _refit.Unfit(run, lance.Id);
        Assert.Null(run.Ship.Find(lance.Id));
        Assert.Equal(lance, run.Cargo.Pieces[^1]);
        Assert.Equal(1, _refit.CargoExcess(run));
    }

    [Fact]
    public void TheBridgeCannotBeUnfitted()
    {
        var run = NewRun();
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _refit.Unfit(run, BridgeId(run)));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void CompostingRemovesAHeldPieceButNotAFittedOne()
    {
        var run = NewRun();
        var held = Hold(run, "tinker");
        var lance = Fit(run, "lance", 0, 0);

        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _refit.Compost(run, lance.Id));
        Assert.Equal(before, Json(run));

        _refit.Compost(run, held.Id);
        Assert.Empty(run.Cargo.Pieces);
    }

    [Fact]
    public void TurningKeepsThePieceCentered()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 1, 1); // (1,1) (2,1) (3,1)
        Assert.True(_refit.CanRotate(run, lance.Id));
        _refit.Rotate(run, lance.Id);
        var turned = run.Ship.Find(lance.Id)!;
        Assert.Equal(Rotation.R90, turned.Rotation);
        Assert.Equal([new Cell(2, 0), new Cell(2, 1), new Cell(2, 2)], new ShipFitter(Db).CellsOf(turned));
    }

    [Fact]
    public void TurningNudgesThePieceWhenItsCenterSpotIsBlocked()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0); // centered, it would stick out above the hull
        _refit.Rotate(run, lance.Id);
        Assert.Equal(new FittedPiece(lance, new Cell(1, 0), Rotation.R90), run.Ship.Find(lance.Id));
    }

    [Fact]
    public void TurningWithoutRoomThrowsAndChangesNothing()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 1, 2);
        Fit(run, "plating", 1, 1);
        Fit(run, "plating", 1, 3);
        string before = Json(run);
        Assert.False(_refit.CanRotate(run, lance.Id));
        Assert.Throws<InvalidOperationException>(() => _refit.Rotate(run, lance.Id));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void UndoTakesBackEachStepInTurn()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        var held = Hold(run, "tinker");
        string start = Json(run);

        _refit.Move(run, lance.Id, new Cell(0, 1), Rotation.R0);
        string moved = Json(run);
        _refit.Compost(run, held.Id);
        string composted = Json(run);
        _refit.Unfit(run, lance.Id);

        _refit.Undo(run);
        Assert.Equal(composted, Json(run));
        _refit.Undo(run);
        Assert.Equal(moved, Json(run));
        _refit.Undo(run);
        Assert.Equal(start, Json(run));

        Assert.False(_refit.CanUndo(run));
        Assert.Throws<InvalidOperationException>(() => _refit.Undo(run));
        Assert.Equal(start, Json(run));
    }

    [Fact]
    public void UndoReachesBackOnlyToTheLastFight()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        _refit.Move(run, lance.Id, new Cell(0, 1), Rotation.R0);
        _refit.StartNextFight(run);
        Assert.Empty(run.UndoStack);

        WinFight(run);
        _rewards.Take(run, 0);
        Assert.False(_refit.CanUndo(run));
    }

    [Fact]
    public void UndoKeepsOnlyTheMostRecentSteps()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        for (int i = 0; i < Rules.MaxUndoSteps + 10; i++)
        {
            _refit.Move(run, lance.Id, new Cell(0, i % 2 == 0 ? 1 : 0), Rotation.R0);
        }

        Assert.Equal(Rules.MaxUndoSteps, run.UndoStack.Count);
    }

    [Fact]
    public void TheNextFightWaitsUntilTheHoldIsWithinItsCapacity()
    {
        var run = NewRun();
        var pieces = Enumerable.Range(0, Rules.CargoCapacity + 1).Select(_ => Hold(run, "tinker")).ToList();
        Assert.Equal(1, _refit.CargoExcess(run));
        Assert.False(_refit.CanStartFight(run));

        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _refit.StartNextFight(run));
        Assert.Equal(before, Json(run));

        _refit.Compost(run, pieces[0].Id);
        Assert.True(_refit.CanStartFight(run));
        _refit.StartNextFight(run);
        Assert.Equal(RunPhase.Fight, run.Phase);
    }

    [Fact]
    public void ThePreviewShowsTheBonusAPieceWouldTurnOn()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        var grinder = Hold(run, "grinder");
        string before = Json(run);

        var preview = _refit.PreviewPlacement(run, grinder.Id, new Cell(0, 1), Rotation.R0);

        Assert.True(preview.Fits);
        var bonus = Assert.Single(preview.Bonuses);
        Assert.Equal((grinder.Id, lance.Id), (bonus.GiverId, bonus.ReceiverId));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void ThePreviewOfASpotThatDoesNotFitHasNoBonuses()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        var grinder = Hold(run, "grinder");
        var preview = _refit.PreviewPlacement(run, grinder.Id, new Cell(1, 0), Rotation.R0);
        Assert.Equal(FitError.Overlaps, preview.Error);
        Assert.Empty(preview.Bonuses);
    }

    [Fact]
    public void ThePreviewOfAFittedPieceIgnoresItsOwnCells()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        var grinder = Fit(run, "grinder", 2, 1);

        var preview = _refit.PreviewPlacement(run, lance.Id, new Cell(1, 0), Rotation.R0); // overlaps its old cells

        Assert.True(preview.Fits);
        Assert.Equal(grinder.Id, Assert.Single(preview.Bonuses).GiverId);
        Assert.Equal(new Cell(0, 0), run.Ship.Find(lance.Id)!.Origin);
    }
}
