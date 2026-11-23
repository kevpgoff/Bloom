using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class CardUpgradeTests
{
    private readonly CombatEngine _engine = new(Db);

    /// <summary>Test setup: upgrades a piece directly, as a grove or an event would.</summary>
    private static void Upgrade(RunState run, int pieceId) => PieceUpgrades.Upgrade(Db, run, pieceId);

    [Fact]
    public void AnUpgradedCardTakesTheUpgradesEffectsAndKeepsItsCost()
    {
        var ram = Db.Card("ram", upgraded: true);
        Assert.Equal("Ram+", ram.Name);
        Assert.Equal(1, ram.Cost);
        Assert.Equal([new DamageEffect(8)], ram.Effects);
        Assert.Null(ram.Upgrade);
        Assert.Equal("Ram", Db.Card("ram").Name);
    }

    [Fact]
    public void AnUpgradedCardTakesTheUpgradesCostAndKeepsItsEffects()
    {
        var lance = Db.Card("lance", upgraded: true);
        Assert.Equal(1, lance.Cost);
        Assert.Equal(Db.Card("lance").Effects, lance.Effects);
    }

    [Fact]
    public void ACardWithoutAnUpgradeStaysTheSame() =>
        Assert.Same(Db.Card("brace"), Db.Card("brace", upgraded: true));

    [Fact]
    public void AnUpgradedPieceAddsUpgradedCards()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 1, 1);
        Upgrade(run, lance.Id);
        var deck = DeckBuilder.Build(run.Ship, Db);
        Assert.All(deck.Where(c => c.SourcePieceId == lance.Id), c => Assert.True(c.Upgraded));
        Assert.All(deck.Where(c => c.SourcePieceId != lance.Id), c => Assert.False(c.Upgraded));
    }

    [Fact]
    public void AnUpgradedCardPlaysAsItsUpgrade()
    {
        var run = NewRun();
        Upgrade(run, BridgeId(run));
        var combat = _engine.StartFight(run, "dummy");
        int ram = Take(combat, "ram");
        var preview = _engine.Preview(run, combat.Hand.Single(c => c.Id == ram));
        Assert.Equal("Ram+", preview.Def.Name);
        Assert.Equal([new DamageEffect(8)], preview.Effects);

        _engine.PlayCard(run, ram);
        Assert.Equal(22, combat.Enemy.Hull);
    }

    [Fact]
    public void AnUpgradedCostIsWhatThePlayCosts()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 1, 1);
        Upgrade(run, lance.Id);
        var combat = _engine.StartFight(run, "dummy");
        combat.Energy = 1;
        int card = Take(combat, "lance");
        Assert.True(_engine.CanPlay(run, card));
        _engine.PlayCard(run, card);
        Assert.Equal(0, combat.Energy);
        Assert.Equal(18, combat.Enemy.Hull);
    }

    [Fact]
    public void BonusesAddToTheUpgradedNumbers()
    {
        var run = NewRun();
        Upgrade(run, BridgeId(run));
        var combat = _engine.StartFight(run, "dummy");
        combat.PendingFocus = 4;
        int ram = Take(combat, "ram");
        var preview = _engine.Preview(run, combat.Hand.Single(c => c.Id == ram));
        Assert.Equal([new DamageEffect(12)], preview.Effects);
    }

    [Fact]
    public void UpgradedGrowthUsesTheUpgradedGrowAmount()
    {
        var run = NewRun();
        var garden = Fit(run, "garden", 1, 1);
        Upgrade(run, garden.Id);
        var combat = _engine.StartFight(run, "dummy");
        int sprout = Take(combat, "sprout");
        _engine.PlayCard(run, sprout);
        Assert.Equal(6, combat.Block);
        var card = combat.DiscardPile.Single(c => c.Id == sprout);
        Assert.Equal([new BlockEffect(9), new GrowEffect(3)], _engine.Preview(run, card).Effects);
    }

    [Fact]
    public void TheCardPlayedEventSaysTheCardWasUpgraded()
    {
        var events = new List<CombatEvent>();
        var engine = new CombatEngine(Db, events.Add);
        var run = NewRun();
        Upgrade(run, BridgeId(run));
        var combat = engine.StartFight(run, "dummy");
        engine.PlayCard(run, Take(combat, "ram"));
        Assert.True(events.OfType<CardPlayed>().Single().Upgraded);
    }

    [Fact]
    public void UpgradedCardsSurviveASave()
    {
        var run = NewRun();
        Upgrade(run, BridgeId(run));
        _engine.StartFight(run, "dummy");
        var restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(run));
        Assert.True(restored.Ship.Find(BridgeId(run))!.Piece.Upgraded);
        var all = restored.Combat!.Hand.Concat(restored.Combat.DrawPile).ToList();
        Assert.All(all, c => Assert.True(c.Upgraded));
    }
}

public class PieceUpgradeTests
{
    private readonly GroveEngine _grove = new(Db);

    private static string Json(RunState run) => SaveSerializer.Serialize(run);

    private static RunState AtGrove()
    {
        var run = UseTinyMap(NewRun());
        run.Path.Add(0);
        new MapEngine(Db).Choose(run, 1);
        return run;
    }

    [Fact]
    public void AGroveUpgradesAFittedPieceThenReturnsToTheMap()
    {
        var run = AtGrove();
        var lance = Fit(run, "lance", 1, 1);
        _grove.Upgrade(run, lance.Id);
        Assert.True(run.Ship.Find(lance.Id)!.Piece.Upgraded);
        Assert.Equal(new Cell(1, 1), run.Ship.Find(lance.Id)!.Origin);
        Assert.Null(run.Grove);
        Assert.Equal(RunPhase.Map, run.Phase);
    }

    [Fact]
    public void AGroveUpgradesAHeldPiece()
    {
        var run = AtGrove();
        var held = run.NewPiece("garden");
        run.Cargo.Add(held);
        _grove.Upgrade(run, held.Id);
        Assert.True(Assert.Single(run.Cargo.Pieces).Upgraded);
    }

    [Fact]
    public void APieceIsUpgradedOnlyOnce()
    {
        var run = AtGrove();
        var lance = Fit(run, "lance", 1, 1);
        PieceUpgrades.Upgrade(Db, run, lance.Id);
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _grove.Upgrade(run, lance.Id));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void APieceWithoutAnUpgradableCardCannotBeUpgraded()
    {
        var run = AtGrove();
        var plating = Fit(run, "plating", 1, 1);
        Assert.False(PieceUpgrades.CanUpgrade(Db, plating));
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _grove.Upgrade(run, plating.Id));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void APieceWithOneUpgradableCardCanBeUpgraded() =>
        Assert.True(PieceUpgrades.CanUpgrade(Db, new PieceInstance(1, "bridge"))); // ram can, brace cannot

    [Fact]
    public void UpgradableListsFittedPiecesThenHeldOnes()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 1, 1);
        Fit(run, "plating", 1, 2);
        var held = run.NewPiece("garden");
        run.Cargo.Add(held);
        Assert.Equal([BridgeId(run), lance.Id, held.Id], PieceUpgrades.Upgradable(Db, run).Select(p => p.Id));

        PieceUpgrades.Upgrade(Db, run, lance.Id);
        Assert.Equal([BridgeId(run), held.Id], PieceUpgrades.Upgradable(Db, run).Select(p => p.Id));
    }

    [Fact]
    public void UpgradingAPieceThatIsNotOwnedIsRefused()
    {
        var run = AtGrove();
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _grove.Upgrade(run, 999));
        Assert.Equal(before, Json(run));
    }

    [Fact]
    public void UpgradingAwayFromAGroveIsRefused()
    {
        var run = NewRun();
        string before = Json(run);
        Assert.Throws<InvalidOperationException>(() => _grove.Upgrade(run, BridgeId(run)));
        Assert.Equal(before, Json(run));
    }
}

public class CardUpgradeContentTests
{
    private static readonly PieceDef Bridge = new("bridge", "Bridge", PieceKind.Equipment, [Tag.Core], Rarity.Common, Shape.Parse(["##"]), [new CardGrant("ram", 2)]);
    private static readonly CardDef Ram = new("ram", "Ram", 1, [new DamageEffect(5)]);
    private static readonly EnemyDef Dummy = new("dummy", "Dummy", 10, [new Intent(IntentKind.Attack, 1)], EnemyPool.Early);
    private static readonly ShipClassDef Ship = new("ship", "Ship", 60, Shape.Parse(["###", "###"]), new Cell(0, 0), [new StartingPiece("bridge", new Cell(0, 0))]);

    private static void AssertInvalid(string expectedError, CardUpgrade upgrade)
    {
        var e = Assert.Throws<ContentException>(() => ContentDb.Create(
            [Bridge],
            [Ram with { Upgrade = upgrade }],
            [Dummy, Dummy with { Id = "late", Pool = EnemyPool.Late }, Dummy with { Id = "hard", Pool = EnemyPool.Hard }, Dummy with { Id = "boss", Pool = EnemyPool.Boss }],
            [Ship],
            [.. TestContent.Db.Sections],
            [.. TestContent.PlainEvents]));
        Assert.Contains(e.Errors, error => error.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void AnUpgradeThatChangesNothingIsReported() =>
        AssertInvalid("card 'ram': the upgrade must give a cost, effects or both", new CardUpgrade());

    [Fact]
    public void ANegativeUpgradedCostIsReported() =>
        AssertInvalid("the upgraded cost cannot be negative", new CardUpgrade(Cost: -1));

    [Fact]
    public void EmptyUpgradedEffectsAreReported() =>
        AssertInvalid("the upgrade's effects cannot be empty", new CardUpgrade(Effects: []));

    [Fact]
    public void ABonusEffectInAnUpgradeIsReported() =>
        AssertInvalid("card 'ram' (upgraded): 'DamageBonusEffect' can only be used in an adjacency bonus", new CardUpgrade(Effects: [new DamageBonusEffect(2)]));

    [Fact]
    public void AnUpgradeIsReadFromJson()
    {
        const string cards = """[{ "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "type": "damage", "amount": 5 }], "upgrade": { "cost": 0, "effects": [{ "type": "damage", "amount": 7 }] } }]""";
        const string pieces = """[{ "id": "bridge", "name": "Bridge", "kind": "Equipment", "tags": ["Core"], "rarity": "Common", "shape": ["##"], "cards": [{ "card": "ram", "count": 2 }] }]""";
        const string enemies = """
            [{ "id": "a", "name": "A", "pool": "Early", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
             { "id": "b", "name": "B", "pool": "Late", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
             { "id": "c", "name": "C", "pool": "Hard", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
             { "id": "d", "name": "D", "pool": "Boss", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] }]
            """;
        const string ships = """[{ "id": "s", "name": "S", "hull": 60, "hullShape": ["##"], "hullOrigin": { "x": 0, "y": 0 }, "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 0 } }] }]""";
        var db = ContentDb.FromJson(pieces, cards, enemies, ships, TestContent.SectionsJson, TestContent.PlainEventsJson);
        Assert.Equal(0, db.Card("ram").Upgrade!.Cost);
        Assert.Equal(1, db.Card("ram").Cost);
        Assert.Equal(0, db.Card("ram", upgraded: true).Cost);
        Assert.Equal([new DamageEffect(7)], db.Card("ram", upgraded: true).Effects);
    }

    /// <summary>The owner decided on 2026-11-22 that every card has an upgrade, written as placeholders for now.</summary>
    [Fact]
    public void EveryShippedCardHasAnUpgrade()
    {
        var db = LoadShipped();
        Assert.All(db.Cards, c => Assert.NotNull(c.Upgrade));
        Assert.All(db.Pieces, p => Assert.True(PieceUpgrades.CanUpgrade(db, new PieceInstance(1, p.Id)), p.Id));
    }
}
