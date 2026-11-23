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

    private static int TotalCards(CombatState c) => c.DrawPile.Count + c.Hand.Count + c.DiscardPile.Count + c.OfflineCards.Count;

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
    public void PlayingACardPaysEnergyAppliesItAndDiscardsIt()
    {
        var run = NewRun();
        var combat = _engine.StartFight(run, "dummy");
        int ram = CardIn(combat, "ram");
        Assert.Equal(CombatOutcome.Ongoing, _engine.PlayCard(run, ram));
        Assert.Equal(2, combat.Energy);
        Assert.Equal(25, combat.Enemy.Hull);
        Assert.Contains(combat.DiscardPile, c => c.Id == ram);
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
        Assert.DoesNotContain(combat.DrawPile.Concat(combat.Hand).Concat(combat.DiscardPile), c => c.SourcePieceId == plating.Id);
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
        Assert.Equal(plating.Id, _engine.HeavyAttackTarget(run)?.Piece.Id);

        _engine.EndTurn(run); // disables the plating; the bridge is next in lane 0
        Assert.Equal(BridgeId(run), _engine.HeavyAttackTarget(run)?.Piece.Id);
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
        Assert.Equal(1, _engine.HeavyLane(run));
        Assert.Equal(plating.Id, _engine.HeavyAttackTarget(run)?.Piece.Id);
        _engine.EndTurn(run);
        Assert.Contains(plating.Id, run.Combat!.DisabledPieceIds);

        run.Combat = null;
        run.Ship.Grid.Attach([new Cell(0, 0), new Cell(0, 1), new Cell(0, 2)]);
        var lance = Fit(run, "lance", 0, 0, Rotation.R90);
        _engine.StartFight(run, "heavy");
        Assert.Equal(0, _engine.HeavyLane(run));
        Assert.Equal(lance.Id, _engine.HeavyAttackTarget(run)?.Piece.Id);
    }

    [Fact]
    public void ThereIsNoHeavyAttackTargetWhenTheEnemyDoesNotMakeAHeavyAttack()
    {
        var run = NewRun();
        _engine.StartFight(run, "dummy");
        Assert.Null(_engine.HeavyAttackTarget(run));
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
}
