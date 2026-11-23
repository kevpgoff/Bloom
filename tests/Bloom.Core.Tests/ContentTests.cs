using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Core.Run;

namespace Bloom.Core.Tests;

public class ContentTests
{
    private static readonly PieceDef Bridge = new("bridge", "Bridge", PieceKind.Equipment, [Tag.Core], Rarity.Common, Shape.Parse(["##"]), [new CardGrant("ram", 2)]);
    private static readonly PieceDef Lance = new("lance", "Lance", PieceKind.Equipment, [Tag.Weapon], Rarity.Common, Shape.Parse(["###"]), [new CardGrant("ram", 1)]);
    private static readonly CardDef Ram = new("ram", "Ram", 1, [new DamageEffect(5)]);
    private static readonly EnemyDef Dummy = new("dummy", "Dummy", 10, [new Intent(IntentKind.Attack, 1)], EnemyPool.Early);

    /// <summary>One enemy for each pool, which every sector needs.</summary>
    private static readonly EnemyDef[] Enemies =
    [
        Dummy,
        Dummy with { Id = "late", Pool = EnemyPool.Late },
        Dummy with { Id = "hard", Pool = EnemyPool.Hard },
        Dummy with { Id = "boss", Pool = EnemyPool.Boss },
    ];
    private static readonly ShipClassDef Ship = new("ship", "Ship", 60, Shape.Parse(["###", "###"]), new Cell(0, 0), [new StartingPiece("bridge", new Cell(0, 0))]);

    private static ContentDb Create(PieceDef[]? pieces = null, CardDef[]? cards = null, EnemyDef[]? enemies = null, ShipClassDef[]? ships = null, HullSectionDef[]? sections = null, EventDef[]? events = null) =>
        ContentDb.Create(pieces ?? [Bridge, Lance], cards ?? [Ram], enemies ?? Enemies, ships ?? [Ship], sections ?? [.. TestContent.Db.Sections], events ?? [.. TestContent.PlainEvents]);

    private static void AssertInvalid(string expectedError, PieceDef[]? pieces = null, CardDef[]? cards = null, EnemyDef[]? enemies = null, ShipClassDef[]? ships = null, HullSectionDef[]? sections = null, EventDef[]? events = null)
    {
        var e = Assert.Throws<ContentException>(() => Create(pieces, cards, enemies, ships, sections, events));
        Assert.Contains(e.Errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidContentLoads() => Create();

    [Fact]
    public void AnEnemyWithoutAPoolIsReported() =>
        AssertInvalid("enemy 'late': needs a pool", enemies: [.. Enemies, Dummy with { Id = "late", Pool = null }]);

    [Fact]
    public void AnEmptyPoolIsReported() =>
        AssertInvalid("Sector 1 has no enemy in the Hard pool", enemies: [Enemies[0], Enemies[1], Enemies[3]]);

    [Fact]
    public void AnEnemyInASectorPastTheRunIsReported() =>
        AssertInvalid($"sector {Rules.SectorsPerRun + 1} is outside", enemies: [.. Enemies, Dummy with { Id = "far", Sector = Rules.SectorsPerRun + 1 }]);

    [Fact]
    public void DuplicateIdsAreReported() =>
        AssertInvalid("Duplicate card id 'ram'", cards: [Ram, Ram]);

    [Fact]
    public void AnUnknownCardIsReported() =>
        AssertInvalid("unknown card 'nope'", pieces: [Bridge, Lance with { Cards = [new CardGrant("nope", 1)] }]);

    [Fact]
    public void APieceAddingTooManyCardsIsReported() =>
        AssertInvalid("adds 5 cards", pieces: [Bridge, Lance with { Cards = [new CardGrant("ram", 5)] }]);

    [Fact]
    public void CrewWithTooManyCellsIsReported() =>
        AssertInvalid("Crew must cover 1 to 3 cells", pieces: [Bridge, Lance with { Kind = PieceKind.Crew, Shape = Shape.Parse(["##", "##"]) }]);

    [Fact]
    public void ADisconnectedShapeIsReported() =>
        AssertInvalid("one group of cells", pieces: [Bridge, Lance with { Shape = Shape.Parse(["#.#"]) }]);

    [Fact]
    public void ABonusEffectOnACardIsReported() =>
        AssertInvalid("can only be used in an adjacency bonus", cards: [Ram with { Effects = [new DamageBonusEffect(2)] }]);

    [Fact]
    public void AHeavyAttackWithoutALaneIsReported() =>
        AssertInvalid("must name a lane", enemies: [Dummy with { Pattern = [new Intent(IntentKind.HeavyAttack, 5)] }]);

    [Fact]
    public void ALaneOutsideTheGridIsReported() =>
        AssertInvalid("lane 6 is outside", enemies: [Dummy with { Pattern = [new Intent(IntentKind.HeavyAttack, 5, 6)] }]);

    [Fact]
    public void AShipWithoutABridgeIsReported() =>
        AssertInvalid("exactly one bridge", ships: [Ship with { StartingPieces = [new StartingPiece("lance", new Cell(0, 0))] }]);

    [Fact]
    public void StartingPiecesThatDoNotFitAreReported() =>
        AssertInvalid("OffHull", ships: [Ship with { StartingPieces = [new StartingPiece("bridge", new Cell(2, 0))] }]);

    [Fact]
    public void ASectionOutsideThreeToFiveCellsIsReported()
    {
        AssertInvalid("hull section 'stub': a section covers 3 to 5 cells, but the shape covers 2",
            sections: [.. TestContent.Db.Sections, new HullSectionDef("stub", "Stub", Shape.Parse(["##"]))]);
        AssertInvalid("hull section 'slab': a section covers 3 to 5 cells, but the shape covers 6",
            sections: [.. TestContent.Db.Sections, new HullSectionDef("slab", "Slab", Shape.Parse(["###", "###"]))]);
    }

    [Fact]
    public void ASectionInPiecesIsReported() =>
        AssertInvalid("hull section 'split': the shape must be one group of cells joined by edges",
            sections: [.. TestContent.Db.Sections, new HullSectionDef("split", "Split", Shape.Parse(["##.#"]))]);

    [Fact]
    public void ASectionWithoutANameIsReported() =>
        AssertInvalid("hull section 'rib2': missing name",
            sections: [.. TestContent.Db.Sections, new HullSectionDef("rib2", " ", Shape.Parse(["###"]))]);

    [Fact]
    public void TooFewSectionsForAShipyardAreReported() =>
        AssertInvalid("There are 2 hull sections, but a shipyard offers 3.", sections: [.. TestContent.Db.Sections.Take(2)]);

    [Fact]
    public void ADuplicateSectionIdIsReported() =>
        AssertInvalid("Duplicate hull section id 'rib'", sections: [.. TestContent.Db.Sections, TestContent.Db.Sections[0]]);

    private static readonly EventChoice Leave = new("Leave", [new EventResult("Gone.")]);

    /// <summary>An event with the given first choice and an open "Leave" after it.</summary>
    private static EventDef EventWith(params EventOutcome[] outcomes) =>
        new("ev", "Ev", "Text.", [new EventChoice("Do", [new EventResult("Done.", outcomes)]), Leave]);

    [Fact]
    public void AnEventWithOneChoiceIsReported() =>
        AssertInvalid("event 'ev': an event has 2 to 4 choices, but this one has 1", events: [new EventDef("ev", "Ev", "Text.", [Leave])]);

    [Fact]
    public void AnEventWithFiveChoicesIsReported() =>
        AssertInvalid("but this one has 5", events: [new EventDef("ev", "Ev", "Text.", [Leave, Leave, Leave, Leave, Leave])]);

    [Fact]
    public void AnEventWithNoOpenChoiceIsReported() =>
        AssertInvalid("event 'ev': needs a choice with no cost and no tag", events: [new EventDef("ev", "Ev", "Text.", [
            Leave with { Cost = 10 },
            Leave with { NeedsTag = Tag.Drone }])]);

    [Fact]
    public void AnEventWithoutTextIsReported() =>
        AssertInvalid("event 'ev': missing text", events: [EventWith() with { Text = "" }]);

    [Fact]
    public void AChoiceWithoutResultsIsReported() =>
        AssertInvalid("event 'ev', choice 1: needs at least one result", events: [new EventDef("ev", "Ev", "Text.", [Leave with { Results = [] }, Leave])]);

    [Fact]
    public void AResultWithoutWeightIsReported() =>
        AssertInvalid("a result's weight must be at least 1", events: [new EventDef("ev", "Ev", "Text.", [new EventChoice("Do", [new EventResult("Done.", Weight: 0)]), Leave])]);

    [Fact]
    public void AnOutcomeOfZeroIsReported() =>
        AssertInvalid("'HullOutcome' needs an amount other than 0", events: [EventWith(new HullOutcome(0))]);

    [Fact]
    public void AnEventGivingAnUnknownPieceIsReported() =>
        AssertInvalid("event 'ev', choice 1: unknown piece 'nope'", events: [EventWith(new PieceOutcome("nope"))]);

    [Fact]
    public void AnEventGivingTheBridgeIsReported() =>
        AssertInvalid("an event cannot give the bridge", events: [EventWith(new PieceOutcome("bridge"))]);

    [Fact]
    public void APieceFilterThatMatchesNothingIsReported() =>
        AssertInvalid("no piece matches rarity Rare and kind any", events: [EventWith(new PieceOutcome(Rarity: Rarity.Rare))]);

    [Fact]
    public void AnEventFightingAnUnknownEnemyIsReported() =>
        AssertInvalid("unknown enemy 'nope'", events: [EventWith(new FightOutcome("nope"))]);

    [Fact]
    public void AnEventFightingABossIsReported() =>
        AssertInvalid("enemy 'boss' is a boss", events: [EventWith(new FightOutcome("boss"))]);

    [Fact]
    public void AResultWithTwoFightsIsReported() =>
        AssertInvalid("a result can start only one fight", events: [EventWith(new FightOutcome("dummy"), new FightOutcome("late"))]);

    [Fact]
    public void AResultWithAFightAndASectionIsReported() =>
        AssertInvalid("cannot both start a fight and give a hull section", events: [EventWith(new FightOutcome("dummy"), new SectionOutcome())]);

    [Fact]
    public void ASectorWithoutEventsIsReported() =>
        AssertInvalid("Sector 1 has no event.", events: []);

    [Fact]
    public void ADuplicateEventIdIsReported() =>
        AssertInvalid("Duplicate event id 'ev'", events: [EventWith(), EventWith()]);

    [Fact]
    public void AValidEventLoads() =>
        Create(events: [EventWith(new SalvageOutcome(-5), new HullOutcome(3), new MaxHullOutcome(-2), new PieceOutcome("lance"),
            new PieceOutcome(Kind: PieceKind.Equipment), new UpgradeOutcome(), new SectionOutcome())]);

    [Fact]
    public void AnOutcomeMustStartWithItsType()
    {
        const string events = """[{ "id": "e", "name": "E", "text": "T", "choices": [{ "label": "A", "results": [{ "text": "R", "outcomes": [{ "amount": 5, "type": "salvage" }] }] }] }]""";
        var e = Assert.Throws<ContentException>(() => ContentDb.FromJson("[]", "[]", "[]", "[]", "[]", events));
        Assert.StartsWith("events.json:", Assert.Single(e.Errors));
    }

    [Fact]
    public void BrokenJsonNamesTheFile()
    {
        var e = Assert.Throws<ContentException>(() => ContentDb.FromJson("[", "[]", "[]", "[]", "[]", "[]"));
        Assert.StartsWith("pieces.json:", Assert.Single(e.Errors));
    }

    [Fact]
    public void AnEffectMustStartWithItsType()
    {
        // .NET 8 reads the "type" discriminator only when it comes first. If this starts passing after a
        // runtime upgrade, the rule in game/data/README.md can be dropped.
        const string cards = """[{ "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "amount": 5, "type": "damage" }] }]""";
        Assert.Throws<ContentException>(() => ContentDb.FromJson("[]", cards, "[]", "[]", "[]", "[]"));
    }
}

public class ShippedContentTests
{
    [Fact]
    public void TheShippedContentLoadsAndPassesValidation()
    {
        var db = TestContent.LoadShipped();
        Assert.Equal(16, db.Pieces.Count); // the spec's 7, the placeholder Tinker and 8 placeholder rewards
        Assert.Equal(17, db.Cards.Count);  // the spec's 8, the placeholder Patch and 8 for the rewards
        Assert.Equal(12, db.Enemies.Count);
        Assert.Single(db.Ships);
        Assert.Equal(10, db.Sections.Count);
        Assert.Equal(20, db.Events.Count); // 10 sketched with the owner and 10 from the owner's writer prompt, 2026-11-23
    }

    /// <summary>
    /// Every choice of every shipped event can be taken on the starting ship and continued past, with salvage to
    /// pay for it. A choice that needs a tag the seedling lacks is played with a piece of that tag fitted on new hull.
    /// </summary>
    [Fact]
    public void EveryShippedEventChoiceCanBePlayed()
    {
        var db = TestContent.LoadShipped();
        var events = new EventEngine(db);
        foreach (var ev in db.Events)
        {
            for (int i = 0; i < ev.Choices.Count; i++)
            {
                for (ulong seed = 1; seed <= 4; seed++)
                {
                    var run = RunFactory.NewRun(db, "seedling", seed);
                    run.Salvage = 500;
                    if (ev.Choices[i].NeedsTag is { } tag)
                    {
                        FitPieceWithTag(db, run, tag);
                    }

                    run.Event = new EventVisit(ev.Id);
                    Assert.Equal(ChoiceState.Open, events.StateOf(run, ev.Choices[i]));
                    events.Choose(run, i);
                    events.Continue(run);
                    Assert.Contains(run.Phase, new[] { RunPhase.Map, RunPhase.Shipyard, RunPhase.Prepare });
                }
            }
        }
    }

    /// <summary>Test setup: grows two rows of hull behind the ship and fits the first piece with <paramref name="tag"/> there.</summary>
    private static void FitPieceWithTag(ContentDb db, RunState run, Tag tag)
    {
        run.Ship.Grid.Attach([.. Enumerable.Range(0, Rules.GridColumns).SelectMany(x => new[] { new Cell(x, 6), new Cell(x, 7) })]);
        var piece = run.NewPiece(db.Pieces.First(p => !p.IsBridge && p.Tags.Contains(tag)).Id);
        run.Cargo.Add(piece);
        var refit = new RefitEngine(db);
        var spot = Enum.GetValues<Rotation>()
            .SelectMany(r => Enumerable.Range(0, Rules.GridColumns).Select(x => (Origin: new Cell(x, 6), Rotation: r)))
            .First(s => refit.PreviewPlacement(run, piece.Id, s.Origin, s.Rotation).Error == FitError.None);
        refit.FitFromCargo(run, piece.Id, spot.Origin, spot.Rotation);
    }

    /// <summary>Every shipped hull section can be offered on the starting ship, so the first shipyard offers any of them.</summary>
    [Fact]
    public void EverySectionFitsTheStartingShip()
    {
        var db = TestContent.LoadShipped();
        foreach (var ship in db.Ships)
        {
            var run = RunFactory.NewRun(db, ship.Id, seed: 1);
            foreach (var section in db.Sections)
            {
                Assert.True(ShipyardEngine.Fits(run.Ship.Grid, section.Shape), $"'{section.Id}' cannot attach to the starting {ship.Id}.");
            }
        }
    }

    /// <summary>The first sector (owner, 2026-11-22): 8 normal enemies, 3 hard ones and a boss.</summary>
    [Fact]
    public void TheFirstSectorHasEnemiesInEveryPool()
    {
        var db = TestContent.LoadShipped();
        int Count(EnemyPool pool) => db.Enemies.Count(e => e.Pool == pool && e.Sector == 1);
        Assert.Equal(4, Count(EnemyPool.Early));
        Assert.Equal(4, Count(EnemyPool.Late));
        Assert.Equal(3, Count(EnemyPool.Hard));
        Assert.Equal(1, Count(EnemyPool.Boss));
    }

    [Fact]
    public void ARunStartsOnTheMapOfTheFirstSector()
    {
        var db = TestContent.LoadShipped();
        var run = RunFactory.NewRun(db, "seedling", seed: 1);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal(1, run.Sector);
        Assert.NotEmpty(new MapEngine(db).Reachable(run));
    }

    /// <summary>
    /// The seedling has no hull in lane 0. Some enemies of every fight pool but the boss aim there anyway: they
    /// hit lane 1 until a section opens lane 0, then lane 0 (owner, 2026-11-23).
    /// </summary>
    [Fact]
    public void SomeHeavyAttacksAimAtTheLeftLaneTheSeedlingHasNotGrownInto()
    {
        var db = TestContent.LoadShipped();
        var run = RunFactory.NewRun(db, "seedling", seed: 1);
        Assert.DoesNotContain(run.Ship.Grid.Cells, c => c.X == 0);
        Assert.Equal(1, run.Ship.Grid.LaneHit(0));
        var pools = db.Enemies
            .Where(e => e.Pattern.Any(i => i.Kind == IntentKind.HeavyAttack && i.Lane == 0))
            .Select(e => e.Pool!.Value)
            .Distinct()
            .Order();
        Assert.Equal([EnemyPool.Early, EnemyPool.Late, EnemyPool.Hard], pools);
    }

    [Fact]
    public void TheShippedContentOffersThreePieces()
    {
        var db = TestContent.LoadShipped();
        var offer = new RewardEngine(db).Roll(RunFactory.NewRun(db, "seedling", seed: 1));
        Assert.Equal(3, offer!.Pieces.Distinct().Count());
        Assert.DoesNotContain("bridge", offer.Pieces);
    }

    /// <summary>
    /// The placeholder seedling carries every spec piece so that their effects show up in the game: a
    /// 5x5 hull instead of the spec's "about 20 cells" (owner ruling, 2026-11-21).
    /// </summary>
    [Fact]
    public void TheStartingShipCarriesEveryPiece()
    {
        var db = TestContent.LoadShipped();
        var run = RunFactory.NewRun(db, "seedling", seed: 1);
        Assert.Equal(25, run.Ship.Grid.CellCount);
        var startingPieces = db.Pieces.Where(p => !p.Placeholder || p.Id == "tinker").Select(p => p.Id);
        Assert.Equal(startingPieces.Order(), run.Ship.Fitted.Select(f => f.Piece.DefId).Order());
        Assert.Equal(14, DeckBuilder.Build(run.Ship, db).Count);
        Assert.Equal(60, run.Hull);
    }

    [Fact]
    public void BothAdjacencyBonusesAreActiveOnTheStartingShip()
    {
        var db = TestContent.LoadShipped();
        var run = RunFactory.NewRun(db, "seedling", seed: 1);
        string DefOf(int pieceId) => run.Ship.Find(pieceId)!.Piece.DefId;
        var bonuses = new ShipFitter(db).ActiveBonuses(run.Ship, [])
            .Select(b => $"{DefOf(b.GiverId)}->{DefOf(b.ReceiverId)}")
            .Order();
        Assert.Equal(["lens_grinder->light_lance", "spore_singer->moss_reactor"], bonuses);
    }
}
