using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class CombatTests
{
    private readonly CombatEngine _engine = new(Db);

    private static int CardIn(CombatState combat, string cardId) => combat.Hand.First(c => c.CardId == cardId).Id;

    private static int TotalCards(CombatState c) =>
        c.DrawPile.Count + c.Hand.Count + c.DiscardPile.Count + c.PlayedPile.Count + c.OfflineCards.Count;

    [Fact]
    public void AFightStartsWithThreeEnergyAndFiveCards()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        var combat = _engine.StartFight(run, "dummy");
        Assert.Same(combat, run.Combat);
        Assert.Equal(1, combat.Turn);
        Assert.Equal(Rules.EnergyPerTurn, combat.Energy);
        Assert.Equal(Rules.CardsDrawnPerTurn, combat.Hand.Count);
        Assert.Single(combat.DrawPile);
        Assert.Equal(30, combat.Enemy.Hull);
    }

    [Fact]
    public void PlayingACardPaysEnergyAppliesItAndPutsItWithThePlayedCards()
    {
        var run = NewRun();
        var combat = _engine.StartFight(run, "dummy");
        int ram = CardIn(combat, "ram");
        Assert.Equal(CombatOutcome.Ongoing, _engine.PlayCard(run, ram));
        Assert.Equal(2, combat.Energy);
        Assert.Equal(25, combat.Enemy.Hull);
        Assert.Equal([ram], combat.PlayedPile.Select(c => c.Id));
        Assert.Empty(combat.DiscardPile);
        Assert.DoesNotContain(combat.Hand, c => c.Id == ram);
    }

    [Fact]
    public void EnemyBlockAbsorbsDamageFirst()
    {
        var run = NewRun();
        var combat = _engine.StartFight(run, "dummy");
        combat.Enemy.Block = 3;
        _engine.PlayCard(run, CardIn(combat, "ram"));
        Assert.Equal(0, combat.Enemy.Block);
        Assert.Equal(28, combat.Enemy.Hull);
    }

    [Fact]
    public void ACardCannotBePlayedWithoutEnoughEnergy()
    {
        var run = NewRun();
        var combat = _engine.StartFight(run, "dummy");
        combat.Energy = 0;
        int ram = CardIn(combat, "ram");
        Assert.False(_engine.CanPlay(run, ram));
        Assert.Throws<InvalidOperationException>(() => _engine.PlayCard(run, ram));
        Assert.Contains(combat.Hand, c => c.Id == ram);
    }

    [Fact]
    public void BlockAbsorbsEnemyDamageBeforeHullAndThenResets()
    {
        var run = NewRun();
        var combat = _engine.StartFight(run, "dummy");
        _engine.PlayCard(run, CardIn(combat, "brace"));
        Assert.Equal(5, combat.Block);
        _engine.EndTurn(run);
        Assert.Equal(59, run.Hull);
        Assert.Equal(0, combat.Block);
    }

    [Fact]
    public void EndingATurnDiscardsTheHandAndReshufflesWhenTheDrawPileRunsOut()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        Fit(run, "plating", 0, 1);
        var combat = _engine.StartFight(run, "dummy"); // 8 cards: 5 in hand, 3 to draw
        _engine.EndTurn(run);                          // draws 3, then reshuffles the 5 discarded cards and draws 2
        Assert.Equal(2, combat.Turn);
        Assert.Equal(Rules.EnergyPerTurn, combat.Energy);
        Assert.Equal(Rules.CardsDrawnPerTurn, combat.Hand.Count);
        Assert.Equal(3, combat.DrawPile.Count);
        Assert.Empty(combat.DiscardPile);
        Assert.Equal(8, TotalCards(combat));
    }

    [Fact]
    public void AHeavyAttackPastBlockDisablesTheFrontmostPieceInItsLane()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0); // front of lane 0
        var combat = _engine.StartFight(run, "heavy");
        _engine.EndTurn(run);
        Assert.Equal(50, run.Hull);
        Assert.Equal([plating.Id], combat.DisabledPieceIds);
        Assert.Equal(2, combat.OfflineCards.Count(c => c.SourcePieceId == plating.Id));
        Assert.DoesNotContain(combat.DrawPile.Concat(combat.Hand).Concat(combat.DiscardPile).Concat(combat.PlayedPile), c => c.SourcePieceId == plating.Id);
        Assert.Equal(6, TotalCards(combat));
    }

    [Fact]
    public void AFullyBlockedHeavyAttackDisablesNothing()
    {
        var run = NewRun();
        Fit(run, "plating", 0, 0);
        var combat = _engine.StartFight(run, "heavy");
        combat.Block = 10;
        _engine.EndTurn(run);
        Assert.Equal(60, run.Hull);
        Assert.Empty(combat.DisabledPieceIds);
    }

    // A card that was played stays out of the deck until the turn ends (the owner's ruling of 2026-11-23).

    [Fact]
    public void ACardThatWasPlayedIsNotDrawnAgainInTheSameTurn()
    {
        var run = NewRun(); // the bridge's 4 cards are all in the hand, so the draw and discard piles are empty
        var combat = _engine.StartFight(run, "dummy");
        int ram = CardIn(combat, "ram");
        _engine.PlayCard(run, ram);
        var chorus = new CardInstance(900, "chorus", BridgeId(run)); // draws 2
        combat.Hand.Add(chorus);

        _engine.PlayCard(run, chorus.Id);

        Assert.Equal(3, combat.Hand.Count); // nothing could be drawn
        Assert.DoesNotContain(combat.Hand, c => c.Id == ram);
        Assert.Equal([ram, chorus.Id], combat.PlayedPile.Select(c => c.Id));
        Assert.Empty(combat.DrawPile);
        Assert.Empty(combat.DiscardPile);
    }

    [Fact]
    public void AShuffleInTheMiddleOfATurnBringsBackEarlierDiscardsButNotThePlayedCards()
    {
        var run = NewRun(); // the bridge's 4 cards are all in the hand
        var combat = _engine.StartFight(run, "dummy");
        int ram = CardIn(combat, "ram");
        var earlier = combat.Hand.Where(c => c.Id != ram).Take(2).ToList(); // as if discarded in an earlier turn
        combat.Hand.RemoveAll(earlier.Contains);
        combat.DiscardPile.AddRange(earlier);
        _engine.PlayCard(run, ram);
        var chorus = new CardInstance(900, "chorus", BridgeId(run)); // draws 2
        combat.Hand.Add(chorus);

        _engine.PlayCard(run, chorus.Id); // the draw pile is empty, so the discard pile is shuffled into it

        Assert.All(earlier, c => Assert.Contains(c, combat.Hand));
        Assert.DoesNotContain(combat.Hand, c => c.Id == ram);
        Assert.Equal([ram, chorus.Id], combat.PlayedPile.Select(c => c.Id));
        Assert.Empty(combat.DiscardPile);
    }

    [Fact]
    public void PlayedCardsJoinTheDiscardPileWhenTheTurnEndsAndCanBeDrawnInTheNextTurn()
    {
        var run = NewRun(); // 4 cards, all in the hand
        var combat = _engine.StartFight(run, "dummy");
        int ram = CardIn(combat, "ram");
        _engine.PlayCard(run, ram);

        _engine.EndTurn(run); // the played card and the 3 others are discarded, shuffled and drawn again

        Assert.Empty(combat.PlayedPile);
        Assert.Equal(4, combat.Hand.Count);
        Assert.Contains(combat.Hand, c => c.Id == ram);
    }

    [Fact]
    public void AHeavyAttackAlsoTakesThePlayedCardsOfThePieceItDisablesOffline()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0); // front of lane 0
        var combat = _engine.StartFight(run, "heavy");
        _engine.PlayCard(run, Take(combat, "deflect")); // blocks 8 of the 10

        _engine.EndTurn(run);

        Assert.Equal(58, run.Hull);
        Assert.Equal([plating.Id], combat.DisabledPieceIds);
        Assert.Equal(2, combat.OfflineCards.Count(c => c.SourcePieceId == plating.Id));
        Assert.DoesNotContain(
            combat.DrawPile.Concat(combat.Hand).Concat(combat.DiscardPile).Concat(combat.PlayedPile),
            c => c.SourcePieceId == plating.Id);
        Assert.Equal(6, TotalCards(combat));
    }

    /// <summary>
    /// Before the ruling this turn had no end: each Chorus drew the other Chorus and the Unfurl back, and every
    /// round of Unfurl and Chorus left 1 more energy than the round before.
    /// </summary>
    [Fact]
    public void CardsThatPayForThemselvesAndDrawAreEachPlayedOnceInATurn()
    {
        var run = NewRun();
        var combat = _engine.StartFight(run, "dummy");
        combat.Hand.Clear();
        combat.Hand.AddRange([
            new CardInstance(900, "unfurl", BridgeId(run)), // gains 2 energy at no cost
            new CardInstance(901, "chorus", BridgeId(run)), // draws 2 for 1 energy
            new CardInstance(902, "chorus", BridgeId(run))]);

        int plays = 0;
        while (plays < 100 && combat.Hand.FirstOrDefault(c => _engine.CanPlay(run, c.Id)) is { } card)
        {
            _engine.PlayCard(run, card.Id);
            plays++;
        }

        Assert.Equal(3, plays);
        Assert.Empty(combat.Hand);
        Assert.Equal([900, 901, 902], combat.PlayedPile.Select(c => c.Id));
    }

    [Fact]
    public void ADisabledBridgeDrawsOneCardFewer()
    {
        var run = NewRun(); // the bridge is the only piece in lane 0
        var combat = _engine.StartFight(run, "heavy");
        Assert.Equal(Rules.CardsDrawnPerTurn, _engine.CardsToDraw(run, combat));
        _engine.EndTurn(run);
        Assert.Equal([BridgeId(run)], combat.DisabledPieceIds);
        Assert.Equal(Rules.CardsDrawnPerTurn - 1, _engine.CardsToDraw(run, combat));
    }

    [Fact]
    public void BringingTheEnemyToZeroWinsTheFight()
    {
        var run = NewRun();
        var combat = _engine.StartFight(run, "dummy");
        combat.Enemy.Hull = 5;
        Assert.Equal(CombatOutcome.Won, _engine.PlayCard(run, CardIn(combat, "ram")));
        Assert.Null(run.Combat);
        Assert.Equal(RunResult.InProgress, run.Result);
        Assert.Equal(new FightRecord("dummy", CombatOutcome.Won, 1), run.LastFight);
        Assert.Equal(1, run.FightsWon);
        Assert.NotNull(run.Reward);
        Assert.Equal(RunPhase.Reward, run.Phase);
    }

    [Fact]
    public void AFightCannotStartWhileARewardWaits()
    {
        var run = NewRun();
        var combat = _engine.StartFight(run, "dummy");
        combat.Enemy.Hull = 5;
        _engine.PlayCard(run, CardIn(combat, "ram"));
        Assert.Throws<InvalidOperationException>(() => _engine.StartFight(run, "dummy"));
        Assert.Null(run.Combat);
    }

    [Fact]
    public void ReachingZeroHullLosesTheRun()
    {
        var run = NewRun();
        _engine.StartFight(run, "dummy");
        run.Hull = 3;
        Assert.Equal(CombatOutcome.Lost, _engine.EndTurn(run));
        Assert.Equal(0, run.Hull);
        Assert.Equal(RunResult.Lost, run.Result);
        Assert.Null(run.Combat);
        Assert.Equal(new FightRecord("dummy", CombatOutcome.Lost, 1), run.LastFight);
        Assert.Equal(0, run.FightsWon);
        Assert.Null(run.Reward);
        Assert.Equal(RunPhase.Lost, run.Phase);
        Assert.Throws<InvalidOperationException>(() => _engine.StartFight(run, "dummy"));
    }

    [Fact]
    public void TheHeavyAttackTargetIsTheFrontmostWorkingPieceInItsLane()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        _engine.StartFight(run, "heavy");
        Assert.Equal(plating.Id, _engine.NextActions(run).Single().Target?.Piece.Id);

        _engine.EndTurn(run); // disables the plating; the bridge is next in lane 0
        Assert.Equal(BridgeId(run), _engine.NextActions(run).Single().Target?.Piece.Id);
    }

    /// <summary>
    /// "heavy" aims at lane 0. On a hull in lanes 1 to 5 it hits lane 1; once a section grows into lane 0, it hits
    /// lane 0 (owner, 2026-11-23).
    /// </summary>
    [Fact]
    public void AHeavyAttackOnTheLeftLaneHitsItOnlyOnceTheHullGrowsThere()
    {
        var run = NewRun();
        var hull = Enumerable.Range(1, 5).SelectMany(x => Enumerable.Range(0, 5).Select(y => new Cell(x, y)));
        run.Ship = new ShipLayout { Grid = new ShipGrid(hull.ToList()) };
        Fit(run, "bridge", 1, 4);
        var plating = Fit(run, "plating", 1, 0);
        _engine.StartFight(run, "heavy");
        Assert.Equal(1, _engine.NextActions(run).Single().Lane);
        Assert.Equal(plating.Id, _engine.NextActions(run).Single().Target?.Piece.Id);
        _engine.EndTurn(run);
        Assert.Contains(plating.Id, run.Combat!.DisabledPieceIds);

        run.Combat = null;
        run.Ship.Grid.Attach([new Cell(0, 0), new Cell(0, 1), new Cell(0, 2)]);
        var lance = Fit(run, "lance", 0, 0, Rotation.R90);
        _engine.StartFight(run, "heavy");
        Assert.Equal(0, _engine.NextActions(run).Single().Lane);
        Assert.Equal(lance.Id, _engine.NextActions(run).Single().Target?.Piece.Id);
    }

    [Fact]
    public void ThereIsNoHeavyAttackTargetWhenTheEnemyDoesNotMakeAHeavyAttack()
    {
        var run = NewRun();
        _engine.StartFight(run, "dummy");
        var action = _engine.NextActions(run).Single();
        Assert.Equal(new EnemyAction(null, new Intent(IntentKind.Attack, 6)), action);
    }

    [Fact]
    public void TheSameSeedDealsTheSameCards()
    {
        static IEnumerable<int> Hand(ulong seed)
        {
            var run = NewRun(seed);
            Fit(run, "lance", 0, 0);
            Fit(run, "plating", 0, 1);
            return new CombatEngine(Db).StartFight(run, "dummy").Hand.Select(c => c.Id).ToList();
        }

        Assert.Equal(Hand(5), Hand(5));
    }

    [Fact]
    public void KeepEnergyCarriesUnspentEnergyIntoTheNextTurn()
    {
        var run = NewRun("solar");
        var combat = _engine.StartFight(run, "dummy");
        Assert.Equal(Rules.EnergyPerTurn, combat.Energy);

        _engine.PlayCard(run, CardIn(combat, "ram"));
        Assert.Equal(Rules.EnergyPerTurn - 1, combat.Energy);

        _engine.EndTurn(run);
        Assert.Equal((Rules.EnergyPerTurn * 2) - 1, combat.Energy);
    }

    [Fact]
    public void KeepEnergyCarriesCardGainedEnergyAndStacksWithoutACap()
    {
        var run = NewRun("solar");
        Fit(run, "sail", 2, 2);
        var combat = _engine.StartFight(run, "dummy");
        _engine.PlayCard(run, Take(combat, "unfurl"));
        Assert.Equal(Rules.EnergyPerTurn + 2, combat.Energy);

        _engine.EndTurn(run);
        Assert.Equal((Rules.EnergyPerTurn * 2) + 2, combat.Energy);

        _engine.EndTurn(run);
        Assert.Equal((Rules.EnergyPerTurn * 3) + 2, combat.Energy);
    }

    [Fact]
    public void KeepEnergyDoesNotCarryBetweenFights()
    {
        var run = NewRun("solar");
        var combat = _engine.StartFight(run, "dummy");
        combat.Energy = 7;
        combat.Enemy.Hull = 5;
        Assert.Equal(CombatOutcome.Won, _engine.PlayCard(run, CardIn(combat, "ram")));

        run.Reward = null;
        var next = _engine.StartFight(run, "dummy");
        Assert.Equal(Rules.EnergyPerTurn, next.Energy);
    }

    [Fact]
    public void KeepEnergyStillAppliesWhenTheBridgeIsDisabled()
    {
        var run = NewRun("solar"); // bridge is the only piece in lane 0
        var combat = _engine.StartFight(run, "heavy");
        _engine.PlayCard(run, CardIn(combat, "ram")); // leaves 2 energy unspent

        _engine.EndTurn(run);
        Assert.Equal([BridgeId(run)], combat.DisabledPieceIds);
        Assert.Equal(Rules.CardsDrawnPerTurn - 1, _engine.CardsToDraw(run, combat));
        Assert.Equal((Rules.EnergyPerTurn * 2) - 1, combat.Energy);
    }
}
