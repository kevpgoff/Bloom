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

    private static ContentDb Create(PieceDef[]? pieces = null, CardDef[]? cards = null, EnemyDef[]? enemies = null, ShipClassDef[]? ships = null) =>
        ContentDb.Create(pieces ?? [Bridge, Lance], cards ?? [Ram], enemies ?? Enemies, ships ?? [Ship]);

    private static void AssertInvalid(string expectedError, PieceDef[]? pieces = null, CardDef[]? cards = null, EnemyDef[]? enemies = null, ShipClassDef[]? ships = null)
    {
        var e = Assert.Throws<ContentException>(() => Create(pieces, cards, enemies, ships));
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
        Assert.Equal(16, db.Pieces.Count); // the spec's 7, the placeholder Tinker and 8 placeholder rewards
        Assert.Equal(17, db.Cards.Count);  // the spec's 8, the placeholder Patch and 8 for the rewards
        Assert.Equal(12, db.Enemies.Count);
        Assert.Single(db.Ships);
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

    [Fact]
    public void EveryHeavyAttackHitsALaneTheSeedlingCovers()
    {
        var db = TestContent.LoadShipped();
        var run = RunFactory.NewRun(db, "seedling", seed: 1);
        var lanes = run.Ship.Grid.Cells.Select(c => c.X).ToHashSet();
        foreach (var enemy in db.Enemies)
        {
            foreach (var intent in enemy.Pattern.Where(i => i.Kind == IntentKind.HeavyAttack))
            {
                Assert.True(lanes.Contains(intent.Lane!.Value), $"{enemy.Id} attacks lane {intent.Lane}, which has no hull.");
            }
        }
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
