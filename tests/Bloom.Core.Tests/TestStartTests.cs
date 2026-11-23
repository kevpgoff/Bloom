using Bloom.Core.Combat;
using Bloom.Core.Map;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

/// <summary>Test runs, which the hangar's test panel starts at a chosen node (<see cref="RunFactory.NewTestRun"/>).</summary>
public class TestStartTests
{
    private static readonly ulong[] Seeds = Enumerable.Range(0, 40).Select(s => (ulong)s).ToArray();

    private readonly CombatEngine _combat = new(Db);

    public static TheoryData<NodeType, RunPhase> Starts => new()
    {
        { NodeType.Skirmish, RunPhase.Prepare },
        { NodeType.HardFight, RunPhase.Prepare },
        { NodeType.Boss, RunPhase.Prepare },
        { NodeType.Dock, RunPhase.Dock },
        { NodeType.Shipyard, RunPhase.Shipyard },
        { NodeType.Grove, RunPhase.Grove },
        { NodeType.Event, RunPhase.Event },
    };

    private static RunState Start(ulong seed, NodeType node, string? eventId = null) =>
        RunFactory.NewTestRun(Db, new RunSetup("test", seed), new TestStart(node, eventId));

    /// <summary>Plays the node the ship is at to its end, taking nothing: back on the map, or the run won.</summary>
    private void FinishNode(RunState run)
    {
        for (int steps = 0; run.Phase is not (RunPhase.Map or RunPhase.Won); steps++)
        {
            Assert.True(steps < 10, $"the node did not end; the run is in {run.Phase}");
            switch (run.Phase)
            {
                case RunPhase.Prepare:
                    new RefitEngine(Db, _combat).StartFight(run);
                    run.Combat!.Enemy.Hull = 1;
                    _combat.PlayCard(run, Take(run.Combat, "ram"));
                    break;
                case RunPhase.Reward:
                    new RewardEngine(Db).Skip(run);
                    break;
                case RunPhase.Dock:
                    new DockEngine(Db).Leave(run);
                    break;
                case RunPhase.Shipyard:
                    new ShipyardEngine(Db).Leave(run);
                    break;
                case RunPhase.Grove:
                    new GroveEngine(Db).Repair(run);
                    break;
                case RunPhase.Event:
                    var events = new EventEngine(Db);
                    events.Choose(run, events.Current(run)!.Choices.Count - 1); // the last choice of every test event is "Leave"
                    events.Continue(run);
                    break;
                default:
                    Assert.Fail($"unexpected phase {run.Phase}");
                    break;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Starts))]
    public void ATestRunStartsAtANodeOfTheWantedType(NodeType type, RunPhase phase)
    {
        foreach (ulong seed in Seeds)
        {
            var run = Start(seed, type);
            Assert.Equal(phase, run.Phase);
            Assert.Equal(type, run.CurrentNode!.Type);
            Assert.Equal(EnemyPoolOf(type), run.Encounter is { } fight ? Db.Enemy(fight.EnemyId).Pool : null);
        }
    }

    private static EnemyPool? EnemyPoolOf(NodeType type) => type switch
    {
        NodeType.Skirmish => EnemyPool.Early, // the first skirmish is on the first row
        NodeType.HardFight => EnemyPool.Hard,
        NodeType.Boss => EnemyPool.Boss,
        _ => null,
    };

    [Theory]
    [MemberData(nameof(Starts))]
    public void ThePathIsARealWayToTheNode(NodeType type, RunPhase phase)
    {
        _ = phase;
        foreach (ulong seed in Seeds)
        {
            var run = Start(seed, type);
            var map = run.Map!;
            Assert.Equal(0, map.Node(run.Path[0]).Row);
            Assert.Equal(run.CurrentNode!.Row + 1, run.Path.Count);
            Assert.All(run.Path.Zip(run.Path.Skip(1)), step => Assert.Contains(new MapEdge(step.First, step.Second), map.Edges));
        }
    }

    [Theory]
    [MemberData(nameof(Starts))]
    public void ATestRunIsSavedAndResumedLikeAnyOther(NodeType type, RunPhase phase)
    {
        var store = new MemorySaveStore();
        var session = RunSession.StartTest(Db, store, new RunSetup("test", 3), new TestStart(type));
        Assert.Equal(1, store.SaveCount);

        var resumed = RunSession.TryResume(Db, store)!;
        Assert.Equal(phase, resumed.State.Phase);
        Assert.Equal(SaveSerializer.Serialize(session.State), SaveSerializer.Serialize(resumed.State));
    }

    [Theory]
    [MemberData(nameof(Starts))]
    public void TheRunCarriesOnAfterTheNode(NodeType type, RunPhase phase)
    {
        _ = phase;
        var map = new MapEngine(Db);
        foreach (ulong seed in Seeds)
        {
            var run = Start(seed, type);
            var node = run.CurrentNode!;
            FinishNode(run);
            if (type == NodeType.Boss)
            {
                Assert.Equal(RunPhase.Won, run.Phase); // the only sector's boss
                continue;
            }

            var next = map.Reachable(run);
            Assert.Equal(run.Map!.NextNodes(node.Id), next);
            map.Choose(run, next[0].Id);
            Assert.Equal(node.Row + 1, run.CurrentNode!.Row);
        }
    }

    [Fact]
    public void TheSameSetupGivesTheSameTestRun()
    {
        foreach (var type in Enum.GetValues<NodeType>())
        {
            Assert.Equal(SaveSerializer.Serialize(Start(11, type)), SaveSerializer.Serialize(Start(11, type)));
        }
    }

    [Fact]
    public void ATestRunHasTheMapOfTheSameVoyage()
    {
        foreach (ulong seed in Seeds)
        {
            var voyage = RunFactory.NewRun(Db, "test", seed);
            var test = Start(seed, NodeType.Boss);
            Assert.Equal(voyage.Map!.Nodes, test.Map!.Nodes);
            Assert.Equal(voyage.Map.Edges, test.Map.Edges);
        }
    }

    [Theory]
    [InlineData(NodeType.HardFight)]
    [InlineData(NodeType.Event)]
    public void AMapWithoutTheTypeGetsOneNodeOfItOnTheMiddleRow(NodeType type)
    {
        var without = Seeds.Where(seed => RunFactory.NewRun(Db, "test", seed).Map!.Nodes.All(n => n.Type != type)).ToList();
        Assert.NotEmpty(without);
        foreach (ulong seed in without)
        {
            var voyage = RunFactory.NewRun(Db, "test", seed).Map!;
            var run = Start(seed, type);
            var node = run.CurrentNode!;
            Assert.Equal(Rules.MapRows / 2, node.Row);
            Assert.Equal(voyage.Nodes.Select(n => n.Id == node.Id ? n with { Type = type } : n), run.Map!.Nodes);
            Assert.Equal(voyage.Edges, run.Map.Edges);

            // A dock and a shipyard are on every map, so the changed node is neither while the row has another.
            var row = voyage.Nodes.Where(n => n.Row == node.Row).ToList();
            Assert.True(!Rules.MapRequiredTypes.Contains(voyage.Node(node.Id).Type) || row.All(n => Rules.MapRequiredTypes.Contains(n.Type)));
        }
    }

    [Fact]
    public void ANamedEventIsTheOneAtTheNode()
    {
        foreach (var ev in Db.Events)
        {
            var run = Start(5, NodeType.Event, ev.Id);
            Assert.Equal(ev.Id, run.Event!.EventId);
            Assert.Equal([ev.Id], run.SeenEvents);
            Assert.Equal(ev, new EventEngine(Db).Current(run));
        }
    }

    [Fact]
    public void AnEventNeedsAnEventNodeAndAKnownId()
    {
        Assert.Throws<InvalidOperationException>(() => Start(5, NodeType.Dock, "cache"));
        Assert.Throws<InvalidOperationException>(() => Start(5, NodeType.Event, "nope"));
    }

    [Fact]
    public void ChoosingANodeStillChecksThatItCanBeReached()
    {
        var run = Start(5, NodeType.Dock);
        new DockEngine(Db).Leave(run);
        int far = run.Map!.Boss.Id;
        string before = SaveSerializer.Serialize(run);
        Assert.Throws<InvalidOperationException>(() => new MapEngine(Db).Choose(run, far));
        Assert.Equal(before, SaveSerializer.Serialize(run));
    }
}
