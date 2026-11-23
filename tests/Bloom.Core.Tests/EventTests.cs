using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Map;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class EventTests
{
    private readonly EventEngine _events = new(Db);
    private readonly MapEngine _map = new(Db);

    private static string Json(RunState run) => SaveSerializer.Serialize(run);

    /// <summary>Test setup: the ship at an event on node 1 of the tiny map, swapped for <paramref name="eventId"/> when given.</summary>
    private RunState AtEvent(string? eventId = null, ulong seed = 1)
    {
        var run = UseTinyMap(NewRun(seed), middle: NodeType.Event);
        run.Path.Add(0);
        _map.Choose(run, 1);
        if (eventId is not null)
        {
            run.Event = new EventVisit(eventId);
        }

        return run;
    }

    [Fact]
    public void ChoosingAnEventNodeRollsOneOfTheSectorsEvents()
    {
        var run = AtEvent();
        Assert.Equal(RunPhase.Event, run.Phase);
        Assert.Null(run.Event!.Choice);
        Assert.Equal([run.Event.EventId], run.SeenEvents);
        Assert.Same(Db.Event(run.Event.EventId), _events.Current(run));
        Assert.Empty(_map.Reachable(run));
    }

    [Fact]
    public void AnEventRepeatsOnlyOnceTheSectorHasNoOtherLeft()
    {
        var run = NewRun();
        var rolled = Enumerable.Range(0, Db.Events.Count).Select(_ => EventEngine.Roll(Db, run).EventId).ToList();
        Assert.Equal(Db.Events.Select(e => e.Id).Order(), rolled.Order());

        // Once all of them were met, any can come up again.
        Assert.Contains(EventEngine.Roll(Db, run).EventId, Db.Events.Select(e => e.Id));
        Assert.Equal(Db.Events.Count + 1, run.SeenEvents.Count);
    }

    [Fact]
    public void EveryEventGetsRolled()
    {
        var seen = new HashSet<string>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            seen.Add(AtEvent(seed: seed).Event!.EventId);
        }

        Assert.Equal(Db.Events.Select(e => e.Id).Order(), seen.Order());
    }

    [Fact]
    public void AChoiceAppliesItsResultAndContinuingReturnsToTheMap()
    {
        var run = AtEvent("cache");
        _events.Choose(run, 0);
        Assert.Equal(20, run.Salvage);
        Assert.Equal(RunPhase.Event, run.Phase);
        Assert.Equal(0, run.Event!.Choice);
        Assert.Equal(0, run.Event.Result);
        var changes = run.Event.Changes!;
        Assert.Equal((20, 0, 0), (changes.Salvage, changes.Hull, changes.MaxHull));
        Assert.Empty(changes.Gained);
        Assert.Empty(changes.Upgraded);
        Assert.Equal("Scrap.", _events.Result(run)!.Text);

        _events.Continue(run);
        Assert.Null(run.Event);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal([2], _map.Reachable(run).Select(n => n.Id));
    }

    [Fact]
    public void AChoiceWithNoOutcomeChangesNothing()
    {
        var run = AtEvent("cache");
        var before = (run.Salvage, run.Hull, run.MaxHull, run.Cargo.Pieces.Count);
        _events.Choose(run, 2);
        Assert.Equal(before, (run.Salvage, run.Hull, run.MaxHull, run.Cargo.Pieces.Count));
        Assert.Equal((0, 0, 0), (run.Event!.Changes!.Salvage, run.Event.Changes.Hull, run.Event.Changes.MaxHull));
    }

    [Fact]
    public void ACostIsPaidAndANamedPieceGoesToTheCargoHold()
    {
        var run = AtEvent("cache");
        run.Salvage = 40;
        _events.Choose(run, 1);
        Assert.Equal(10, run.Salvage);
        var spear = Assert.Single(run.Cargo.Pieces);
        Assert.Equal("spear", spear.DefId);
        Assert.Equal(-30, run.Event!.Changes!.Salvage);
        Assert.Equal([spear.Id], run.Event.Changes.Gained);
    }

    /// <summary>The owner, while the events were sketched: a choice the ship cannot pay for is shown but cannot be taken.</summary>
    [Fact]
    public void AChoiceThatCostsMoreThanTheShipHasIsUnaffordableAndRefused()
    {
        var run = AtEvent("cache");
        run.Salvage = 29;
        var ev = _events.Current(run)!;
        Assert.Equal(ChoiceState.Unaffordable, _events.StateOf(run, ev.Choices[1]));
        Assert.Equal(ChoiceState.Open, _events.StateOf(run, ev.Choices[0]));
        Assert.False(_events.CanChoose(run, 1));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _events.Choose(run, 1));
        Assert.Equal(before, Json(run));
    }

    /// <summary>The owner, while the events were sketched: a choice that needs a tag the ship lacks is hidden, as FTL's blue options are.</summary>
    [Fact]
    public void AChoiceThatNeedsATagTheShipLacksIsHiddenAndRefused()
    {
        var run = AtEvent("signal");
        var answer = _events.Current(run)!.Choices[0];
        Assert.Equal(ChoiceState.Hidden, _events.StateOf(run, answer));
        Assert.False(_events.CanChoose(run, 0));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _events.Choose(run, 0));
        Assert.Equal(before, Json(run));

        Fit(run, "drone", 2, 0);
        Assert.Equal(ChoiceState.Open, _events.StateOf(run, answer));
        _events.Choose(run, 0);
        Assert.Equal(10, run.Salvage);
    }

    [Fact]
    public void APieceInTheCargoHoldDoesNotOpenATaggedChoice()
    {
        var run = AtEvent("signal");
        run.Cargo.Add(run.NewPiece("drone"));
        Assert.Equal(ChoiceState.Hidden, _events.StateOf(run, _events.Current(run)!.Choices[0]));
    }

    [Fact]
    public void EventDamageNeverTakesTheHullBelowOne()
    {
        var run = AtEvent("storm");
        _events.Choose(run, 0);
        Assert.Equal(1, run.Hull);
        Assert.Equal(RunResult.InProgress, run.Result);
        Assert.Equal(-59, run.Event!.Changes!.Hull);
    }

    [Fact]
    public void AnEventRepairsTheHullUpToItsMaximum()
    {
        var run = AtEvent("storm");
        run.Hull = 50;
        _events.Choose(run, 1);
        Assert.Equal(60, run.Hull);
        Assert.Equal(10, run.Event!.Changes!.Hull);
    }

    [Fact]
    public void RaisingTheMaximumHullRaisesTheHullWithIt()
    {
        var run = AtEvent("graft");
        run.Hull = 40;
        _events.Choose(run, 0);
        Assert.Equal((46, 66), (run.Hull, run.MaxHull));
        Assert.Equal((6, 6), (run.Event!.Changes!.Hull, run.Event.Changes.MaxHull));
    }

    [Theory]
    [InlineData(60, 50)] // a full hull is cut to the new maximum
    [InlineData(30, 30)] // a damaged one keeps its hull points
    public void LoweringTheMaximumHullCapsTheHull(int hull, int expected)
    {
        var run = AtEvent("graft");
        run.Hull = hull;
        _events.Choose(run, 1);
        Assert.Equal((expected, 50), (run.Hull, run.MaxHull));
        Assert.Equal(-10, run.Event!.Changes!.MaxHull);
    }

    [Fact]
    public void APieceCanBeGivenByRarity()
    {
        var run = AtEvent("gift");
        _events.Choose(run, 0);
        Assert.Equal("drone", Assert.Single(run.Cargo.Pieces).DefId);
    }

    [Fact]
    public void APieceCanBeGivenByKind()
    {
        var crew = new HashSet<string>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var run = AtEvent("gift", seed);
            _events.Choose(run, 1);
            var piece = Assert.Single(run.Cargo.Pieces);
            Assert.Equal(PieceKind.Crew, Db.Piece(piece.DefId).Kind);
            crew.Add(piece.DefId);
        }

        Assert.True(crew.Count > 1, "every seed gave the same crew member");
    }

    [Fact]
    public void AnUpgradeUpgradesAPieceThatCanStillBeUpgraded()
    {
        var run = AtEvent("gift");
        int bridge = BridgeId(run);
        _events.Choose(run, 2);
        Assert.True(run.Ship.Find(bridge)!.Piece.Upgraded);
        Assert.Equal([bridge], run.Event!.Changes!.Upgraded);
    }

    [Fact]
    public void AnUpgradeDoesNothingWhenNoPieceCanBeUpgraded()
    {
        var run = AtEvent("gift");
        PieceUpgrades.Upgrade(Db, run, BridgeId(run));
        Assert.Empty(PieceUpgrades.Upgradable(Db, run));
        _events.Choose(run, 2);
        Assert.Empty(run.Event!.Changes!.Upgraded);
        Assert.Equal(RunPhase.Event, run.Phase);
    }

    [Fact]
    public void AHullSectionIsOfferedOnceTheEventIsOver()
    {
        var run = AtEvent("sprout");
        _events.Choose(run, 0);
        Assert.Equal(RunPhase.Event, run.Phase);
        _events.Continue(run);
        Assert.Equal(RunPhase.Shipyard, run.Phase);
        Assert.Equal(Rules.ShipyardChoices, run.Shipyard!.Sections.Count);

        var yard = new ShipyardEngine(Db);
        var (origin, rotation) = ShipyardEngine.Placements(run.Ship.Grid, yard.Offered(run, 0)!.Shape).First();
        yard.Attach(run, 0, origin, rotation);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal([2], _map.Reachable(run).Select(n => n.Id));
    }

    [Fact]
    public void AFightFromAnEventIsPreparedFoughtAndRewardedBeforeTheMap()
    {
        var run = AtEvent("gamble");
        _events.Choose(run, 1);
        _events.Continue(run);
        Assert.Equal(RunPhase.Prepare, run.Phase);
        Assert.Equal("dummy", run.Encounter!.EnemyId);
        Assert.Contains("dummy", run.EnemiesMet);
        Assert.True(RefitEngine.CanRefit(run));
        Assert.Empty(_map.Reachable(run));

        var combat = new CombatEngine(Db);
        var fight = new RefitEngine(Db, combat).StartFight(run);
        fight.Enemy.Hull = 1;
        combat.PlayCard(run, Take(fight, "ram"));
        Assert.Equal(RunPhase.Reward, run.Phase);
        Assert.False(run.Reward!.Section);

        new RewardEngine(Db).Skip(run);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal([2], _map.Reachable(run).Select(n => n.Id));
    }

    [Fact]
    public void AGambleRollsItsResultsByWeight()
    {
        var results = new List<int>();
        for (ulong seed = 1; seed <= 80; seed++)
        {
            var run = AtEvent("gamble", seed);
            _events.Choose(run, 0);
            results.Add(run.Event!.Result!.Value);
            if (run.Event.Result == 0)
            {
                Assert.Equal(50, run.Salvage);
            }
            else
            {
                _events.Continue(run);
                Assert.Equal("brute", run.Encounter!.EnemyId);
            }
        }

        int lost = results.Count(r => r == 1);
        Assert.InRange(lost, 8, 32); // 1 in 4 of 80 is 20
    }

    [Fact]
    public void AHardEnemyFromAnEventPaysAHardFightsReward()
    {
        var run = AtEvent("gamble");
        run.Event = run.Event! with { Choice = 0, Result = 1, Changes = new EventChanges(0, 0, 0, [], []) };
        _events.Continue(run);
        var combat = new CombatEngine(Db);
        var fight = new RefitEngine(Db, combat).StartFight(run);
        fight.Enemy.Hull = 1;
        combat.PlayCard(run, Take(fight, "ram"));
        Assert.True(run.Reward!.Section);
        Assert.Equal(Rarity.Rare, Db.Piece(run.Reward.Pieces[0]).Rarity);
    }

    [Fact]
    public void ContinuingBeforeChoosingIsRefused()
    {
        var run = AtEvent("cache");
        string before = Json(run);
        Assert.Null(_events.Result(run));
        Assert.Throws<InvalidOperationException>(() => _events.Continue(run));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void ChoosingTwiceIsRefused()
    {
        var run = AtEvent("cache");
        _events.Choose(run, 0);
        Assert.False(_events.CanChoose(run, 2));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _events.Choose(run, 2));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void AChoiceThatDoesNotExistIsRefused()
    {
        var run = AtEvent("cache");
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _events.Choose(run, 3));
        Assert.Throws<InvalidOperationException>(() => _events.Choose(run, -1));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void NothingElseCanHappenDuringAnEvent()
    {
        var run = AtEvent("cache");
        var held = run.NewPiece("lance");
        run.Cargo.Add(held);
        string before = Json(run);
        Assert.False(RefitEngine.CanRefit(run));
        var e = Assert.Throws<InvalidOperationException>(() => new RefitEngine(Db).FitFromCargo(run, held.Id, new Cell(1, 1), Rotation.R0));
        Assert.Equal("Finish the event first.", e.Message);
        Assert.Throws<InvalidOperationException>(() => _map.Choose(run, 2));
        var fight = Assert.Throws<InvalidOperationException>(() => new CombatEngine(Db).StartFight(run, "dummy"));
        Assert.Equal("Finish the event first.", fight.Message);
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void ChoosingOrContinuingAwayFromAnEventIsRefused()
    {
        var run = NewRun();
        Assert.Null(_events.Current(run));
        Assert.False(_events.CanChoose(run, 0));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _events.Choose(run, 0));
        Assert.Throws<InvalidOperationException>(() => _events.Continue(run));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void AnEventSurvivesASaveBeforeAndAfterTheChoice()
    {
        var run = AtEvent("cache");
        run.Salvage = 40;
        var restored = SaveSerializer.Deserialize(Json(run));
        Assert.Equal(RunPhase.Event, restored.Phase);
        Assert.Equal(run.Event, restored.Event);
        Assert.Equal(run.SeenEvents, restored.SeenEvents);

        _events.Choose(restored, 1);
        var again = SaveSerializer.Deserialize(Json(restored));
        Assert.Equal(Json(restored), Json(again));
        Assert.Equal(1, again.Event!.Choice);
        Assert.Equal(restored.Event!.Changes!.Gained, again.Event.Changes!.Gained);
        Assert.Equal("Buy the spear", _events.Current(again)!.Choices[again.Event.Choice!.Value].Label);

        _events.Continue(again);
        Assert.Equal(RunPhase.Map, again.Phase);
    }

    [Fact]
    public void OutcomesAreReadFromJson()
    {
        var gamble = Db.Event("gamble").Choices[0];
        Assert.Equal([3, 1], gamble.Results.Select(r => r.Weight));
        Assert.Equal([new SalvageOutcome(50)], gamble.Results[0].AllOutcomes);
        Assert.Equal([new FightOutcome("brute")], gamble.Results[1].AllOutcomes);
        Assert.Equal([new PieceOutcome(Rarity: Rarity.Uncommon)], Db.Event("gift").Choices[0].Results[0].AllOutcomes);
        Assert.Equal([new PieceOutcome(Kind: PieceKind.Crew)], Db.Event("gift").Choices[1].Results[0].AllOutcomes);
        Assert.IsType<UpgradeOutcome>(Db.Event("gift").Choices[2].Results[0].AllOutcomes[0]);
        Assert.IsType<SectionOutcome>(Db.Event("sprout").Choices[0].Results[0].AllOutcomes[0]);
        Assert.Equal(Tag.Drone, Db.Event("signal").Choices[0].NeedsTag);
        Assert.Equal(30, Db.Event("cache").Choices[1].Cost);
        Assert.Empty(Db.Event("cache").Choices[2].Results[0].AllOutcomes);
    }
}
