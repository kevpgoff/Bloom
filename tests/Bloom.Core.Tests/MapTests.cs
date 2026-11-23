using Bloom.Core.Combat;
using Bloom.Core.Map;
using Bloom.Core.Rng;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class MapGeneratorTests
{
    private static SectorMap Make(ulong seed) => MapGenerator.Generate(new Pcg32(seed), sector: 1);

    private static IEnumerable<SectorMap> ManyMaps() => Enumerable.Range(0, 300).Select(seed => Make((ulong)seed));

    [Fact]
    public void TheSameSeedMakesTheSameMap()
    {
        var a = Make(42);
        var b = Make(42);
        Assert.Equal(a.Nodes, b.Nodes);
        Assert.Equal(a.Edges, b.Edges);
        Assert.NotEqual(a.Edges, Make(43).Edges);
    }

    [Fact]
    public void NodesAreInRowOrderWithTheBossLastAlone()
    {
        foreach (var map in ManyMaps())
        {
            Assert.Equal(Enumerable.Range(0, map.Nodes.Count), map.Nodes.Select(n => n.Id));
            Assert.Equal(map.Nodes.OrderBy(n => n.Row).ThenBy(n => n.Column), map.Nodes);
            Assert.Equal(NodeType.Boss, map.Boss.Type);
            Assert.Equal(Rules.MapRows, map.Boss.Row);
            Assert.Single(map.Nodes, n => n.Type == NodeType.Boss);
            Assert.All(map.Nodes.SkipLast(1), n => Assert.InRange(n.Column, 0, Rules.MapColumns - 1));
        }
    }

    [Fact]
    public void EveryRowHasAChoiceAndEveryNodeLeadsToTheBoss()
    {
        foreach (var map in ManyMaps())
        {
            for (int row = 0; row < Rules.MapRows; row++)
            {
                Assert.InRange(map.Nodes.Count(n => n.Row == row), 1, Rules.MapColumns);
            }

            Assert.True(map.StartNodes().Count() >= 2, "the first row offers a choice");
            foreach (var node in map.Nodes.SkipLast(1))
            {
                var next = map.NextNodes(node.Id).ToList();
                Assert.NotEmpty(next);
                Assert.All(next, n => Assert.Equal(node.Row + 1, n.Row));
                Assert.All(next, n => Assert.True(n.Type == NodeType.Boss || Math.Abs(n.Column - node.Column) <= 1));
                if (node.Row > 0)
                {
                    Assert.NotEmpty(map.PreviousNodes(node.Id));
                }
            }
        }
    }

    [Fact]
    public void EdgesNeverCross()
    {
        foreach (var map in ManyMaps())
        {
            var edges = map.Edges.Select(e => (From: map.Node(e.From), To: map.Node(e.To))).Where(e => e.To.Type != NodeType.Boss).ToList();
            foreach (var a in edges)
            {
                foreach (var b in edges.Where(b => b.From.Row == a.From.Row))
                {
                    bool crosses = (a.From.Column < b.From.Column && a.To.Column > b.To.Column)
                                   || (a.From.Column > b.From.Column && a.To.Column < b.To.Column);
                    Assert.False(crosses, $"{a} crosses {b}");
                }
            }
        }
    }

    [Fact]
    public void TheOpeningRowsAreSkirmishesAndTheRowBeforeTheBossIsGroves()
    {
        foreach (var map in ManyMaps())
        {
            Assert.All(map.Nodes.Where(n => n.Row < Rules.MapOpeningRows), n => Assert.Equal(NodeType.Skirmish, n.Type));
            Assert.All(map.Nodes.Where(n => n.Row == Rules.MapRows - 1), n => Assert.Equal(NodeType.Grove, n.Type));
        }
    }

    [Fact]
    public void OnlyTypesWithAWeightArePlaced()
    {
        var placed = ManyMaps().SelectMany(m => m.Nodes).Select(n => n.Type).Distinct().Order().ToList();
        var allowed = Rules.MapNodeWeights.Where(w => w.Weight > 0).Select(w => w.Type)
            .Append(NodeType.Skirmish).Append(NodeType.Boss).Append(Rules.MapRowBeforeBoss!.Value)
            .Distinct().Order().ToList();
        Assert.Equal(allowed, placed);
    }

    [Fact]
    public void NoRepeatTypesNeverFollowThemselves()
    {
        foreach (var map in ManyMaps())
        {
            foreach (var edge in map.Edges)
            {
                var (from, to) = (map.Node(edge.From), map.Node(edge.To));
                if (Rules.MapNoRepeatTypes.Contains(from.Type))
                {
                    Assert.NotEqual(from.Type, to.Type);
                }
            }
        }
    }

    [Fact]
    public void AMapKeepsTheSectorItWasMadeFor() =>
        Assert.Equal(3, MapGenerator.Generate(new Pcg32(1), sector: 3).Sector);
}

public class MapEngineTests
{
    private readonly MapEngine _map = new(Db);
    private readonly CombatEngine _combat = new(Db);
    private readonly RewardEngine _rewards = new(Db);
    private readonly GroveEngine _grove = new();

    private static string Json(RunState run) => SaveSerializer.Serialize(run);

    private void WinFight(RunState run)
    {
        new RefitEngine(Db, _combat).StartFight(run);
        run.Combat!.Enemy.Hull = 1;
        _combat.PlayCard(run, Take(run.Combat, "ram"));
    }

    [Fact]
    public void ANewSectorStartsBeforeItsFirstRow()
    {
        var run = NewRun();
        Assert.Empty(run.Path);
        Assert.Null(run.CurrentNode);
        Assert.Equal(run.Map!.StartNodes(), _map.Reachable(run));
    }

    [Fact]
    public void ChoosingASkirmishCommitsToAFightAgainstAnEarlyEnemy()
    {
        var run = UseTinyMap(NewRun());
        _map.Choose(run, 0);
        Assert.Equal([0], run.Path);
        Assert.Equal(RunPhase.Prepare, run.Phase);
        Assert.Contains(run.Encounter!.EnemyId, new[] { "dummy", "blocker" });
        Assert.Equal([run.Encounter.EnemyId], run.EnemiesMet);
        Assert.Empty(_map.Reachable(run));
    }

    [Theory]
    [InlineData(NodeType.Skirmish, 0, EnemyPool.Early)]
    [InlineData(NodeType.Skirmish, Rules.MapEarlyRows - 1, EnemyPool.Early)]
    [InlineData(NodeType.Skirmish, Rules.MapEarlyRows, EnemyPool.Late)]
    [InlineData(NodeType.HardFight, 0, EnemyPool.Hard)]
    [InlineData(NodeType.Boss, Rules.MapRows, EnemyPool.Boss)]
    [InlineData(NodeType.Grove, 3, null)]
    public void EachNodeDrawsFromItsPool(NodeType type, int row, EnemyPool? pool) =>
        Assert.Equal(pool, MapEngine.PoolFor(new MapNode(0, type, row, 0)));

    [Fact]
    public void AnEnemyRepeatsOnlyOnceItsPoolRunsOut()
    {
        var run = NewRun();
        var met = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            UseTinyMap(run);
            _map.Choose(run, 0);
            met.Add(run.Encounter!.EnemyId);
            run.Encounter = null;
        }

        // The early pool is dummy and blocker: both before either repeats.
        Assert.Equal(["blocker", "dummy"], met.Take(2).Order());
        Assert.Equal(met, run.EnemiesMet);
    }

    [Fact]
    public void AGroveRepairsThirtyPercentThenReturnsToTheMap()
    {
        var run = UseTinyMap(NewRun());
        run.Path.Add(0);
        run.Hull = 20;
        _map.Choose(run, 1);
        Assert.Equal(RunPhase.Grove, run.Phase);
        Assert.Equal(18, GroveEngine.RepairAmount(run)); // 30% of 60

        _grove.Repair(run);
        Assert.Equal(38, run.Hull);
        Assert.Null(run.Grove);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal([2], _map.Reachable(run).Select(n => n.Id));
    }

    [Fact]
    public void AGroveNeverRepairsPastTheMaximum()
    {
        var run = NewRun();
        run.Hull = run.MaxHull - 5;
        run.Grove = new GroveVisit();
        Assert.Equal(5, GroveEngine.RepairAmount(run));
        _grove.Repair(run);
        Assert.Equal(run.MaxHull, run.Hull);
    }

    [Fact]
    public void RepairingAwayFromAGroveThrowsAndChangesNothing()
    {
        var run = NewRun();
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _grove.Repair(run));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void ANodeThatCannotBeReachedIsRefusedAndNothingChanges()
    {
        var run = UseTinyMap(NewRun());
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _map.Choose(run, 1));
        Assert.Throws<InvalidOperationException>(() => _map.Choose(run, 99));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void TheNextNodeWaitsUntilThisOneIsDone()
    {
        var run = UseTinyMap(NewRun());
        _map.Choose(run, 0);
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _map.Choose(run, 1));
        Assert.Equal(before, Json(run));

        WinFight(run);
        Assert.Empty(_map.Reachable(run)); // the reward waits
        Assert.Throws<InvalidOperationException>(() => _map.Choose(run, 1));

        _rewards.Skip(run);
        Assert.Equal([1], _map.Reachable(run).Select(n => n.Id));
    }

    [Fact]
    public void EnteringANodeClearsUndo()
    {
        var run = UseTinyMap(NewRun());
        var lance = Fit(run, "lance", 1, 1);
        new RefitEngine(Db).Move(run, lance.Id, new Grid.Cell(1, 2), Grid.Rotation.R0);
        Assert.NotEmpty(run.UndoStack);
        _map.Choose(run, 0);
        Assert.Empty(run.UndoStack);
    }

    [Fact]
    public void AHardFightNodeFightsAHardEnemyAndOffersARarePiece()
    {
        var run = UseTinyMap(NewRun());
        run.Path.AddRange([0, 1]);
        _map.Choose(run, 2);
        Assert.Equal("brute", run.Encounter!.EnemyId);
        WinFight(run);
        Assert.Equal("spear", run.Reward!.Pieces[0]);
    }

    [Fact]
    public void BeatingTheBossOfTheOnlySectorWinsTheRun()
    {
        var run = UseTinyMap(NewRun());
        run.Path.AddRange([0, 1, 2]);
        _map.Choose(run, 3);
        Assert.Equal("titan", run.Encounter!.EnemyId);
        WinFight(run);
        Assert.Equal(RunPhase.Won, run.Phase);
        Assert.Null(run.Reward);
    }

    [Fact]
    public void TheMapAndPathSurviveASave()
    {
        var run = NewRun(seed: 9);
        _map.Choose(run, _map.Reachable(run)[^1].Id);
        var restored = SaveSerializer.Deserialize(Json(run));
        Assert.Equal(run.Map!.Nodes, restored.Map!.Nodes);
        Assert.Equal(run.Map.Edges, restored.Map.Edges);
        Assert.Equal(run.Path, restored.Path);
        Assert.Equal(run.Encounter, restored.Encounter);
        Assert.Equal(RunPhase.Prepare, restored.Phase);
        Assert.Equal(Json(run), Json(restored));
    }

    [Fact]
    public void AGroveVisitSurvivesASave()
    {
        var run = UseTinyMap(NewRun());
        run.Path.Add(0);
        _map.Choose(run, 1);
        Assert.Equal(RunPhase.Grove, SaveSerializer.Deserialize(Json(run)).Phase);
    }
}
