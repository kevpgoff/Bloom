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
    private static readonly EnemyDef Dummy = new("dummy", "Dummy", 10, [new Intent(IntentKind.Attack, 1)]);
    private static readonly ShipClassDef Ship = new("ship", "Ship", 60, Shape.Parse(["###", "###"]), new Cell(0, 0), [new StartingPiece("bridge", new Cell(0, 0))]);

    private static ContentDb Create(PieceDef[]? pieces = null, CardDef[]? cards = null, EnemyDef[]? enemies = null, ShipClassDef[]? ships = null) =>
        ContentDb.Create(pieces ?? [Bridge, Lance], cards ?? [Ram], enemies ?? [Dummy], ships ?? [Ship]);

    private static void AssertInvalid(string expectedError, PieceDef[]? pieces = null, CardDef[]? cards = null, EnemyDef[]? enemies = null, ShipClassDef[]? ships = null)
    {
        var e = Assert.Throws<ContentException>(() => Create(pieces, cards, enemies, ships));
        Assert.Contains(e.Errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidContentLoads() => Create();

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
    public void BrokenJsonNamesTheFile()
    {
        var e = Assert.Throws<ContentException>(() => ContentDb.FromJson("[", "[]", "[]", "[]"));
        Assert.StartsWith("pieces.json:", Assert.Single(e.Errors));
    }

    [Fact]
    public void AnEffectMustStartWithItsType()
    {
        // .NET 8 reads the "type" discriminator only when it comes first. If this starts passing after a
        // runtime upgrade, the rule in game/data/README.md can be dropped.
        const string cards = """[{ "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "amount": 5, "type": "damage" }] }]""";
        Assert.Throws<ContentException>(() => ContentDb.FromJson("[]", cards, "[]", "[]"));
    }
}

public class ShippedContentTests
{
    [Fact]
    public void TheShippedContentLoadsAndPassesValidation()
    {
        var db = TestContent.LoadShipped();
        Assert.Equal(7, db.Pieces.Count);
        Assert.Equal(8, db.Cards.Count);
        Assert.Single(db.Enemies);
        Assert.Single(db.Ships);
    }

    [Fact]
    public void TheStartingShipMatchesTheSpec()
    {
        var db = TestContent.LoadShipped();
        var run = RunFactory.NewRun(db, "seedling", seed: 1);
        Assert.Equal(20, run.Ship.Grid.CellCount);       // "about 20 cells"
        Assert.Equal(9, DeckBuilder.Build(run.Ship, db).Count); // "about 10 cards"
        Assert.Equal(60, run.Hull);
    }
}
