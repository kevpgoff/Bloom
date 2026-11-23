using Bloom.Core.Grid;
using Bloom.Core.Map;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class DockTests
{
    private readonly DockEngine _dock = new(Db);
    private readonly MapEngine _map = new(Db);

    private static string Json(RunState run) => SaveSerializer.Serialize(run);

    /// <summary>Test setup: the ship at a dock that sells a common lance, the uncommon drone and the rare spear.</summary>
    private static RunState AtDock(int salvage = 500)
    {
        var run = NewRun();
        run.Salvage = salvage;
        run.Dock = new DockVisit(["lance", "drone", "spear"]);
        return run;
    }

    [Fact]
    public void ChoosingADockRollsPiecesForSale()
    {
        var run = UseTinyMap(NewRun(), middle: NodeType.Dock);
        run.Path.Add(0);
        _map.Choose(run, 1);
        Assert.Equal(RunPhase.Dock, run.Phase);
        Assert.Equal(Rules.DockPieces, run.Dock!.Pieces.Count);
        Assert.Equal(run.Dock.Pieces.Distinct(), run.Dock.Pieces);
        Assert.DoesNotContain("bridge", run.Dock.Pieces);
    }

    [Fact]
    public void PricesFollowRarity()
    {
        Assert.Equal(40, _dock.Price("lance"));
        Assert.Equal(70, _dock.Price("drone"));
        Assert.Equal(120, _dock.Price("spear"));
    }

    [Fact]
    public void BuyingPaysThePriceAndPutsThePieceInTheHold()
    {
        var run = AtDock(salvage: 100);
        var piece = _dock.Buy(run, 1);
        Assert.Equal("drone", piece.DefId);
        Assert.Equal(30, run.Salvage);
        Assert.Equal([piece], run.Cargo.Pieces);
        Assert.Equal(["lance", "spear"], run.Dock!.Pieces);
        Assert.Equal(RunPhase.Dock, run.Phase);
    }

    [Fact]
    public void BuyingGoesPastAFullHold()
    {
        var run = AtDock();
        for (int i = 0; i < Rules.CargoCapacity; i++)
        {
            run.Cargo.Add(run.NewPiece("plating"));
        }

        _dock.Buy(run, 0);
        Assert.Equal(1, run.Cargo.Excess);
    }

    [Fact]
    public void BuyingWithoutEnoughSalvageIsRefused()
    {
        var run = AtDock(salvage: 119);
        Assert.False(_dock.CanBuy(run, 2));
        Assert.True(_dock.CanBuy(run, 1));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _dock.Buy(run, 2));
        Assert.Throws<InvalidOperationException>(() => _dock.Buy(run, 3));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void SellingAHeldPiecePaysHalfItsPrice()
    {
        var run = AtDock(salvage: 0);
        var drone = run.NewPiece("drone");
        run.Cargo.Add(drone);
        Assert.Equal(35, _dock.SellPrice(drone));
        Assert.True(_dock.CanSell(run, drone.Id));
        Assert.Equal(35, _dock.Sell(run, drone.Id));
        Assert.Equal(35, run.Salvage);
        Assert.Empty(run.Cargo.Pieces);
    }

    [Fact]
    public void SellingAFittedPieceTakesItOffTheShip()
    {
        var run = AtDock(salvage: 0);
        var lance = Fit(run, "lance", 1, 1);
        Assert.Equal(20, _dock.Sell(run, lance.Id));
        Assert.Null(run.Ship.Find(lance.Id));
        Assert.Empty(run.Cargo.Pieces);
    }

    [Fact]
    public void AnUpgradedPieceSellsForTheSamePrice()
    {
        var run = AtDock(salvage: 0);
        var lance = Fit(run, "lance", 1, 1);
        PieceUpgrades.Upgrade(Db, run, lance.Id);
        Assert.Equal(20, _dock.Sell(run, lance.Id));
    }

    [Fact]
    public void TheBridgeIsNotForSale()
    {
        var run = AtDock();
        Assert.False(_dock.CanSell(run, BridgeId(run)));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _dock.Sell(run, BridgeId(run)));
        Assert.Throws<InvalidOperationException>(() => _dock.Sell(run, 999));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void ARepairRestoresTenHullForTwentySalvage()
    {
        var run = AtDock(salvage: 45);
        run.Hull = 30;
        Assert.True(DockEngine.CanRepair(run));
        _dock.Repair(run);
        _dock.Repair(run);
        Assert.Equal(50, run.Hull);
        Assert.Equal(5, run.Salvage);
        Assert.False(DockEngine.CanRepair(run));
        Assert.Throws<InvalidOperationException>(() => _dock.Repair(run));
    }

    [Fact]
    public void ARepairNeverGoesPastTheMaximum()
    {
        var run = AtDock(salvage: 100);
        run.Hull = run.MaxHull - 4;
        Assert.Equal(4, DockEngine.RepairAmount(run));
        _dock.Repair(run);
        Assert.Equal(run.MaxHull, run.Hull);
        Assert.Equal(80, run.Salvage);

        Assert.False(DockEngine.CanRepair(run));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _dock.Repair(run));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void TheShipCanBeRefittedAtADock()
    {
        var run = AtDock();
        var held = run.NewPiece("lance");
        run.Cargo.Add(held);
        var refit = new RefitEngine(Db);
        Assert.True(RefitEngine.CanRefit(run));
        refit.FitFromCargo(run, held.Id, new Cell(1, 1), Rotation.R0);
        Assert.NotNull(run.Ship.Find(held.Id));
        Assert.True(refit.CanUndo(run));
        Assert.False(refit.CanStartFight(run));
        Assert.Throws<InvalidOperationException>(() => refit.StartFight(run));
    }

    [Fact]
    public void ATradeClearsUndo()
    {
        var run = AtDock();
        var refit = new RefitEngine(Db);
        var lance = Fit(run, "lance", 1, 1);
        refit.Unfit(run, lance.Id);
        Assert.True(refit.CanUndo(run));
        _dock.Sell(run, lance.Id);
        Assert.False(refit.CanUndo(run));
    }

    [Fact]
    public void LeavingReturnsToTheMap()
    {
        var run = UseTinyMap(NewRun(), middle: NodeType.Dock);
        run.Path.Add(0);
        _map.Choose(run, 1);
        _dock.Leave(run);
        Assert.Null(run.Dock);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal([2], _map.Reachable(run).Select(n => n.Id));
    }

    [Fact]
    public void ADockCannotBeLeftForAFightOrTheMapEarly()
    {
        var run = UseTinyMap(NewRun(), middle: NodeType.Dock);
        run.Path.Add(0);
        _map.Choose(run, 1);
        string before = Json(run);
        Assert.Empty(_map.Reachable(run));
        Assert.Throws<InvalidOperationException>(() => _map.Choose(run, 2));
        Assert.Throws<InvalidOperationException>(() => new Combat.CombatEngine(Db).StartFight(run, "dummy"));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void TradingAwayFromADockIsRefused()
    {
        var run = NewRun();
        run.Salvage = 500;
        var held = run.NewPiece("lance");
        run.Cargo.Add(held);
        run.Hull = 10;
        Assert.False(_dock.CanSell(run, held.Id));
        Assert.False(DockEngine.CanRepair(run));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _dock.Buy(run, 0));
        Assert.Throws<InvalidOperationException>(() => _dock.Sell(run, held.Id));
        Assert.Throws<InvalidOperationException>(() => _dock.Repair(run));
        Assert.Throws<InvalidOperationException>(() => _dock.Leave(run));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void ADockVisitSurvivesASave()
    {
        var run = AtDock();
        _dock.Buy(run, 0);
        var restored = SaveSerializer.Deserialize(Json(run));
        Assert.Equal(RunPhase.Dock, restored.Phase);
        Assert.Equal(["drone", "spear"], restored.Dock!.Pieces);
        Assert.Equal(Json(run), Json(restored));
    }
}
