using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

/// <summary>
/// The events the combat engine reports, which the game turns into particles, shake and haptics. They
/// must tell what happened in the order it happened, and say nothing about a rejected action.
/// </summary>
public class CombatEventTests
{
    private readonly List<CombatEvent> _log = [];
    private readonly CombatEngine _engine;

    public CombatEventTests() => _engine = new CombatEngine(Db, _log.Add);

    /// <summary>Starts a fight and forgets the events of its start, so a test sees only its own action.</summary>
    private CombatState Start(RunState run, string enemyId)
    {
        var combat = _engine.StartFight(run, enemyId);
        _log.Clear();
        return combat;
    }

    private void AssertEvents(params CombatEvent[] expected) => Assert.Equal(expected, _log);

    private static void MoveToHand(CombatState combat, int cardId)
    {
        var card = combat.DiscardPile.Concat(combat.DrawPile).Concat(combat.PlayedPile).Single(c => c.Id == cardId);
        combat.DiscardPile.Remove(card);
        combat.PlayedPile.Remove(card);
        combat.DrawPile.Remove(card);
        combat.Hand.Add(card);
    }

    // Cards

    [Fact]
    public void AnAttackReportsThePlayThenTheHit()
    {
        var run = NewRun();
        var combat = Start(run, "dummy");
        int ram = Take(combat, "ram");
        _engine.PlayCard(run, ram);
        AssertEvents(new CardPlayed(ram, "ram", BridgeId(run)), new EnemyHit(5, 0, 5, 25));
    }

    [Fact]
    public void AHitSplitsItsDamageBetweenEnemyBlockAndHull()
    {
        var run = NewRun();
        var combat = Start(run, "dummy");
        combat.Enemy.Block = 3;
        _engine.PlayCard(run, Take(combat, "ram"));
        Assert.Equal(new EnemyHit(5, 3, 2, 28), _log[^1]);
    }

    [Fact]
    public void FocusIsReportedWhenGainedAndWhenTheNextAttackSpendsIt()
    {
        var run = NewRun();
        var grinder = Fit(run, "grinder", 3, 0);
        var combat = Start(run, "dummy");
        int focus = Take(combat, "focus");
        _engine.PlayCard(run, focus);
        AssertEvents(new CardPlayed(focus, "focus", grinder.Id), new FocusGained(4, 4));

        _log.Clear();
        int ram = Take(combat, "ram");
        _engine.PlayCard(run, ram);
        AssertEvents(new CardPlayed(ram, "ram", BridgeId(run)), new FocusSpent(4), new EnemyHit(9, 0, 9, 21));
    }

    [Fact]
    public void ACardWithoutDamageDoesNotSpendFocus()
    {
        var run = NewRun();
        Fit(run, "grinder", 3, 0);
        var combat = Start(run, "dummy");
        _engine.PlayCard(run, Take(combat, "focus"));
        _log.Clear();
        int brace = Take(combat, "brace");
        _engine.PlayCard(run, brace);
        AssertEvents(new CardPlayed(brace, "brace", BridgeId(run)), new BlockGained(5, 5));
        Assert.Equal(4, combat.PendingFocus);
    }

    [Fact]
    public void GrowthIsReportedAfterTheCardsEffects()
    {
        var run = NewRun();
        var garden = Fit(run, "garden", 3, 0);
        var combat = Start(run, "dummy");
        int sprout = Take(combat, "sprout");
        _engine.PlayCard(run, sprout);
        AssertEvents(new CardPlayed(sprout, "sprout", garden.Id), new BlockGained(4, 4), new CardGrew(sprout, 1));

        _log.Clear();
        MoveToHand(combat, sprout);
        _engine.PlayCard(run, sprout);
        AssertEvents(new CardPlayed(sprout, "sprout", garden.Id), new BlockGained(6, 10), new CardGrew(sprout, 2));
    }

    [Fact]
    public void GainedEnergyIsReportedWithTheNewTotal()
    {
        var run = NewRun();
        var sail = Fit(run, "sail", 0, 0);
        var combat = Start(run, "dummy");
        int unfurl = Take(combat, "unfurl");
        _engine.PlayCard(run, unfurl);
        AssertEvents(new CardPlayed(unfurl, "unfurl", sail.Id), new EnergyGained(2, 5));
    }

    [Fact]
    public void ADrawReportsEachCardAndTheReshuffleBetweenThem()
    {
        var run = NewRun(); // the bridge's 4 cards are all in the hand
        var combat = Start(run, "dummy");
        var basics = combat.Hand.ToList();
        combat.Hand.Clear();
        combat.DrawPile.Add(basics[0]);
        combat.DiscardPile.AddRange(basics.Skip(1));
        var chorus = new CardInstance(900, "chorus", BridgeId(run));
        combat.Hand.Add(chorus);

        _engine.PlayCard(run, chorus.Id);
        Assert.Equal(4, _log.Count);
        Assert.Equal(new CardPlayed(chorus.Id, "chorus", BridgeId(run)), _log[0]);
        Assert.Equal(new CardDrawn(basics[0].Id), _log[1]);
        Assert.Equal(new DiscardShuffled(3), _log[2]);
        Assert.Equal(new CardDrawn(combat.Hand[^1].Id), _log[3]);
        Assert.Contains(basics.Skip(1), c => c.Id == combat.Hand[^1].Id);
    }

    [Fact]
    public void AShuffleInTheMiddleOfATurnCountsOnlyTheDiscardPile()
    {
        var run = NewRun(); // the bridge's 4 cards are all in the hand
        var combat = Start(run, "dummy");
        var basics = combat.Hand.ToList();
        combat.Hand.Clear();
        combat.PlayedPile.Add(basics[0]);            // played earlier this turn
        combat.DiscardPile.AddRange(basics.Skip(1)); // discarded in an earlier turn
        var chorus = new CardInstance(900, "chorus", BridgeId(run));
        combat.Hand.Add(chorus);

        _engine.PlayCard(run, chorus.Id);
        Assert.Equal(
            [typeof(CardPlayed), typeof(DiscardShuffled), typeof(CardDrawn), typeof(CardDrawn)],
            _log.Select(e => e.GetType()));
        Assert.Equal(new DiscardShuffled(3), _log[1]);
        Assert.DoesNotContain(combat.Hand, c => c.Id == basics[0].Id);
    }

    [Fact]
    public void DrawingFromTwoEmptyPilesReportsNothing()
    {
        var run = NewRun();
        var combat = Start(run, "dummy");
        combat.Hand.Clear();
        var chorus = new CardInstance(900, "chorus", BridgeId(run));
        combat.Hand.Add(chorus);
        _engine.PlayCard(run, chorus.Id);
        AssertEvents(new CardPlayed(chorus.Id, "chorus", BridgeId(run)));
    }

    [Fact]
    public void RepairsAreReportedInTheOrderPicked()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        var lance = Fit(run, "lance", 0, 1);
        var fixer = Fit(run, "fixer", 4, 0);
        var combat = Start(run, "dummy");
        combat.DisabledPieceIds.AddRange([plating.Id, lance.Id]);
        int overhaul = Take(combat, "overhaul");
        _engine.PlayCard(run, overhaul, [lance.Id, plating.Id]);
        AssertEvents(new CardPlayed(overhaul, "overhaul", fixer.Id), new PieceRepaired(lance.Id), new PieceRepaired(plating.Id));
    }

    [Fact]
    public void ARejectedPlayReportsNothing()
    {
        var run = NewRun();
        var combat = Start(run, "dummy");
        combat.Energy = 0;
        Assert.Throws<InvalidOperationException>(() => _engine.PlayCard(run, Take(combat, "ram")));
        Assert.Empty(_log);
    }

    // The enemy's turn

    [Fact]
    public void AnAttackReportsWhatBlockAbsorbed()
    {
        var run = NewRun();
        var combat = Start(run, "dummy");
        _engine.PlayCard(run, Take(combat, "brace"));
        _log.Clear();
        _engine.EndTurn(run);
        Assert.Equal(new ShipHit(6, 5, 1, 59, null), _log[0]);
    }

    [Fact]
    public void AHeavyAttackReportsItsLaneAndThePieceItDisables()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        Start(run, "heavy");
        _engine.EndTurn(run);
        Assert.Equal(new ShipHit(10, 0, 10, 50, 0), _log[0]);
        Assert.Equal(new PieceDisabled(plating.Id, 0), _log[1]);
    }

    [Fact]
    public void AFullyBlockedHeavyAttackReportsNoDisable()
    {
        var run = NewRun();
        Fit(run, "plating", 0, 0);
        var combat = Start(run, "heavy");
        combat.Block = 10;
        _engine.EndTurn(run);
        Assert.Equal(new ShipHit(10, 10, 0, 60, 0), _log[0]);
        Assert.DoesNotContain(_log, e => e is PieceDisabled);
    }

    [Fact]
    public void EnemyBlockIsReportedWithItsTotal()
    {
        var run = NewRun();
        Start(run, "blocker");
        _engine.EndTurn(run);
        Assert.Equal(new EnemyBlocked(5, 5), _log[0]);
    }

    [Fact]
    public void EndingATurnReportsTheEnemyThenTheDiscardThenTheNewTurnsDraws()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        Fit(run, "plating", 0, 1);
        Start(run, "dummy"); // 8 cards: 5 in hand, 3 to draw
        _engine.EndTurn(run); // draws 3, then reshuffles the 5 discarded cards and draws 2
        Assert.Equal(new ShipHit(6, 0, 6, 54, null), _log[0]);
        Assert.Equal(new HandDiscarded(5), _log[1]);
        Assert.Equal(new TurnStarted(2), _log[2]);
        Assert.Equal(
            [typeof(CardDrawn), typeof(CardDrawn), typeof(CardDrawn), typeof(DiscardShuffled), typeof(CardDrawn), typeof(CardDrawn)],
            _log.Skip(3).Select(e => e.GetType()));
        Assert.Equal(new DiscardShuffled(5), _log[6]);
    }

    // The end of a fight

    [Fact]
    public void AKillingBlowEndsWithTheWin()
    {
        var run = NewRun();
        var combat = Start(run, "dummy");
        combat.Enemy.Hull = 3;
        int ram = Take(combat, "ram");
        _engine.PlayCard(run, ram);
        AssertEvents(new CardPlayed(ram, "ram", BridgeId(run)), new EnemyHit(5, 0, 5, 0), new FightEnded(CombatOutcome.Won));
    }

    [Fact]
    public void ALossEndsWithTheHitAndNoNewTurn()
    {
        var run = NewRun();
        Start(run, "dummy");
        run.Hull = 6;
        _engine.EndTurn(run);
        AssertEvents(new ShipHit(6, 0, 6, 0, null), new FightEnded(CombatOutcome.Lost));
    }

    // Through the session

    [Fact]
    public void StartingTheChosenFightReportsTheStartTheTurnAndTheOpeningHand()
    {
        var session = RunSession.StartNew(Db, new MemorySaveStore(), "test", seed: 1);
        Assert.Empty(session.LastEvents);
        Prepare(session.State);
        session.Do(s => s.Refit.StartFight(s.State));

        var events = session.LastEvents;
        Assert.Equal(new FightStarted("dummy"), events[0]);
        Assert.Equal(new TurnStarted(1), events[1]);
        Assert.Equal(session.State.Combat!.Hand.Select(c => new CardDrawn(c.Id)), events.Skip(2));
    }

    [Fact]
    public void LastEventsHoldOnlyTheLastAction()
    {
        var session = RunSession.StartNew(Db, new MemorySaveStore(), "test", seed: 1);
        Prepare(session.State);
        session.Do(s => s.Refit.StartFight(s.State));
        var combat = session.State.Combat!;
        int ram = Take(combat, "ram");
        int brace = Take(combat, "brace");

        session.Do(s => s.Combat.PlayCard(s.State, ram));
        Assert.Equal([new CardPlayed(ram, "ram", BridgeId(session.State)), new EnemyHit(5, 0, 5, 25)], session.LastEvents);

        session.Do(s => s.Combat.PlayCard(s.State, brace));
        Assert.Equal([new CardPlayed(brace, "brace", BridgeId(session.State)), new BlockGained(5, 5)], session.LastEvents);
    }

    [Fact]
    public void ARejectedActionOrOneOutsideAFightLeavesNoEvents()
    {
        var session = RunSession.StartNew(Db, new MemorySaveStore(), "test", seed: 1);
        Prepare(session.State);
        session.Do(s => s.Refit.StartFight(s.State));
        var combat = session.State.Combat!;
        combat.Energy = 0;
        int ram = Take(combat, "ram");
        Assert.Throws<InvalidOperationException>(() => session.Do(s => s.Combat.PlayCard(s.State, ram)));
        Assert.Empty(session.LastEvents);

        combat.Energy = 1;
        combat.Enemy.Hull = 1;
        session.Do(s => s.Combat.PlayCard(s.State, ram));
        Assert.Equal(new FightEnded(CombatOutcome.Won), session.LastEvents[^1]);
        session.Do(s => s.Rewards.Skip(s.State));
        Assert.Empty(session.LastEvents);
    }
}
