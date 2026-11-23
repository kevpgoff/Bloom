using Bloom.Core.Combat;
using Bloom.Core.Grid;
using Bloom.Core.Map;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class ShipyardTests
{
    private readonly ShipyardEngine _yard = new(Db);
    private readonly MapEngine _map = new(Db);

    private static string Json(RunState run) => SaveSerializer.Serialize(run);

    /// <summary>Every cell of the grid, for filling the hull.</summary>
    private static IEnumerable<Cell> WholeGrid =>
        Enumerable.Range(0, Rules.GridColumns).SelectMany(x => Enumerable.Range(0, Rules.GridRows).Select(y => new Cell(x, y)));

    /// <summary>
    /// Test setup: the ship at the shipyard on node 1 of the tiny map. With <paramref name="sections"/>, the
    /// offer is replaced by those section ids.
    /// </summary>
    private RunState AtShipyard(params string[] sections)
    {
        var run = UseTinyMap(NewRun(), middle: NodeType.Shipyard);
        run.Path.Add(0);
        _map.Choose(run, 1);
        if (sections.Length > 0)
        {
            run.Shipyard = new ShipyardVisit([.. sections]);
        }

        return run;
    }

    [Fact]
    public void ChoosingAShipyardOffersThreeSections()
    {
        var run = AtShipyard();
        Assert.Equal(RunPhase.Shipyard, run.Phase);
        Assert.Equal(Rules.ShipyardChoices, run.Shipyard!.Sections.Count);
        Assert.Equal(run.Shipyard.Sections.Distinct(), run.Shipyard.Sections);
        Assert.All(run.Shipyard.Sections, id => Db.Section(id));
    }

    [Fact]
    public void EverySectionGetsOffered()
    {
        var seen = new HashSet<string>();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var run = UseTinyMap(NewRun(seed), middle: NodeType.Shipyard);
            run.Path.Add(0);
            _map.Choose(run, 1);
            seen.UnionWith(run.Shipyard!.Sections);
        }

        Assert.Equal(Db.Sections.Select(s => s.Id).Order(), seen.Order());
    }

    [Fact]
    public void AttachingASectionAddsItsCellsAndReturnsToTheMap()
    {
        var run = AtShipyard("rib", "pod", "keel");
        Assert.True(_yard.CanAttach(run, 0, new Cell(5, 0), Rotation.R90));
        _yard.Attach(run, 0, new Cell(5, 0), Rotation.R90);
        Assert.Equal(28, run.Ship.Grid.CellCount);
        Assert.All(new[] { new Cell(5, 0), new Cell(5, 1), new Cell(5, 2) }, c => Assert.True(run.Ship.Grid.Contains(c)));
        Assert.Null(run.Shipyard);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal([2], _map.Reachable(run).Select(n => n.Id));
    }

    [Fact]
    public void ASectionCanBeTurned()
    {
        var run = AtShipyard("crook", "pod", "keel");
        _yard.Attach(run, 0, new Cell(0, 5), Rotation.R90);
        Assert.All(new[] { new Cell(0, 5), new Cell(1, 5), new Cell(0, 6), new Cell(0, 7) }, c => Assert.True(run.Ship.Grid.Contains(c)));
        Assert.False(run.Ship.Grid.Contains(new Cell(1, 6)));
    }

    [Fact]
    public void PiecesFitOnAnAttachedSection()
    {
        var run = AtShipyard("rib", "pod", "keel");
        _yard.Attach(run, 1, new Cell(0, 5), Rotation.R0);
        var garden = run.NewPiece("garden");
        run.Cargo.Add(garden);
        new RefitEngine(Db).FitFromCargo(run, garden.Id, new Cell(0, 6), Rotation.R0);
        Assert.NotNull(run.Ship.Find(garden.Id));
    }

    [Theory]
    [InlineData(5, 5, Rotation.R90)] // in the free corner, touching no hull
    [InlineData(0, 0, Rotation.R0)] // over the hull
    [InlineData(4, 7, Rotation.R0)] // off the grid's right edge
    public void ASectionThatCannotAttachThereIsRefused(int x, int y, Rotation rotation)
    {
        var run = AtShipyard("rib", "pod", "keel");
        Assert.False(_yard.CanAttach(run, 0, new Cell(x, y), rotation));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _yard.Attach(run, 0, new Cell(x, y), rotation));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void ASectionThatIsNotOfferedIsRefused()
    {
        var run = AtShipyard("rib", "pod", "keel");
        Assert.Null(_yard.Offered(run, 3));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _yard.Attach(run, 3, new Cell(5, 0), Rotation.R90));
        Assert.Throws<InvalidOperationException>(() => _yard.Attach(run, -1, new Cell(5, 0), Rotation.R90));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void LeavingWithoutASectionKeepsTheHull()
    {
        var run = AtShipyard();
        var cells = run.Ship.Grid.Cells;
        _yard.Leave(run);
        Assert.Equal(cells, run.Ship.Grid.Cells);
        Assert.Equal(RunPhase.Map, run.Phase);
    }

    [Fact]
    public void OnlySectionsThatFitAreOffered()
    {
        var run = UseTinyMap(NewRun(), middle: NodeType.Shipyard);
        // Every cell is hull but a column of three at the right edge, where only the rib fits.
        run.Ship.Grid = new ShipGrid(WholeGrid.Where(c => !(c.X == 5 && c.Y < 3)).ToList());
        run.Path.Add(0);
        _map.Choose(run, 1);
        Assert.Equal(["rib"], run.Shipyard!.Sections);
    }

    [Fact]
    public void AFullGridIsOfferedNothing()
    {
        var run = UseTinyMap(NewRun(), middle: NodeType.Shipyard);
        run.Ship.Grid = new ShipGrid(WholeGrid.ToList());
        run.Path.Add(0);
        _map.Choose(run, 1);
        Assert.Equal(RunPhase.Shipyard, run.Phase);
        Assert.Empty(run.Shipyard!.Sections);
        _yard.Leave(run);
        Assert.Equal(RunPhase.Map, run.Phase);
    }

    [Fact]
    public void PlacementsListEveryWayASectionAttaches()
    {
        var run = NewRun();
        var keel = Db.Section("keel").Shape;
        var placements = ShipyardEngine.Placements(run.Ship.Grid, keel).ToList();

        // A line of 5 lies along row 5 from x 0 or 1, or stands in column 5 from y 0 to 3, each two ways round.
        Assert.Equal(12, placements.Count);
        Assert.Equal((new Cell(0, 5), Rotation.R0), placements[0]);
        Assert.All(placements, p => Assert.Equal(AttachError.None, run.Ship.Grid.CanAttach(keel.CellsAt(p.Origin, p.Rotation))));
    }

    [Fact]
    public void TheShipCannotBeRefittedOrLeftEarlyAtAShipyard()
    {
        var run = AtShipyard();
        var held = run.NewPiece("lance");
        run.Cargo.Add(held);
        string before = Json(run);
        Assert.False(RefitEngine.CanRefit(run));
        Assert.Throws<InvalidOperationException>(() => new RefitEngine(Db).FitFromCargo(run, held.Id, new Cell(1, 1), Rotation.R0));
        Assert.Empty(_map.Reachable(run));
        Assert.Throws<InvalidOperationException>(() => _map.Choose(run, 2));
        Assert.Throws<InvalidOperationException>(() => new CombatEngine(Db).StartFight(run, "dummy"));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void AttachingAwayFromAShipyardIsRefused()
    {
        var run = NewRun();
        Assert.False(_yard.CanAttach(run, 0, new Cell(5, 0), Rotation.R90));
        Assert.Null(_yard.Offered(run, 0));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _yard.Attach(run, 0, new Cell(5, 0), Rotation.R90));
        Assert.Throws<InvalidOperationException>(() => _yard.Leave(run));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void AShipyardVisitAndTheGrownHullSurviveASave()
    {
        var run = AtShipyard("rib", "pod", "keel");
        var restored = SaveSerializer.Deserialize(Json(run));
        Assert.Equal(RunPhase.Shipyard, restored.Phase);
        Assert.Equal(["rib", "pod", "keel"], restored.Shipyard!.Sections);

        _yard.Attach(restored, 1, new Cell(0, 5), Rotation.R0);
        var again = SaveSerializer.Deserialize(Json(restored));
        Assert.True(again.Ship.Grid.Contains(new Cell(1, 6)));
        Assert.Equal(Json(restored), Json(again));
    }

    /// <summary>The owner, 2026-11-23: a hard fight gives a free hull section after its reward, so its rare piece has room.</summary>
    [Fact]
    public void AHardFightGivesAFreeSectionAfterItsReward()
    {
        var run = UseTinyMap(NewRun());
        run.Path.AddRange([0, 1]);
        _map.Choose(run, 2);
        var combat = new CombatEngine(Db);
        var fight = combat.StartFight(run, run.Encounter!.EnemyId);
        fight.Enemy.Hull = 1;
        combat.PlayCard(run, Take(fight, "ram"));
        Assert.True(run.Reward!.Section);

        new RewardEngine(Db).Take(run, 0);
        Assert.Equal(RunPhase.Shipyard, run.Phase);
        Assert.Equal(Rules.ShipyardChoices, run.Shipyard!.Sections.Count);
        var (origin, rotation) = ShipyardEngine.Placements(run.Ship.Grid, _yard.Offered(run, 0)!.Shape).First();
        _yard.Attach(run, 0, origin, rotation);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal([3], _map.Reachable(run).Select(n => n.Id));
    }

    /// <summary>The spec: a boss gives a free hull section. It comes after the boss's reward, before the next sector's first node.</summary>
    [Fact]
    public void ABossBeforeTheLastSectorGivesAFreeSection()
    {
        var run = UseTinyMap(NewRun());
        run.Sectors = 2;
        run.Path.AddRange([0, 1, 2, 3]);
        run.Reward = new RewardOffer(["lance"], Section: true);
        new RewardEngine(Db).Skip(run);
        Assert.Equal(2, run.Sector);
        Assert.Empty(run.Path);
        Assert.Equal(RunPhase.Shipyard, run.Phase);
        Assert.Equal(Rules.ShipyardChoices, run.Shipyard!.Sections.Count);

        var (origin, rotation) = ShipyardEngine.Placements(run.Ship.Grid, _yard.Offered(run, 0)!.Shape).First();
        _yard.Attach(run, 0, origin, rotation);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal(2, run.Sector);
        Assert.Equal(run.Map!.StartNodes().Select(n => n.Id), _map.Reachable(run).Select(n => n.Id));
    }

    [Fact]
    public void ABossOffersNoSectionWhenNoneFits()
    {
        var run = UseTinyMap(NewRun());
        run.Sectors = 2;
        run.Ship.Grid = new ShipGrid(WholeGrid.ToList());
        run.Path.AddRange([0, 1, 2, 3]);
        run.Reward = new RewardOffer(["lance"], Section: true);
        new RewardEngine(Db).Skip(run);
        Assert.Equal(2, run.Sector);
        Assert.Null(run.Shipyard);
        Assert.Equal(RunPhase.Map, run.Phase);
    }
}
