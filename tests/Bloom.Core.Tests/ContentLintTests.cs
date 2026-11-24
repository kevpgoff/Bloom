using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;

namespace Bloom.Core.Tests;

/// <summary>
/// The lint reports content that loads and plays but is very likely a mistake. Each test adds one such mistake
/// to the test content, which is valid, and looks for its finding.
/// </summary>
public class ContentLintTests
{
    private static readonly ContentDb Db = TestContent.Db;

    private static ContentDb With(
        IEnumerable<PieceDef>? pieces = null,
        IEnumerable<CardDef>? cards = null,
        IEnumerable<EnemyDef>? enemies = null,
        IEnumerable<ShipClassDef>? ships = null,
        IEnumerable<EventDef>? events = null) =>
        ContentDb.Create([.. pieces ?? Db.Pieces], [.. cards ?? Db.Cards], [.. enemies ?? Db.Enemies], [.. ships ?? Db.Ships], [.. Db.Sections], [.. events ?? Db.Events]);

    private static PieceDef Piece(string id, string card = "ram", string[]? shape = null, Rarity rarity = Rarity.Common, AdjacencyBonus? bonus = null, PieceKind kind = PieceKind.Equipment) =>
        new(id, id, kind, [], rarity, Shape.Parse(shape ?? ["##"]), [new CardGrant(card, 1)], bonus);

    private static void AssertFinding(ContentDb db, string expected) =>
        Assert.Contains(ContentLint.Check(db), finding => finding.Contains(expected, StringComparison.Ordinal));

    private static void AssertNoFinding(ContentDb db, string unexpected) =>
        Assert.DoesNotContain(ContentLint.Check(db), finding => finding.Contains(unexpected, StringComparison.Ordinal));

    /// <summary>The test content keeps two oddities on purpose (see <see cref="TestContent"/>), and the lint finds exactly those.</summary>
    [Fact]
    public void TheTestContentHasOnlyItsKnownOddities()
    {
        Assert.Equal(
            [
                "card 'chorus': no piece adds it.",
                "Only 1 Uncommon pieces can be offered, but a reward offers 3.",
                "Only 1 Rare pieces can be offered, but a reward offers 3.",
            ],
            ContentLint.Check(Db));
    }

    [Fact]
    public void ACardThatNoPieceAddsIsReported()
    {
        AssertFinding(With(cards: [.. Db.Cards, new CardDef("spare", "Spare", 1, [new DamageEffect(3)])]), "card 'spare': no piece adds it.");
        AssertNoFinding(With(), "card 'ram'");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEffectOfZeroIsReported(bool inUpgrade)
    {
        var card = inUpgrade
            ? new CardDef("dud", "Dud", 1, [new BlockEffect(4)], Upgrade: new CardUpgrade(Effects: [new BlockEffect(0)]))
            : new CardDef("dud", "Dud", 1, [new BlockEffect(0)]);
        var db = With(pieces: [.. Db.Pieces, Piece("dud", "dud")], cards: [.. Db.Cards, card]);
        AssertFinding(db, inUpgrade ? "card 'dud' (upgraded): 'BlockEffect' has an amount of 0" : "card 'dud': 'BlockEffect' has an amount of 0");
    }

    [Fact]
    public void GrowthWithoutBlockIsReported()
    {
        var card = new CardDef("vine", "Vine", 1, [new DamageEffect(4), new GrowEffect(2)]);
        AssertFinding(With(pieces: [.. Db.Pieces, Piece("vine", "vine")], cards: [.. Db.Cards, card]), "card 'vine': it grows but has no block");
        AssertNoFinding(With(), "card 'sprout'");
    }

    [Fact]
    public void ABonusThatGivesNothingIsReported() =>
        AssertFinding(
            With(pieces: [.. Db.Pieces, Piece("mute", bonus: new AdjacencyBonus("hush", Tag.Weapon, new DamageBonusEffect(0)))]),
            "piece 'mute': its bonus 'hush' has an amount of 0");

    [Fact]
    public void ABonusForATagThatNoPieceCarriesIsReported()
    {
        var pieces = Db.Pieces.Where(p => !p.Tags.Contains(Tag.Garden)).ToList();
        AssertFinding(With(pieces: pieces), "piece 'singer': its bonus targets Garden, a tag that no piece carries.");
    }

    [Fact]
    public void TwoDifferentBonusesWithOneNameAreReported()
    {
        var twin = Piece("twin", bonus: new AdjacencyBonus("lens_focus", Tag.Weapon, new DamageBonusEffect(5)));
        AssertFinding(With(pieces: [.. Db.Pieces, twin]), "piece 'twin': its bonus 'lens_focus' shares its name with a different bonus");

        var same = Piece("twin", bonus: new AdjacencyBonus("lens_focus", Tag.Weapon, new DamageBonusEffect(2)));
        AssertNoFinding(With(pieces: [.. Db.Pieces, same]), "shares its name");
    }

    [Fact]
    public void ARarityWithFewerPiecesThanAnOfferIsReported()
    {
        AssertFinding(With(), "Only 1 Rare pieces can be offered");
        var db = With(pieces: [.. Db.Pieces, Piece("r2", rarity: Rarity.Rare), Piece("r3", rarity: Rarity.Rare)]);
        AssertNoFinding(db, "Rare pieces can be offered");
    }

    [Fact]
    public void APieceThatFitsNoStartingHullIsReported()
    {
        // The test hulls are 5x5 at (0,0) and 6x3 at (0,2), so a line of 6 fits only the second one. Written
        // upright, it fits there only after a turn.
        string[] upright = ["#", "#", "#", "#", "#", "#"];
        AssertNoFinding(With(pieces: [.. Db.Pieces, Piece("long", shape: ["######"])]), "piece 'long'");
        AssertNoFinding(With(pieces: [.. Db.Pieces, Piece("long", shape: upright)]), "piece 'long'");
        var ships = Db.Ships.Where(s => s.Id == "test");
        AssertFinding(With(pieces: [.. Db.Pieces, Piece("long", shape: upright)], ships: ships), "piece 'long': it fits on no ship's starting hull");
    }

    [Fact]
    public void AnIntentOfZeroIsReported()
    {
        var idle = new EnemyDef("idle", "Idle", 10, [new Intent(IntentKind.Attack, 3), new Intent(IntentKind.Block, 0)], EnemyPool.Early);
        AssertFinding(With(enemies: [.. Db.Enemies, idle]), "enemy 'idle': an intent has an amount of 0");
    }

    [Fact]
    public void AnEventChoiceForATagThatNoPieceCarriesIsReported()
    {
        var pieces = Db.Pieces.Where(p => !p.Tags.Contains(Tag.Drone)).ToList();
        var events = Db.Events.Where(e => e.Id != "gift"); // its "uncommon piece" was the drone
        AssertFinding(With(pieces: pieces, events: events), "event 'signal': a choice needs Drone, a tag that no piece carries, so the choice can never open.");
        AssertNoFinding(With(), "event 'signal'");
    }

    /// <summary>The bridge is always fitted, so a choice that needs its tag hides nothing: it is open on every ship.</summary>
    [Fact]
    public void AnEventChoiceForTheBridgesTagIsReportedAsAlwaysOpen()
    {
        var ev = new EventDef("helm", "Helm", "A helm.", [
            new EventChoice("Steer", [new EventResult("Steered.")], NeedsTag: Tag.Core),
            new EventChoice("Leave", [new EventResult("Gone.")])]);
        AssertFinding(With(events: [.. Db.Events, ev]), "event 'helm': a choice needs Core, which the bridge always carries, so the choice is always open.");
    }

    [Fact]
    public void TwoEntriesOfAKindWithOneNameAreReported()
    {
        var db = With(pieces: [.. Db.Pieces, Piece("lance2") with { Name = "Lance" }]);
        AssertFinding(db, "Two pieces are named 'Lance'.");
        AssertNoFinding(db, "Two cards");
    }
}
