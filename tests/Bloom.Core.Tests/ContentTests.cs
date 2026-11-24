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
    private static readonly ShipClassDef Ship = new("ship", "Ship", 60, Shape.Parse(["###", "###"]), new Cell(0, 0), [new ShipLoadout("a", "A", [new StartingPiece("bridge", new Cell(0, 0))])]);

    /// <summary>The test ship with one loadout of these starting pieces.</summary>
    private static ShipClassDef WithPieces(params StartingPiece[] pieces) =>
        Ship with { Loadouts = [new ShipLoadout("a", "A", pieces)] };

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

    private static readonly EnemyPartDef Arm = new("Arm", 10, [new Intent(IntentKind.Attack, 2)]);

    private static EnemyDef BossWith(params EnemyPartDef[] parts) => Enemies[3] with { Parts = parts };

    [Fact]
    public void ABossWithPartsLoads() => Create(enemies: [.. Enemies[..3], BossWith(Arm, Arm with { Name = "Shell" })]);

    [Fact]
    public void TooManyPartsAreReported() =>
        AssertInvalid($"has 4 parts, but at most {Rules.MaxEnemyParts}", enemies: [.. Enemies[..3], BossWith(Arm, Arm with { Name = "B" }, Arm with { Name = "C" }, Arm with { Name = "D" })]);

    [Fact]
    public void APartWithoutANameIsReported() =>
        AssertInvalid("enemy 'boss', part 1: missing name", enemies: [.. Enemies[..3], BossWith(Arm, Arm with { Name = " " })]);

    [Fact]
    public void TwoPartsWithOneNameAreReported() =>
        AssertInvalid("enemy 'boss', part 1: another part is already named 'Arm'", enemies: [.. Enemies[..3], BossWith(Arm, Arm)]);

    [Fact]
    public void APartWithoutHullIsReported() =>
        AssertInvalid("enemy 'boss', part 0: hull must be above 0", enemies: [.. Enemies[..3], BossWith(Arm with { Hull = 0 })]);

    [Fact]
    public void APartWithoutAPatternIsReported() =>
        AssertInvalid("enemy 'boss', part 0: needs at least one intent", enemies: [.. Enemies[..3], BossWith(Arm with { Pattern = [] })]);

    [Fact]
    public void APartsHeavyAttackWithoutALaneIsReported() =>
        AssertInvalid("enemy 'boss', part 0: a heavy attack must name a lane", enemies: [.. Enemies[..3], BossWith(Arm with { Pattern = [new Intent(IntentKind.HeavyAttack, 5)] })]);

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
        AssertInvalid("exactly one bridge", ships: [WithPieces(new StartingPiece("lance", new Cell(0, 0)))]);

    [Fact]
    public void StartingPiecesThatDoNotFitAreReported() =>
        AssertInvalid("OffHull", ships: [WithPieces(new StartingPiece("bridge", new Cell(2, 0)))]);

    [Fact]
    public void AShipWithoutALoadoutIsReported() =>
        AssertInvalid("needs at least one loadout", ships: [Ship with { Loadouts = [] }]);

    [Fact]
    public void ARepeatedLoadoutIdIsReported() =>
        AssertInvalid("duplicate loadout id 'a'", ships: [Ship with { Loadouts = [Ship.Loadouts[0], Ship.Loadouts[0]] }]);

    [Theory]
    [InlineData("Big start")]
    [InlineData("b_2")]
    public void ALoadoutIdIsWrittenLikeAFolderName(string id) =>
        AssertInvalid($"loadout '{id}': the id must be lowercase words joined by hyphens", ships: [Ship with { Loadouts = [Ship.Loadouts[0] with { Id = id }] }]);

    [Fact]
    public void ALoadoutWithoutANameIsReported() =>
        AssertInvalid("loadout 'a': missing name", ships: [Ship with { Loadouts = [Ship.Loadouts[0] with { Name = "" }] }]);

    /// <summary>Every loadout is checked, not only the first: a later one whose pieces do not fit is named.</summary>
    [Fact]
    public void ALaterLoadoutThatDoesNotFitIsReported()
    {
        var bad = new ShipLoadout("b", "B", [new StartingPiece("bridge", new Cell(2, 0))]);
        AssertInvalid("ship class 'ship', loadout 'b': ", ships: [Ship with { Loadouts = [Ship.Loadouts[0], bad] }]);
        AssertInvalid("loadout 'b': must start with exactly one bridge", ships: [Ship with { Loadouts = [Ship.Loadouts[0], bad with { StartingPieces = [new StartingPiece("lance", new Cell(0, 0))] }] }]);
    }

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

    [Fact]
    public void AShipRuleIsReadFromJson()
    {
        const string pieces = """[{ "id": "bridge", "name": "Bridge", "kind": "Equipment", "tags": ["Core"], "rarity": "Common", "shape": ["##"], "cards": [{ "card": "ram", "count": 2 }] }]""";
        const string cards = """[{ "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "type": "damage", "amount": 5 }] }]""";
        const string enemies = """
            [{ "id": "a", "name": "A", "pool": "Early", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
             { "id": "b", "name": "B", "pool": "Late", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
             { "id": "c", "name": "C", "pool": "Hard", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
             { "id": "d", "name": "D", "pool": "Boss", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] }]
            """;
        const string ships = """[{ "id": "s", "name": "S", "hull": 60, "hullShape": ["##"], "hullOrigin": { "x": 0, "y": 0 }, "rule": { "type": "keepEnergy" }, "loadouts": [{ "id": "a", "name": "A", "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 0 } }] }] }]""";
        var db = ContentDb.FromJson(pieces, cards, enemies, ships, TestContent.SectionsJson, TestContent.PlainEventsJson);
        Assert.IsType<KeepEnergyRule>(db.Ship("s").Rule);
    }

    [Fact]
    public void AnUnknownShipRuleIsReported()
    {
        const string ships = """[{ "id": "s", "name": "S", "hull": 60, "hullShape": ["##"], "hullOrigin": { "x": 0, "y": 0 }, "rule": { "type": "nope" }, "loadouts": [{ "id": "a", "name": "A", "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 0 } }] }] }]""";
        var e = Assert.Throws<ContentException>(() => ContentDb.FromJson("[]", "[]", "[]", ships, "[]", "[]"));
        Assert.StartsWith("ships.json:", Assert.Single(e.Errors));
    }

    [Fact]
    public void AShipRuleMustStartWithItsType()
    {
        const string ships = """[{ "id": "s", "name": "S", "hull": 60, "hullShape": ["##"], "hullOrigin": { "x": 0, "y": 0 }, "rule": { "extra": 1, "type": "keepEnergy" }, "loadouts": [{ "id": "a", "name": "A", "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 0 } }] }] }]""";
        var e = Assert.Throws<ContentException>(() => ContentDb.FromJson("[]", "[]", "[]", ships, "[]", "[]"));
        Assert.StartsWith("ships.json:", Assert.Single(e.Errors));
    }

    private const string OneBridge = """[{ "id": "bridge", "name": "Bridge", "kind": "Equipment", "tags": ["Core"], "rarity": "Common", "shape": ["##"], "cards": [{ "card": "ram", "count": 2 }] }]""";
    private const string OneCard = """[{ "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "type": "damage", "amount": 5 }] }]""";
    private const string FourEnemies = """
        [{ "id": "a", "name": "A", "pool": "Early", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
         { "id": "b", "name": "B", "pool": "Late", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
         { "id": "c", "name": "C", "pool": "Hard", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
         { "id": "d", "name": "D", "pool": "Boss", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] }]
        """;

    private static string ShipWithSoundSet(string soundSet) =>
        $$"""[{ "id": "s", "name": "S", "hull": 60, "hullShape": ["##"], "hullOrigin": { "x": 0, "y": 0 }, {{soundSet}} "loadouts": [{ "id": "a", "name": "A", "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 0 } }] }] }]""";

    [Fact]
    public void AShipSoundSetIsReadFromJsonAndIsOptional()
    {
        var with = ContentDb.FromJson(OneBridge, OneCard, FourEnemies, ShipWithSoundSet("\"soundSet\": \"warm-circuits\","), TestContent.SectionsJson, TestContent.PlainEventsJson);
        Assert.Equal("warm-circuits", with.Ship("s").SoundSet);

        var without = ContentDb.FromJson(OneBridge, OneCard, FourEnemies, ShipWithSoundSet(""), TestContent.SectionsJson, TestContent.PlainEventsJson);
        Assert.Null(without.Ship("s").SoundSet);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Warm")]
    [InlineData("warm circuits")]
    [InlineData("warm--circuits")]
    [InlineData("-warm")]
    [InlineData("warm/circuits")]
    public void AShipSoundSetMustBeAFolderName(string name)
    {
        string ships = ShipWithSoundSet($"\"soundSet\": \"{name}\",");
        var e = Assert.Throws<ContentException>(() => ContentDb.FromJson(OneBridge, OneCard, FourEnemies, ships, TestContent.SectionsJson, TestContent.PlainEventsJson));
        Assert.Contains("soundSet", Assert.Single(e.Errors), StringComparison.Ordinal);
    }

    /// <summary>Ship classes "a", "b" and so on, each with the unlock written for it, or none for an empty text.</summary>
    private static string ShipsWithUnlocks(params string[] unlocks) =>
        "[" + string.Join(",", unlocks.Select((unlock, i) =>
            $$"""{ "id": "{{(char)('a' + i)}}", "name": "S", "hull": 60, "hullShape": ["##"], "hullOrigin": { "x": 0, "y": 0 }, {{(unlock.Length > 0 ? $"\"unlock\": {unlock}," : "")}} "loadouts": [{ "id": "a", "name": "A", "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 0 } }] }] }""")) + "]";

    private static ContentDb WithUnlocks(params string[] unlocks) =>
        ContentDb.FromJson(OneBridge, OneCard, FourEnemies, ShipsWithUnlocks(unlocks), TestContent.SectionsJson, TestContent.PlainEventsJson);

    [Fact]
    public void AShipUnlockIsReadFromJsonAndIsOptional()
    {
        var db = WithUnlocks("", """{ "type": "winWith", "ship": "a" }""", """{ "type": "winWith", "ship": "b" }""");
        Assert.Null(db.Ship("a").Unlock);
        Assert.Equal(new WinWithUnlock("a"), db.Ship("b").Unlock);
        Assert.Equal(new WinWithUnlock("b"), db.Ship("c").Unlock);
    }

    [Theory]
    [InlineData("""{ "type": "winWith", "ship": "b" }""", "names the ship class itself")]
    [InlineData("""{ "type": "winWith", "ship": "nope" }""", "unknown ship class 'nope'")]
    [InlineData("""{ "type": "winWith" }""", "needs a ship")]
    public void AShipUnlockMustNameAnotherKnownShip(string unlock, string expected)
    {
        var e = Assert.Throws<ContentException>(() => WithUnlocks("", unlock));
        Assert.Contains(expected, Assert.Single(e.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void OneShipMustBeOpenFromTheStart()
    {
        var e = Assert.Throws<ContentException>(() => WithUnlocks("""{ "type": "winWith", "ship": "b" }""", """{ "type": "winWith", "ship": "a" }"""));
        Assert.Contains("No ship class is open from the start", Assert.Single(e.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void UnlocksThatLeadBackToEachOtherAreReported()
    {
        var e = Assert.Throws<ContentException>(() => WithUnlocks("", """{ "type": "winWith", "ship": "c" }""", """{ "type": "winWith", "ship": "b" }"""));
        Assert.Equal(2, e.Errors.Count);
        Assert.All(e.Errors, error => Assert.Contains("can never be unlocked", error, StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownUnlockIsReported()
    {
        var e = Assert.Throws<ContentException>(() => WithUnlocks("", """{ "type": "nope" }"""));
        Assert.StartsWith("ships.json:", Assert.Single(e.Errors));
    }
}

public class ShippedContentTests
{
    /// <summary>
    /// The shipped content loads, and the lint finds nothing in it. These tests check the content's structure,
    /// not its exact counts, so that adding an entry does not break them.
    /// </summary>
    [Fact]
    public void TheShippedContentLoadsAndPassesValidationAndTheLint()
    {
        var db = TestContent.LoadShipped();
        Assert.Empty(ContentLint.Check(db));
    }

    /// <summary>
    /// A tag can be built around only when several pieces carry it, and a choice of an event can ask for any tag.
    /// The numbers are the least that the 2026-11-23 piece set gives, so that a later change cannot thin a tag out
    /// without this test saying so.
    /// </summary>
    [Fact]
    public void EveryTagAndKindHasEnoughPiecesToBuildAround()
    {
        var offered = TestContent.LoadShipped().Pieces.Where(p => !p.IsBridge).ToList();
        foreach (var tag in Enum.GetValues<Tag>().Where(t => t != Tag.Core))
        {
            var carrying = offered.Where(p => p.Tags.Contains(tag)).ToList();
            Assert.True(carrying.Count >= 4, $"Only {carrying.Count} pieces carry {tag}.");
            Assert.True(carrying.Count(p => p.Rarity == Rarity.Common) >= 2, $"Fewer than 2 common pieces carry {tag}.");
            Assert.True(carrying.Any(p => p.Rarity != Rarity.Common), $"No uncommon or rare piece carries {tag}.");
        }

        Assert.True(offered.Count(p => p.Kind == PieceKind.Crew) >= 6);
        Assert.True(offered.Count(p => p.Kind == PieceKind.Equipment) >= 12);
    }

    /// <summary>
    /// Every choice of every shipped event can be taken on every ship and continued past, with salvage to pay for
    /// it. A choice that needs a tag is played with a piece of that tag fitted on new hull.
    /// </summary>
    [Fact]
    public void EveryShippedEventChoiceCanBePlayed()
    {
        var db = TestContent.LoadShipped();
        var events = new EventEngine(db);
        foreach (var ship in db.Ships)
        {
            foreach (var ev in db.Events)
            {
                for (int i = 0; i < ev.Choices.Count; i++)
                {
                    for (ulong seed = 1; seed <= 4; seed++)
                    {
                        var run = RunFactory.NewRun(db, ship.Id, seed);
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
    }

    /// <summary>
    /// Test setup: grows the hull over the whole grid and fits the first piece with <paramref name="tag"/> on the
    /// first free spot. It works for any starting hull, wherever the hull sits in the grid.
    /// </summary>
    private static void FitPieceWithTag(ContentDb db, RunState run, Tag tag)
    {
        var grid = run.Ship.Grid;
        var all = Enumerable.Range(0, Rules.GridRows).SelectMany(y => Enumerable.Range(0, Rules.GridColumns).Select(x => new Cell(x, y)));
        grid.Attach([.. all.Where(c => !grid.Contains(c))]);
        var piece = run.NewPiece(db.Pieces.First(p => !p.IsBridge && p.Tags.Contains(tag)).Id);
        run.Cargo.Add(piece);
        var refit = new RefitEngine(db);
        var spot = Enum.GetValues<Rotation>()
            .SelectMany(r => grid.Cells.Select(origin => (Origin: origin, Rotation: r)))
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

    /// <summary>
    /// Every sector has enough enemies that a path seldom meets one twice: an enemy repeats in a run only after
    /// the rest of its pool has been met. The early rows can all be skirmishes, so the early pool needs one enemy
    /// for each of them. These are floors for any sector. The owner sized the first sector larger on 2026-11-22
    /// (4 early, 4 late, 3 hard and a boss), and this test does not hold it to those numbers.
    /// </summary>
    [Fact]
    public void EverySectorHasEnoughEnemiesInEveryPool()
    {
        var db = TestContent.LoadShipped();
        for (int sector = 1; sector <= Rules.SectorsPerRun; sector++)
        {
            int Count(EnemyPool pool) => db.Enemies.Count(e => e.Pool == pool && e.Sector == sector);
            Assert.True(Count(EnemyPool.Early) >= Rules.MapEarlyRows, $"Sector {sector} has too few early enemies.");
            Assert.True(Count(EnemyPool.Late) >= 3, $"Sector {sector} has too few late enemies.");
            Assert.True(Count(EnemyPool.Hard) >= 2, $"Sector {sector} has too few hard enemies.");
            Assert.True(Count(EnemyPool.Boss) >= 1, $"Sector {sector} has no boss.");
        }
    }

    /// <summary>
    /// Every sector has enough events that a path never has to meet one twice. The opening rows are skirmishes and
    /// the row before the boss is fixed, and any other row of a path can be an event.
    /// </summary>
    [Fact]
    public void EverySectorHasEnoughEvents()
    {
        var db = TestContent.LoadShipped();
        int rowsThatCanHoldAnEvent = Rules.MapRows - Rules.MapOpeningRows - (Rules.MapRowBeforeBoss is null ? 0 : 1);
        for (int sector = 1; sector <= Rules.SectorsPerRun; sector++)
        {
            int count = db.Events.Count(e => e.Sector == sector);
            Assert.True(count >= rowsThatCanHoldAnEvent, $"Sector {sector} has {count} events, but a path can hold {rowsThatCanHoldAnEvent}.");
        }
    }

    /// <summary>The spec's boss (owner, 2026-11-22): 2 or 3 parts, each acting every turn with its own pattern.</summary>
    [Fact]
    public void EveryShippedBossHasTwoOrThreeParts()
    {
        var db = TestContent.LoadShipped();
        Assert.All(db.Enemies.Where(e => e.Pool == EnemyPool.Boss), boss => Assert.InRange(boss.AllParts.Count, 2, 3));
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
            .Where(e => e.AllIntents.Any(i => i.Kind == IntentKind.HeavyAttack && i.Lane == 0))
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

    [Fact]
    public void TheHeliotropeMatchesItsStartingSetup()
    {
        var db = TestContent.LoadShipped();
        var def = db.Ship("heliotrope");
        var run = RunFactory.NewRun(db, "heliotrope", seed: 1);
        var fitter = new ShipFitter(db);

        Assert.IsType<KeepEnergyRule>(def.Rule);
        Assert.Equal("heliotrope", def.SoundSet);
        Assert.Null(db.Ship("seedling").SoundSet); // the Seedling's set is the base set
        Assert.Equal(60, run.Hull);
        Assert.Equal(18, run.Ship.Grid.CellCount);
        Assert.Equal(0, run.Ship.Grid.Cells.Min(c => c.X));
        Assert.Equal(5, run.Ship.Grid.Cells.Max(c => c.X));
        Assert.Equal(2, run.Ship.Grid.Cells.Min(c => c.Y));
        Assert.Equal(4, run.Ship.Grid.Cells.Max(c => c.Y));
        Assert.Equal(0, run.Ship.Grid.LaneHit(0));

        int usedCells = run.Ship.Fitted.Sum(f => fitter.CellsOf(f).Count);
        Assert.Equal(16, usedCells);
        Assert.Equal(2, run.Ship.Fitted.Count(f => f.Piece.DefId == "burning_glass"));
        Assert.Equal(11, DeckBuilder.Build(run.Ship, db).Count);

        var fittedDefs = run.Ship.Fitted.Select(f => db.Piece(f.Piece.DefId)).ToList();
        Assert.All(fittedDefs, p => Assert.Equal(Rarity.Common, p.Rarity));
        var nonCoreTags = fittedDefs.SelectMany(p => p.Tags).Where(t => t != Tag.Core).Distinct().Order();
        Assert.Equal([Tag.Weapon, Tag.Solar], nonCoreTags);

        string DefOf(int pieceId) => run.Ship.Find(pieceId)!.Piece.DefId;
        var bonuses = fitter.ActiveBonuses(run.Ship, [])
            .Select(b => $"{DefOf(b.GiverId)}->{DefOf(b.ReceiverId)}")
            .Order();
        Assert.Equal(["lens_grinder->burning_glass"], bonuses);
    }

    /// <summary>
    /// The Seedling's placeholder second loadout: a Garden and Shield start from existing pieces, with a Lance and
    /// an Arc Cutter so that it can still win a fight. Both crew start the Moss Reactor's cards grown.
    /// </summary>
    [Fact]
    public void TheSeedlingsOrchardLoadoutMatchesItsSetup()
    {
        var db = TestContent.LoadShipped();
        var seedling = db.Ship("seedling");
        Assert.Equal(["a", "b", "c"], seedling.Loadouts.Select(l => l.Id));

        var run = RunFactory.NewRun(db, new RunSetup("seedling", Seed: 1, LoadoutId: "b"));
        var fitter = new ShipFitter(db);
        Assert.Equal(60, run.Hull);
        Assert.Equal(25, run.Ship.Grid.CellCount);
        Assert.Equal(22, run.Ship.Fitted.Sum(f => fitter.CellsOf(f).Count));
        Assert.Equal(17, DeckBuilder.Build(run.Ship, db).Count);

        string DefOf(int pieceId) => run.Ship.Find(pieceId)!.Piece.DefId;
        var bonuses = fitter.ActiveBonuses(run.Ship, [])
            .Select(b => $"{DefOf(b.GiverId)}->{DefOf(b.ReceiverId)}")
            .Order();
        Assert.Equal(["orchard_keeper->moss_reactor", "spore_singer->moss_reactor"], bonuses);
    }

    /// <summary>
    /// The Seedling's placeholder third loadout starts with four passive pieces, one for each trigger, so that
    /// each kind of passive shows in the first fight. The Breakwater is the frontmost piece of lanes 1 to 3, and
    /// the Tinker is there to bring a disabled piece, and so its passive, back.
    /// </summary>
    [Fact]
    public void TheSeedlingsTidepoolLoadoutMatchesItsSetup()
    {
        var db = TestContent.LoadShipped();
        var run = RunFactory.NewRun(db, new RunSetup("seedling", Seed: 1, LoadoutId: "c"));
        var fitter = new ShipFitter(db);
        Assert.Equal(25, run.Ship.Grid.CellCount);
        Assert.Equal(19, run.Ship.Fitted.Sum(f => fitter.CellsOf(f).Count));
        Assert.Equal(12, DeckBuilder.Build(run.Ship, db).Count);
        Assert.Empty(fitter.ActiveBonuses(run.Ship, []));

        var passives = fitter.ActivePassives(run.Ship, []);
        Assert.Equal(["breakwater", "moss_bank", "lookout", "dew_still"], passives.Select(p => p.Fitted.Piece.DefId));
        Assert.Equal(
            [PassiveTrigger.Standing, PassiveTrigger.TurnStart, PassiveTrigger.FightStart, PassiveTrigger.FightWon],
            passives.Select(p => p.Passive.Trigger!.Value));
        Assert.All(new[] { 1, 2, 3 }, lane => Assert.Equal("breakwater", fitter.FrontmostInLane(run.Ship, lane, [])!.Piece.DefId));

        // The numbers are read from the content, so that rebalancing a piece does not break this.
        int Amount(string pieceId) => db.Piece(pieceId).Passive!.Effect switch
        {
            LaneArmorEffect armor => armor.Amount,
            BlockEffect block => block.Amount,
            DrawEffect draw => draw.Count,
            var other => throw new InvalidOperationException($"'{pieceId}' has the passive {other}."),
        };

        // The Feral Harvester's heavy attack on lane 2 lands on the Breakwater, less its armor.
        var heavy = db.Enemy("feral_harvester").Pattern.Single(i => i.Kind == IntentKind.HeavyAttack);
        var landing = new CombatEngine(db).EffectiveIntent(run.Ship, heavy, []);
        Assert.Equal("breakwater", landing.Target!.Piece.DefId);
        Assert.Equal((heavy.Amount - Amount("breakwater"), Amount("breakwater")), (landing.Intent.Amount, landing.Armor));

        // The first hand has the Lookout's cards on top of the turn's five, and the Moss Bank's block is up.
        var combat = new CombatEngine(db).StartFight(run, "feral_harvester");
        Assert.Equal(Rules.CardsDrawnPerTurn + Amount("lookout"), combat.Hand.Count);
        Assert.Equal(Amount("moss_bank"), combat.Block);
    }

    /// <summary>
    /// Every shipped passive has an upgrade, as every shipped card has, so that every piece can be upgraded at a
    /// grove, and each trigger is on at least one piece, so that every kind of passive can be met in play.
    /// </summary>
    [Fact]
    public void TheShippedPassivesCoverEveryTriggerAndCanBeUpgraded()
    {
        var db = TestContent.LoadShipped();
        var passive = db.Pieces.Where(p => p.Passive is not null).ToList();
        Assert.All(passive, p => Assert.NotNull(p.Passive!.Upgrade));
        Assert.All(Enum.GetValues<PassiveTrigger>(), trigger => Assert.Contains(passive, p => p.Passive!.Trigger == trigger));
    }

    /// <summary>The hangar shows each ship's commune and what it is grown from, and names each loadout.</summary>
    [Fact]
    public void EveryShipHasTheTextTheHangarShows()
    {
        var db = TestContent.LoadShipped();
        var communes = db.Pieces.Select(p => p.Commune).Where(c => c is not null).Distinct().ToList();
        Assert.All(db.Ships, ship =>
        {
            Assert.Contains(ship.Commune, communes);
            Assert.False(string.IsNullOrWhiteSpace(ship.GrownFrom));
            Assert.Equal(ship.Loadouts.Count, ship.Loadouts.Select(l => l.Name).Distinct().Count());
        });
    }
}
