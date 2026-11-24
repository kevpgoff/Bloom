using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

/// <summary>Focus, grow, repair and the adjacency bonuses in combat, as ruled by the owner on 2026-11-21.</summary>
public class EffectTests
{
    private readonly CombatEngine _engine = new(Db);

    private static CardInstance Card(CombatState combat, int id) =>
        combat.Hand.Concat(combat.DrawPile).Concat(combat.DiscardPile).Concat(combat.PlayedPile).Concat(combat.OfflineCards).Single(c => c.Id == id);

    private int PreviewDamage(RunState run, int cardId) =>
        _engine.Preview(run, Card(run.Combat!, cardId)).Effects.OfType<DamageEffect>().Single().Amount;

    private int PreviewBlock(RunState run, int cardId) =>
        _engine.Preview(run, Card(run.Combat!, cardId)).Effects.OfType<BlockEffect>().Single().Amount;

    // Focus

    [Fact]
    public void FocusAddsToTheNextAttack()
    {
        var run = NewRun();
        Fit(run, "grinder", 3, 0);
        var combat = _engine.StartFight(run, "dummy");
        _engine.PlayCard(run, Take(combat, "focus"));
        Assert.Equal(4, combat.PendingFocus);

        _engine.PlayCard(run, Take(combat, "ram"));
        Assert.Equal(30 - 9, combat.Enemy.Hull);
        Assert.Equal(0, combat.PendingFocus);
    }

    [Fact]
    public void FocusAddsUp()
    {
        var run = NewRun();
        Fit(run, "grinder", 3, 0);
        Fit(run, "grinder", 3, 1);
        var combat = _engine.StartFight(run, "dummy");
        _engine.PlayCard(run, Take(combat, "focus"));
        _engine.PlayCard(run, Take(combat, "focus"));
        Assert.Equal(8, combat.PendingFocus);

        _engine.PlayCard(run, Take(combat, "ram"));
        Assert.Equal(30 - 13, combat.Enemy.Hull);
    }

    [Fact]
    public void FocusLastsIntoTheNextTurn()
    {
        var run = NewRun();
        Fit(run, "grinder", 3, 0);
        var combat = _engine.StartFight(run, "dummy");
        _engine.PlayCard(run, Take(combat, "focus"));
        _engine.EndTurn(run);
        Assert.Equal(4, combat.PendingFocus);

        _engine.PlayCard(run, Take(combat, "ram"));
        Assert.Equal(30 - 9, combat.Enemy.Hull);
    }

    [Fact]
    public void FocusIsUsedUpEvenWhenBlockAbsorbsTheWholeAttack()
    {
        var run = NewRun();
        Fit(run, "grinder", 3, 0);
        var combat = _engine.StartFight(run, "dummy");
        combat.Enemy.Block = 20;
        _engine.PlayCard(run, Take(combat, "focus"));
        _engine.PlayCard(run, Take(combat, "ram"));
        Assert.Equal(20 - 9, combat.Enemy.Block);
        Assert.Equal(30, combat.Enemy.Hull);
        Assert.Equal(0, combat.PendingFocus);
    }

    [Fact]
    public void ACardWithoutDamageLeavesFocusPending()
    {
        var run = NewRun();
        Fit(run, "grinder", 3, 0);
        var combat = _engine.StartFight(run, "dummy");
        _engine.PlayCard(run, Take(combat, "focus"));
        _engine.PlayCard(run, Take(combat, "brace"));
        Assert.Equal(5, combat.Block);
        Assert.Equal(4, combat.PendingFocus);
    }

    // Grow

    [Fact]
    public void GrowthBuildsUpPlayAfterPlay()
    {
        var run = NewRun();
        Fit(run, "garden", 0, 0);
        var combat = _engine.StartFight(run, "dummy");
        combat.Energy = 10;
        int sprout = combat.Hand.Concat(combat.DrawPile).First(c => c.CardId == "sprout").Id;

        var gains = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            MoveToHand(combat, sprout);
            int before = combat.Block;
            _engine.PlayCard(run, sprout);
            gains.Add(combat.Block - before);
        }

        Assert.Equal([4, 6, 8], gains);
    }

    [Fact]
    public void EachCopyGrowsOnItsOwn()
    {
        var run = NewRun();
        Fit(run, "garden", 0, 0);
        var combat = _engine.StartFight(run, "dummy");
        int first = Take(combat, "sprout");
        int second = Take(combat, "sprout", skip: 1);

        _engine.PlayCard(run, first);
        Assert.Equal(4, PreviewBlock(run, second));
        _engine.PlayCard(run, second);
        Assert.Equal(8, combat.Block);
        Assert.Equal(1, Card(combat, first).Growth);
        Assert.Equal(1, Card(combat, second).Growth);
    }

    // Adjacency bonuses

    [Fact]
    public void TheGrinderAddsDamageToAnAdjacentWeapon()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        Fit(run, "grinder", 0, 1);
        var combat = _engine.StartFight(run, "dummy");
        int lance = Take(combat, "lance");
        Assert.Equal(14, PreviewDamage(run, lance));

        _engine.PlayCard(run, lance);
        Assert.Equal(30 - 14, combat.Enemy.Hull);
    }

    [Fact]
    public void AGrinderThatDoesNotTouchTheWeaponAddsNothing()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        Fit(run, "grinder", 3, 2);
        var combat = _engine.StartFight(run, "dummy");
        _engine.PlayCard(run, Take(combat, "lance"));
        Assert.Equal(30 - 12, combat.Enemy.Hull);
    }

    [Fact]
    public void ADisabledGrinderStopsGivingItsBonus()
    {
        var run = NewRun();
        var grinder = Fit(run, "grinder", 0, 0); // front of lane 0
        Fit(run, "lance", 0, 1);
        var combat = _engine.StartFight(run, "heavy");
        int lance = Take(combat, "lance");
        Assert.Equal(14, PreviewDamage(run, lance));

        _engine.EndTurn(run);
        Assert.Equal([grinder.Id], combat.DisabledPieceIds);
        lance = Take(combat, "lance");
        Assert.Equal(12, PreviewDamage(run, lance));
        _engine.PlayCard(run, lance);
        Assert.Equal(100 - 12, combat.Enemy.Hull);
    }

    [Fact]
    public void TheSingerMakesAdjacentGardenCardsStartGrown()
    {
        var run = NewRun();
        Fit(run, "singer", 0, 0);
        Fit(run, "garden", 0, 1);
        Fit(run, "garden", 3, 3); // touches no singer
        var combat = _engine.StartFight(run, "dummy");
        var sprouts = combat.Hand.Concat(combat.DrawPile).Where(c => c.CardId == "sprout").ToList();
        Assert.Equal([1, 1, 0, 0], sprouts.Select(c => c.Growth).Order().Reverse());

        int grown = sprouts.First(c => c.Growth == 1).Id;
        MoveToHand(combat, grown);
        _engine.PlayCard(run, grown);
        Assert.Equal(6, combat.Block);
    }

    [Fact]
    public void StartingGrowthStaysWhenTheSingerIsDisabled()
    {
        var run = NewRun();
        var singer = Fit(run, "singer", 0, 0); // front of lane 0
        Fit(run, "garden", 0, 1);
        var combat = _engine.StartFight(run, "heavy");
        _engine.EndTurn(run);
        Assert.Equal([singer.Id], combat.DisabledPieceIds);

        int sprout = Take(combat, "sprout");
        Assert.Equal(6, PreviewBlock(run, sprout));
        _engine.PlayCard(run, sprout);
        Assert.Equal(6, combat.Block);
    }

    // Repair

    [Fact]
    public void RepairBringsThePickedPieceBackAndItsCardsGoToTheDiscardPile()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0); // front of lane 0
        Fit(run, "tinker", 4, 0);
        var combat = _engine.StartFight(run, "heavy");
        _engine.EndTurn(run);
        Assert.Equal([plating.Id], combat.DisabledPieceIds);

        int patch = Take(combat, "patch");
        Assert.True(_engine.CanPlay(run, patch));
        Assert.Equal(1, _engine.RepairTargetsNeeded(run, patch));
        _engine.PlayCard(run, patch, [plating.Id]);

        Assert.Empty(combat.DisabledPieceIds);
        Assert.Empty(combat.OfflineCards);
        Assert.Equal(2, combat.DiscardPile.Count(c => c.SourcePieceId == plating.Id));
        Assert.Contains(combat.PlayedPile, c => c.Id == patch);
    }

    [Fact]
    public void RepairedCardsKeepTheirGrowth()
    {
        var run = NewRun();
        var garden = Fit(run, "garden", 0, 0); // front of lane 0
        Fit(run, "singer", 2, 0);              // grows it, from lane 2
        Fit(run, "tinker", 4, 0);
        var combat = _engine.StartFight(run, "heavy");
        _engine.EndTurn(run);
        Assert.Equal([garden.Id], combat.DisabledPieceIds);

        _engine.PlayCard(run, Take(combat, "patch"), [garden.Id]);
        var sprouts = combat.DiscardPile.Where(c => c.CardId == "sprout").ToList();
        Assert.Equal(2, sprouts.Count);
        Assert.All(sprouts, c => Assert.Equal(1, c.Growth));
    }

    [Fact]
    public void RepairingTheBridgeRestoresTheDraw()
    {
        var run = NewRun(); // the bridge is the only piece in lane 0
        Fit(run, "tinker", 4, 0);
        var combat = _engine.StartFight(run, "heavy");
        _engine.EndTurn(run);
        Assert.Equal(Rules.CardsDrawnPerTurn - 1, _engine.CardsToDraw(run, combat));

        _engine.PlayCard(run, Take(combat, "patch"), [BridgeId(run)]);
        Assert.Equal(Rules.CardsDrawnPerTurn, _engine.CardsToDraw(run, combat));
    }

    [Fact]
    public void ARepairCardCannotBePlayedWhileNothingIsDisabled()
    {
        var run = NewRun();
        Fit(run, "tinker", 4, 0);
        var combat = _engine.StartFight(run, "dummy");
        int patch = Take(combat, "patch");
        string before = SaveSerializer.Serialize(run);

        Assert.False(_engine.CanPlay(run, patch));
        var preview = _engine.Preview(run, Card(combat, patch));
        Assert.False(preview.Playable);
        Assert.Equal(0, preview.RepairTargets);
        Assert.Throws<InvalidOperationException>(() => _engine.PlayCard(run, patch));
        Assert.Equal(before, SaveSerializer.Serialize(run));
    }

    [Fact]
    public void RepairNeedsNoMoreTargetsThanThereAreDisabledPieces()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        Fit(run, "fixer", 4, 0);
        var combat = _engine.StartFight(run, "heavy");
        _engine.EndTurn(run);

        int overhaul = Take(combat, "overhaul"); // repairs 2, but only 1 piece is disabled
        Assert.Equal(1, _engine.RepairTargetsNeeded(run, overhaul));
        _engine.PlayCard(run, overhaul, [plating.Id]);
        Assert.Empty(combat.DisabledPieceIds);
    }

    [Fact]
    public void RepairCanBringBackSeveralPieces()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        var lance = Fit(run, "lance", 0, 1);
        Fit(run, "fixer", 4, 0);
        var combat = _engine.StartFight(run, "heavy");
        _engine.EndTurn(run);
        _engine.EndTurn(run);
        Assert.Equal([plating.Id, lance.Id], combat.DisabledPieceIds);

        int overhaul = Take(combat, "overhaul");
        Assert.Equal(2, _engine.RepairTargetsNeeded(run, overhaul));
        _engine.PlayCard(run, overhaul, [lance.Id, plating.Id]);
        Assert.Empty(combat.DisabledPieceIds);
        Assert.Empty(combat.OfflineCards);
    }

    public static TheoryData<string> BadTargets() => ["none", "too many", "duplicate", "not disabled", "unknown"];

    [Theory]
    [MemberData(nameof(BadTargets))]
    public void WrongRepairTargetsThrowAndChangeNothing(string kind)
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        var lance = Fit(run, "lance", 0, 1);
        var fixer = Fit(run, "fixer", 4, 0);
        var combat = _engine.StartFight(run, "heavy");
        _engine.EndTurn(run);
        _engine.EndTurn(run); // plating and lance are disabled; overhaul needs 2 targets
        int overhaul = Take(combat, "overhaul");
        IReadOnlyList<int>? targets = kind switch
        {
            "none" => null,
            "too many" => [plating.Id, lance.Id, BridgeId(run)],
            "duplicate" => [plating.Id, plating.Id],
            "not disabled" => [plating.Id, fixer.Id],
            _ => [plating.Id, 999],
        };
        string before = SaveSerializer.Serialize(run);

        Assert.Throws<InvalidOperationException>(() => _engine.PlayCard(run, overhaul, targets));
        Assert.Equal(before, SaveSerializer.Serialize(run));
    }

    [Fact]
    public void ACardThatDoesNotRepairTakesNoTargets()
    {
        var run = NewRun();
        var combat = _engine.StartFight(run, "dummy");
        int ram = Take(combat, "ram");
        string before = SaveSerializer.Serialize(run);

        Assert.Throws<InvalidOperationException>(() => _engine.PlayCard(run, ram, [BridgeId(run)]));
        Assert.Equal(before, SaveSerializer.Serialize(run));
    }

    // Preview

    [Fact]
    public void ThePreviewShowsTheFinalNumbers()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        Fit(run, "grinder", 0, 1);
        Fit(run, "garden", 3, 2);
        Fit(run, "singer", 3, 3);
        var combat = _engine.StartFight(run, "dummy");
        _engine.PlayCard(run, Take(combat, "focus"));
        int lance = Take(combat, "lance");
        int sprout = Take(combat, "sprout");

        var preview = _engine.Preview(run, Card(combat, lance));
        Assert.Equal(new DamageEffect(12 + 2 + 4), Assert.Single(preview.Effects));
        Assert.Equal(new DamageEffect(12), Assert.Single(preview.Def.Effects));
        Assert.True(preview.Playable);
        Assert.Equal(0, preview.RepairTargets);
        Assert.Equal(4 + 2, PreviewBlock(run, sprout));
    }

    [Fact]
    public void ACardTooExpensiveToPlayIsNotPlayableInThePreview()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        var combat = _engine.StartFight(run, "dummy");
        combat.Energy = 1;
        int lance = Take(combat, "lance");
        Assert.False(_engine.Preview(run, Card(combat, lance)).Playable);
    }

    private static void MoveToHand(CombatState combat, int cardId)
    {
        foreach (var pile in new[] { combat.DrawPile, combat.DiscardPile, combat.PlayedPile })
        {
            if (pile.Find(c => c.Id == cardId) is { } card)
            {
                pile.Remove(card);
                combat.Hand.Add(card);
                return;
            }
        }
    }
}
