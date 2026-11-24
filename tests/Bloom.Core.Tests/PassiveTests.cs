using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Core.Save;

namespace Bloom.Core.Tests;

/// <summary>
/// Passive pieces (owner, 2026-11-23: the game's items are pieces that take room on the ship). A passive takes
/// effect without a card: as a fight or a turn starts, all the time while its piece works, or when a fight is won.
/// </summary>
/// <remarks>
/// The content here is the test content of <see cref="TestContent"/> plus these pieces, all without cards unless
/// noted: "ward" (fight start: block 6, upgraded 9), "cell" (fight start: 2 energy), "scout" (fight start: draw 2),
/// "moss" (each turn: block 3, upgraded 5), "moss_bed" (the same passive on another piece), "muse" (each turn:
/// focus 2), "sentry" (each turn: 3 damage, upgraded 5), "stinger" (each turn: 4 damage, no upgrade), "wall" (lane
/// armor 4, upgraded 6, three cells wide, and one Deflect card), "still" (fight won: 3 hull, upgraded 5), "pond"
/// (fight won: 4 hull) and "gunner" (a Weapon piece with a Lance card and each turn: 2 damage). The enemies "wisp"
/// (5 hull) and "mote" (3) attack for 1, the "twin" makes a heavy attack of 10 on lane 0 with its body and again
/// with its one part, and the "side" makes a heavy attack of 10 on lane 3.
/// The test ship's hull covers x 0 to 4 and y 0 to 4, with the bridge on (0,4) and (1,4).
/// </remarks>
public class PassiveTests
{
    private static readonly ContentDb Db = ContentDb.Create(
        [
            .. TestContent.Db.Pieces,
            Passive("ward", PassiveTrigger.FightStart, new BlockEffect(6), new BlockEffect(9)),
            Passive("cell", PassiveTrigger.FightStart, new GainEnergyEffect(2), shape: ["#"], kind: PieceKind.Crew),
            Passive("scout", PassiveTrigger.FightStart, new DrawEffect(2), shape: ["#"], kind: PieceKind.Crew),
            Passive("moss", PassiveTrigger.TurnStart, new BlockEffect(3), new BlockEffect(5)),
            Passive("moss_bed", PassiveTrigger.TurnStart, new BlockEffect(3), new BlockEffect(5), passive: "moss"),
            Passive("muse", PassiveTrigger.TurnStart, new FocusEffect(2), shape: ["#"], kind: PieceKind.Crew),
            Passive("sentry", PassiveTrigger.TurnStart, new DamageEffect(3), new DamageEffect(5)),
            Passive("stinger", PassiveTrigger.TurnStart, new DamageEffect(4)),
            Passive("wall", PassiveTrigger.Standing, new LaneArmorEffect(4), new LaneArmorEffect(6), shape: ["###"], cards: [new CardGrant("deflect", 1)]),
            Passive("still", PassiveTrigger.FightWon, new RegainHullEffect(3), new RegainHullEffect(5)),
            Passive("pond", PassiveTrigger.FightWon, new RegainHullEffect(4)),
            Passive("gunner", PassiveTrigger.TurnStart, new DamageEffect(2), cards: [new CardGrant("lance", 1)], tags: [Tag.Weapon]),
        ],
        TestContent.Db.Cards,
        [
            .. TestContent.Db.Enemies,
            new EnemyDef("wisp", "Wisp", 5, [new Intent(IntentKind.Attack, 1)], EnemyPool.Early),
            new EnemyDef("mote", "Mote", 3, [new Intent(IntentKind.Attack, 1)], EnemyPool.Early),
            new EnemyDef("twin", "Twin", 100, [new Intent(IntentKind.HeavyAttack, 10, 0)], EnemyPool.Late,
                Parts: [new EnemyPartDef("Claw", 20, [new Intent(IntentKind.HeavyAttack, 10, 0)])]),
            new EnemyDef("side", "Side", 100, [new Intent(IntentKind.HeavyAttack, 10, 3)], EnemyPool.Late),
        ],
        TestContent.Db.Ships,
        TestContent.Db.Sections,
        TestContent.Db.Events);

    private readonly List<CombatEvent> _log = [];
    private readonly CombatEngine _engine;

    public PassiveTests() => _engine = new CombatEngine(Db, _log.Add);

    private static PieceDef Passive(
        string id,
        PassiveTrigger? trigger,
        Effect effect,
        Effect? upgrade = null,
        string[]? shape = null,
        CardGrant[]? cards = null,
        Tag[]? tags = null,
        PieceKind kind = PieceKind.Equipment,
        Rarity rarity = Rarity.Common,
        string? passive = null) =>
        new(id, id, kind, tags ?? [], rarity, Shape.Parse(shape ?? ["##"]), cards ?? [], Passive: new PassiveDef(passive ?? id, trigger, effect, upgrade));

    private static RunState NewRun(string ship = "test") => RunFactory.NewRun(Db, ship, 1);

    private static PieceInstance Fit(RunState run, string defId, int x, int y, bool upgraded = false)
    {
        var piece = run.NewPiece(defId) with { Upgraded = upgraded };
        new ShipFitter(Db).Place(run.Ship, piece, new Cell(x, y), Rotation.R0);
        return piece;
    }

    /// <summary>Starts a fight and forgets the events of its start.</summary>
    private CombatState Start(RunState run, string enemyId = "dummy")
    {
        var combat = _engine.StartFight(run, enemyId);
        _log.Clear();
        return combat;
    }

    private static IEnumerable<string> Active(RunState run, params int[] disabled) =>
        new ShipFitter(Db).ActivePassives(run.Ship, disabled).Select(p => p.Fitted.Piece.DefId);

    // Which passives count

    [Fact]
    public void PassivesTakeEffectFromTheFrontRowBackAndFromTheLeftWhateverTheOrderOfFitting()
    {
        var run = NewRun();
        Fit(run, "still", 3, 3);
        Fit(run, "sentry", 2, 1);
        Fit(run, "ward", 3, 0);
        Fit(run, "moss", 0, 1);
        Fit(run, "cell", 0, 0);
        Assert.Equal(["cell", "ward", "moss", "sentry", "still"], Active(run));
    }

    [Fact]
    public void APieceWithoutAPassiveHasNone() => Assert.Empty(Active(NewRun()));

    [Fact]
    public void AShipCountsAPassiveOfAGivenNameOnce()
    {
        var run = NewRun();
        var back = Fit(run, "moss", 0, 2);
        var front = Fit(run, "moss", 2, 0);
        Fit(run, "moss_bed", 0, 0);
        var active = new ShipFitter(Db).ActivePassives(run.Ship, []);
        Assert.Equal("moss_bed", Assert.Single(active).Fitted.Piece.DefId);

        var combat = Start(run);
        Assert.Equal(3, combat.Block);
        Assert.NotEqual(back.Id, front.Id);
    }

    [Fact]
    public void WhenThePieceThatCountsIsDisabledTheNextOneOfTheNameCounts()
    {
        var run = NewRun();
        var front = Fit(run, "moss", 0, 0);
        var back = Fit(run, "moss", 0, 2);
        var fitter = new ShipFitter(Db);
        Assert.Equal(front.Id, Assert.Single(fitter.ActivePassives(run.Ship, [])).Fitted.Piece.Id);
        Assert.Equal(back.Id, Assert.Single(fitter.ActivePassives(run.Ship, [front.Id])).Fitted.Piece.Id);
        Assert.Empty(fitter.ActivePassives(run.Ship, [front.Id, back.Id]));
    }

    [Fact]
    public void AnUpgradedPieceUsesTheUpgradeOfItsPassive()
    {
        var run = NewRun();
        Fit(run, "moss", 0, 0, upgraded: true);
        Fit(run, "stinger", 2, 0, upgraded: true);
        var active = new ShipFitter(Db).ActivePassives(run.Ship, []);
        Assert.Equal([new BlockEffect(5), new DamageEffect(4)], active.Select(p => p.Effect));
        Assert.Equal(5, Start(run).Block);
    }

    [Fact]
    public void APieceWithoutCardsReceivesNoBonus()
    {
        var run = NewRun();
        var gunner = Fit(run, "gunner", 0, 0);
        Fit(run, "grinder", 0, 1);
        var fitter = new ShipFitter(Db);
        Assert.Equal(gunner.Id, Assert.Single(fitter.ActiveBonuses(run.Ship, [])).ReceiverId);

        // The same bonus beside a piece that carries the tag but adds no cards has nothing to change.
        var bare = Db.Piece("sentry") with { Id = "bare", Name = "Bare", Tags = [Tag.Weapon] };
        var db = ContentDb.Create([.. Db.Pieces, bare], Db.Cards, Db.Enemies, Db.Ships, Db.Sections, Db.Events);
        var other = RunFactory.NewRun(db, "test", 1);
        var placer = new ShipFitter(db);
        placer.Place(other.Ship, other.NewPiece("bare"), new Cell(0, 0), Rotation.R0);
        placer.Place(other.Ship, other.NewPiece("grinder"), new Cell(0, 1), Rotation.R0);
        Assert.Empty(placer.ActiveBonuses(other.Ship, []));
    }

    // The start of a fight and of a turn

    [Fact]
    public void AFightStartPassiveFiresOnTheFirstTurnOnly()
    {
        var run = NewRun();
        Fit(run, "cell", 0, 0);
        var combat = _engine.StartFight(run, "dummy");
        Assert.Equal(Rules.EnergyPerTurn + 2, combat.Energy);
        _engine.EndTurn(run);
        Assert.Equal(Rules.EnergyPerTurn, combat.Energy);
    }

    [Fact]
    public void ATurnStartPassiveFiresEveryTurnTheFirstOneIncluded()
    {
        var run = NewRun();
        Fit(run, "moss", 0, 0);
        var combat = _engine.StartFight(run, "dummy");
        Assert.Equal(3, combat.Block);
        _engine.EndTurn(run);

        // The dummy's attack of 6 took the 3 block and 3 hull, then block returned to 0 and the passive gave 3 again.
        Assert.Equal(57, run.Hull);
        Assert.Equal(3, combat.Block);
        _engine.EndTurn(run);
        Assert.Equal(54, run.Hull);
    }

    [Fact]
    public void ATurnStartsWithItsEnergyThenTheFightStartPassivesThenTheTurnStartOnesThenTheDraw()
    {
        var run = NewRun();
        var moss = Fit(run, "moss", 0, 0);
        var ward = Fit(run, "ward", 2, 0);
        _engine.StartFight(run, "dummy");
        Assert.Equal(
            [
                new FightStarted("dummy"),
                new TurnStarted(1),
                new PassiveFired(ward.Id, PassiveTrigger.FightStart),
                new BlockGained(6, 6),
                new PassiveFired(moss.Id, PassiveTrigger.TurnStart),
                new BlockGained(3, 9),
            ],
            _log.Take(6));
        Assert.All(_log.Skip(6), e => Assert.IsType<CardDrawn>(e));
        Assert.Equal(4, _log.Count - 6);
    }

    [Fact]
    public void ALaterTurnReportsOnlyTheTurnStartPassives()
    {
        var run = NewRun();
        var moss = Fit(run, "moss", 0, 0);
        Fit(run, "ward", 2, 0);
        Start(run);
        _engine.EndTurn(run);
        int turn = _log.FindIndex(e => e is TurnStarted);
        Assert.Equal(
            [new TurnStarted(2), new PassiveFired(moss.Id, PassiveTrigger.TurnStart), new BlockGained(3, 3)],
            _log.Skip(turn).Take(3));
        Assert.Single(_log.OfType<PassiveFired>());
    }

    [Fact]
    public void ADisabledPiecesPassiveDoesNotFireUntilThePieceIsRepaired()
    {
        var run = NewRun();
        var moss = Fit(run, "moss", 0, 0);
        Fit(run, "tinker", 4, 0);

        // The mixed enemy attacks for 6, then makes a heavy attack of 20 on lane 0, then blocks.
        var combat = Start(run, "mixed");
        _engine.EndTurn(run);
        Assert.Equal(3, combat.Block);
        _engine.EndTurn(run);
        Assert.Equal([moss.Id], combat.DisabledPieceIds);
        Assert.Equal(0, combat.Block);
        Assert.DoesNotContain(_log.Skip(_log.FindLastIndex(e => e is TurnStarted)), e => e is PassiveFired);

        _engine.PlayCard(run, TestContent.Take(combat, "patch"), [moss.Id]);
        Assert.Equal(0, combat.Block);
        _engine.EndTurn(run);
        Assert.Equal(3, combat.Block);
        Assert.Equal(new PassiveFired(moss.Id, PassiveTrigger.TurnStart), _log[_log.FindLastIndex(e => e is TurnStarted) + 1]);
    }

    [Fact]
    public void AFightStartPassiveAddsToTheEnergyThatAShipKeeps()
    {
        var run = NewRun("solar");
        Fit(run, "cell", 0, 2);
        var combat = _engine.StartFight(run, "dummy");
        Assert.Equal(Rules.EnergyPerTurn + 2, combat.Energy);
        _engine.EndTurn(run);
        Assert.Equal((Rules.EnergyPerTurn * 2) + 2, combat.Energy);
    }

    [Fact]
    public void APassiveDrawsCardsOnTopOfTheTurnsDraw()
    {
        var run = NewRun();
        Fit(run, "lance", 0, 0);
        Fit(run, "plating", 0, 1);
        Fit(run, "garden", 0, 2);
        Fit(run, "scout", 4, 4);
        var combat = _engine.StartFight(run, "dummy");
        Assert.Equal(Rules.CardsDrawnPerTurn + 2, combat.Hand.Count);
        Assert.Equal(10 - combat.Hand.Count, combat.DrawPile.Count);
        _engine.EndTurn(run);
        Assert.Equal(Rules.CardsDrawnPerTurn, combat.Hand.Count);
    }

    [Fact]
    public void APassivesFocusWaitsForTheNextAttackAndAddsUp()
    {
        var run = NewRun();
        Fit(run, "muse", 0, 0);
        var combat = _engine.StartFight(run, "dummy");
        Assert.Equal(2, combat.PendingFocus);
        _engine.EndTurn(run);
        Assert.Equal(4, combat.PendingFocus);
        _engine.PlayCard(run, TestContent.Take(combat, "ram"));
        Assert.Equal(30 - 5 - 4, combat.Enemy.Hull);
        Assert.Equal(0, combat.PendingFocus);
    }

    // Damage from a passive

    [Fact]
    public void APassivesDamageHitsTheEnemyAndItsBlockAbsorbsItFirst()
    {
        var run = NewRun();
        var sentry = Fit(run, "sentry", 0, 0);
        var combat = _engine.StartFight(run, "blocker");
        Assert.Equal(27, combat.Enemy.Hull);
        _log.Clear();
        _engine.EndTurn(run);

        // The blocker raised 5 block, which lasts until its next turn and takes the 3 damage.
        Assert.Equal(27, combat.Enemy.Hull);
        Assert.Equal(2, combat.Enemy.Block);
        Assert.Contains(new PassiveFired(sentry.Id, PassiveTrigger.TurnStart), _log);
        Assert.Contains(new EnemyHit(3, 3, 0, 27), _log);
    }

    [Fact]
    public void APassivesDamageIsNotRaisedByABonusOrByFocusAndUsesUpNoFocus()
    {
        var run = NewRun();
        Fit(run, "muse", 4, 0);
        Fit(run, "gunner", 0, 0);
        Fit(run, "grinder", 0, 1);
        var combat = _engine.StartFight(run, "dummy");

        // The gunner's first cell is in front of the muse's on the left, so its 2 damage comes before the focus.
        // The second turn finds 2 focus waiting and still deals 2.
        Assert.Equal(28, combat.Enemy.Hull);
        Assert.Equal(2, combat.PendingFocus);
        _engine.EndTurn(run);
        Assert.Equal(26, combat.Enemy.Hull);
        Assert.Equal(4, combat.PendingFocus);

        // The gunner's card does get the grinder's bonus and the focus.
        _engine.PlayCard(run, TestContent.Take(combat, "lance"));
        Assert.Equal(26 - 12 - 2 - 4, combat.Enemy.Hull);
    }

    [Fact]
    public void APassivesDamageHitsThePartThatThePlayerAimsAt()
    {
        var run = NewRun();
        Fit(run, "sentry", 2, 0);
        var combat = _engine.StartFight(run, "titan");
        Assert.Equal(37, combat.Enemy.Hull);
        _engine.SetTarget(run, 0);
        _log.Clear();
        _engine.EndTurn(run);

        // The titan's shell raised 4 block for the whole enemy, so the arm's hull is untouched, but it was the arm that was hit.
        Assert.Contains(new EnemyHit(3, 3, 0, 10, 0), _log);
        Assert.Equal(37, combat.Enemy.Hull);
    }

    [Fact]
    public void APassiveThatDisablesTheAimedPartSendsTheNextPassiveToTheBody()
    {
        var run = NewRun();
        Fit(run, "sentry", 2, 0);
        Fit(run, "stinger", 2, 1);
        var combat = _engine.StartFight(run, "titan");
        Assert.Equal(40 - 3 - 4, combat.Enemy.Hull);

        // The shell is taken out so that it raises no block, and the arm is left with 2 hull.
        combat.Enemy.Parts[1].Hull = 0;
        combat.Enemy.Parts[0].Hull = 2;
        _engine.SetTarget(run, 0);
        _log.Clear();
        _engine.EndTurn(run);

        int turn = _log.FindIndex(e => e is TurnStarted);
        Assert.Equal(
            [new EnemyHit(3, 0, 3, 0, 0), new PartDisabled(0), new EnemyHit(4, 0, 4, 29)],
            _log.Skip(turn).Where(e => e is EnemyHit or PartDisabled));
        Assert.Null(combat.Target);
        Assert.Equal(29, combat.Enemy.Hull);
    }

    [Fact]
    public void AShipLostInTheEnemysTurnIsLostEvenIfItsPassivesWouldHaveWonTheNextTurn()
    {
        var run = NewRun();
        Fit(run, "sentry", 2, 0);
        var combat = _engine.StartFight(run, "dummy");
        combat.Enemy.Hull = 1;
        run.Hull = 1;
        _log.Clear();

        Assert.Equal(CombatOutcome.Lost, _engine.EndTurn(run));
        Assert.Equal(RunResult.Lost, run.Result);
        Assert.Equal(1, combat.Enemy.Hull);
        Assert.DoesNotContain(_log, e => e is PassiveFired or TurnStarted);
    }

    [Fact]
    public void AFightCanBeWonAsATurnStarts()
    {
        var run = NewRun();
        var sentry = Fit(run, "sentry", 0, 0);
        var combat = _engine.StartFight(run, "wisp");
        Assert.Equal(2, combat.Enemy.Hull);
        _log.Clear();

        Assert.Equal(CombatOutcome.Won, _engine.EndTurn(run));
        Assert.Null(run.Combat);
        Assert.Equal(RunPhase.Reward, run.Phase);
        Assert.Equal(1, run.FightsWon);
        Assert.Equal(new FightRecord("wisp", CombatOutcome.Won, 2), run.LastFight);
        int turn = _log.FindIndex(e => e is TurnStarted);
        Assert.Equal(
            [new TurnStarted(2), new PassiveFired(sentry.Id, PassiveTrigger.TurnStart), new EnemyHit(3, 0, 3, 0), new FightEnded(CombatOutcome.Won)],
            _log.Skip(turn));
    }

    [Fact]
    public void AFightCanBeWonAsItStartsAndTheNextPassivesDoNotFire()
    {
        var run = TestContent.Prepare(NewRun(), "mote");
        var sentry = Fit(run, "sentry", 0, 0);
        Fit(run, "stinger", 2, 0);
        var combat = new RefitEngine(Db, _engine).StartFight(run);

        Assert.Equal(0, combat.Enemy.Hull);
        Assert.Null(run.Combat);
        Assert.Null(run.Encounter);
        Assert.Equal(RunPhase.Reward, run.Phase);
        Assert.Equal(new FightRecord("mote", CombatOutcome.Won, 1), run.LastFight);
        Assert.Equal(
            [new FightStarted("mote"), new TurnStarted(1), new PassiveFired(sentry.Id, PassiveTrigger.TurnStart), new EnemyHit(3, 0, 3, 0), new FightEnded(CombatOutcome.Won)],
            _log);
    }

    [Fact]
    public void ARunWonAsAFightStartsIsSavedAndResumes()
    {
        var session = new RunSession(Db, TestContent.Prepare(NewRun(), "mote"), new MemorySaveStore());
        Fit(session.State, "sentry", 0, 0);
        session.Do(s => s.Refit.StartFight(s.State));
        Assert.Equal(RunPhase.Reward, session.State.Phase);
        Assert.Contains(session.LastEvents, e => e is FightEnded { Outcome: CombatOutcome.Won });
        var restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(session.State));
        Assert.Equal(RunPhase.Reward, restored.Phase);
        Assert.Equal(session.State.Reward!.Pieces, restored.Reward!.Pieces);
    }

    // Lane armor

    [Fact]
    public void LaneArmorTakesItsAmountOffAHeavyAttackThatReachesThePiece()
    {
        var run = NewRun();
        var wall = Fit(run, "wall", 0, 0);
        var combat = Start(run, "heavy");
        _engine.EndTurn(run);

        Assert.Equal(60 - 6, run.Hull);
        Assert.Equal([wall.Id], combat.DisabledPieceIds);
        int fired = _log.IndexOf(new PassiveFired(wall.Id, PassiveTrigger.Standing));
        Assert.True(fired >= 0);
        Assert.Equal(new ShipHit(6, 0, 6, 54, 0, null, 4), _log[fired + 1]);
        Assert.Equal(new PieceDisabled(wall.Id, 0), _log[fired + 2]);
    }

    [Fact]
    public void ADisabledPiecesArmorIsOff()
    {
        var run = NewRun();
        Fit(run, "wall", 0, 0);
        Start(run, "heavy");
        _engine.EndTurn(run);
        _log.Clear();
        _engine.EndTurn(run);

        // The second heavy attack reaches the bridge behind the disabled wall, at its full 10.
        Assert.Equal(60 - 6 - 10, run.Hull);
        Assert.Contains(new ShipHit(10, 0, 10, 44, 0), _log);
        Assert.DoesNotContain(_log, e => e is PassiveFired { Trigger: PassiveTrigger.Standing });
    }

    [Fact]
    public void LaneArmorCountsOnlyWhileItsPieceIsTheFrontmostOfTheLane()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        Fit(run, "wall", 0, 1);
        var combat = Start(run, "heavy");
        Assert.Equal(new EnemyAction(null, new Intent(IntentKind.HeavyAttack, 10, 0), 0, run.Ship.Find(plating.Id)), Assert.Single(_engine.NextActions(run)));
        _engine.EndTurn(run);
        Assert.Equal(50, run.Hull);
        Assert.Equal([plating.Id], combat.DisabledPieceIds);
    }

    [Fact]
    public void LaneArmorDoesNothingAgainstANormalAttackOrAHeavyAttackOnAnotherLane()
    {
        var run = NewRun();
        Fit(run, "wall", 2, 0);
        Start(run, "heavy");
        _engine.EndTurn(run);
        Assert.Equal(50, run.Hull);

        var other = NewRun();
        Fit(other, "wall", 0, 0);
        Start(other, "dummy");
        _engine.EndTurn(other);
        Assert.Equal(54, other.Hull);
    }

    [Fact]
    public void BlockThatAbsorbsWhatTheArmorLeavesKeepsThePieceWorking()
    {
        var run = NewRun();
        Fit(run, "wall", 0, 0);
        var combat = Start(run, "heavy");
        _engine.PlayCard(run, TestContent.Take(combat, "deflect"));
        _engine.EndTurn(run);

        // 10 less 4 of armor is 6, and the Deflect's 8 block takes all of it. Without the armor 2 would get past.
        Assert.Equal(60, run.Hull);
        Assert.Empty(combat.DisabledPieceIds);
        Assert.Contains(new ShipHit(6, 6, 0, 60, 0, null, 4), _log);
    }

    [Fact]
    public void LaneArmorCannotTakeAHeavyAttackBelowZero()
    {
        var thick = Db.Piece("wall") with { Id = "thick", Name = "Thick", Passive = new PassiveDef("thick", PassiveTrigger.Standing, new LaneArmorEffect(25)) };
        var db = ContentDb.Create([.. Db.Pieces, thick], Db.Cards, Db.Enemies, Db.Ships, Db.Sections, Db.Events);
        var engine = new CombatEngine(db, _log.Add);
        var run = RunFactory.NewRun(db, "test", 1);
        var piece = run.NewPiece("thick");
        new ShipFitter(db).Place(run.Ship, piece, new Cell(0, 0), Rotation.R0);
        var combat = engine.StartFight(run, "heavy");

        var action = Assert.Single(engine.NextActions(run));
        Assert.Equal(0, action.Intent.Amount);
        Assert.Equal(10, action.Armor);
        engine.EndTurn(run);
        Assert.Equal(60, run.Hull);
        Assert.Empty(combat.DisabledPieceIds);
        Assert.Contains(new ShipHit(0, 0, 0, 60, 0, null, 10), _log);
    }

    [Fact]
    public void AnUpgradedPieceHasItsUpgradedArmor()
    {
        var run = NewRun();
        Fit(run, "wall", 0, 0, upgraded: true);
        Start(run, "heavy");
        _engine.EndTurn(run);
        Assert.Equal(60 - 4, run.Hull);
    }

    [Fact]
    public void TheNextActionsShowAHeavyAttackAsItWillLand()
    {
        var run = NewRun();
        var wall = Fit(run, "wall", 0, 0);
        Start(run, "heavy");
        var action = Assert.Single(_engine.NextActions(run));
        Assert.Equal(new Intent(IntentKind.HeavyAttack, 6, 0), action.Intent);
        Assert.Equal(4, action.Armor);
        Assert.Equal(0, action.Lane);
        Assert.Equal(wall.Id, action.Target!.Piece.Id);
    }

    [Fact]
    public void AMoveThatIsNotAHeavyAttackComesBackAsItIs()
    {
        var run = NewRun();
        Fit(run, "wall", 0, 0);
        var attack = new Intent(IntentKind.Attack, 7);
        Assert.Equal(new EnemyAction(2, attack), _engine.EffectiveIntent(run.Ship, attack, [], 2));
        var block = new Intent(IntentKind.Block, 5);
        Assert.Equal(new EnemyAction(null, block), _engine.EffectiveIntent(run.Ship, block, []));
    }

    [Fact]
    public void AnEffectiveIntentNeedsNoFightSoTheFittingScreenCanShowItBeforeOne()
    {
        var run = TestContent.Prepare(NewRun(), "heavy");
        var wall = Fit(run, "wall", 0, 0);
        var heavy = Db.Enemy("heavy").Pattern[0];
        var action = _engine.EffectiveIntent(run.Ship, heavy, []);
        Assert.Equal((6, 4, 0, wall.Id), (action.Intent.Amount, action.Armor, action.Lane!.Value, action.Target!.Piece.Id));

        // With the wall out of the lane the attack lands on the bridge at its full amount.
        new ShipFitter(Db).Move(run.Ship, wall.Id, new Cell(2, 0), Rotation.R0);
        action = _engine.EffectiveIntent(run.Ship, heavy, []);
        int bridge = run.Ship.Fitted.Single(f => Db.Piece(f.Piece.DefId).IsBridge).Piece.Id;
        Assert.Equal((10, 0, bridge), (action.Intent.Amount, action.Armor, action.Target!.Piece.Id));
    }

    [Fact]
    public void ASecondHeavyAttackOnTheLaneReachesTheNextPieceWhoseArmorThenCounts()
    {
        var run = NewRun();
        var front = Fit(run, "wall", 0, 0);
        var back = Fit(run, "wall", 0, 1);
        var combat = Start(run, "twin");

        // Only the front wall's armor counts while it works. The body's attack disables it, so the part's attack
        // reaches the wall behind it, whose armor counts from then on.
        var actions = _engine.NextActions(run);
        Assert.Equal([(front.Id, 6, 4), (back.Id, 6, 4)], actions.Select(a => (a.Target!.Piece.Id, a.Intent.Amount, a.Armor)));
        _engine.EndTurn(run);
        Assert.Equal(60 - 12, run.Hull);
        Assert.Equal([front.Id, back.Id], combat.DisabledPieceIds);
        Assert.Equal(2, _log.Count(e => e is PassiveFired { Trigger: PassiveTrigger.Standing }));
    }

    [Fact]
    public void ArmorCountsOncePerShipSoASecondArmoredPieceOnAnotherLaneTakesTheFullAttack()
    {
        var run = NewRun();
        Fit(run, "wall", 0, 0);
        var second = Fit(run, "wall", 2, 1);
        var combat = Start(run, "side");

        // The first wall, nearer the front and the left, is the one whose armor counts. The attack on lane 3
        // reaches the second wall, which has none while the first one works.
        var action = Assert.Single(_engine.NextActions(run));
        Assert.Equal((second.Id, 10, 0), (action.Target!.Piece.Id, action.Intent.Amount, action.Armor));
        _engine.EndTurn(run);
        Assert.Equal(50, run.Hull);
        Assert.Equal([second.Id], combat.DisabledPieceIds);
        Assert.DoesNotContain(_log, e => e is PassiveFired { Trigger: PassiveTrigger.Standing });
    }

    /// <summary>
    /// The enemy's turn is shown as it would go if the turn ended now. When block takes the whole of the first heavy
    /// attack, its piece keeps working, so the second heavy attack on the lane reaches the same piece again.
    /// </summary>
    [Fact]
    public void TheNextActionsFollowTheBlockSoAHeavyAttackThatIsBlockedLeavesItsPieceInFront()
    {
        var run = NewRun();
        var front = Fit(run, "wall", 0, 0);
        var back = Fit(run, "wall", 0, 1);
        var combat = Start(run, "twin");

        // Without block the first attack gets through, so the second is shown on the wall behind.
        Assert.Equal([front.Id, back.Id], _engine.NextActions(run).Select(a => a.Target!.Piece.Id));

        // With 8 block: the first attack lands for 6 and is absorbed, and the second lands on the same wall for 6,
        // of which 2 block absorb the start. That is what the turn then does.
        _engine.PlayCard(run, TestContent.Take(combat, "deflect"));
        var actions = _engine.NextActions(run);
        Assert.Equal([(front.Id, 6, 4), (front.Id, 6, 4)], actions.Select(a => (a.Target!.Piece.Id, a.Intent.Amount, a.Armor)));
        _log.Clear();
        _engine.EndTurn(run);
        Assert.Equal(
            [new ShipHit(6, 6, 0, 60, 0, null, 4), new ShipHit(6, 2, 4, 56, 0, 0, 4)],
            _log.OfType<ShipHit>());
        Assert.Equal([front.Id], combat.DisabledPieceIds);
    }

    /// <summary>The same when the armor takes the whole attack: the piece keeps working with no block at all.</summary>
    [Fact]
    public void TheNextActionsLeaveAPieceInFrontWhenItsArmorTakesTheWholeAttack()
    {
        var thick = Db.Piece("wall") with { Id = "thick", Name = "Thick", Passive = new PassiveDef("thick", PassiveTrigger.Standing, new LaneArmorEffect(25)) };
        var db = ContentDb.Create([.. Db.Pieces, thick], Db.Cards, Db.Enemies, Db.Ships, Db.Sections, Db.Events);
        var engine = new CombatEngine(db, _log.Add);
        var run = RunFactory.NewRun(db, "test", 1);
        var piece = run.NewPiece("thick");
        new ShipFitter(db).Place(run.Ship, piece, new Cell(0, 0), Rotation.R0);
        var combat = engine.StartFight(run, "twin");

        Assert.Equal([(piece.Id, 0), (piece.Id, 0)], engine.NextActions(run).Select(a => (a.Target!.Piece.Id, a.Intent.Amount)));
        engine.EndTurn(run);
        Assert.Equal(60, run.Hull);
        Assert.Empty(combat.DisabledPieceIds);
    }

    // A won fight

    private RunState Win(RunState run, string enemyId = "dummy")
    {
        var combat = _engine.StartFight(run, enemyId);
        combat.Enemy.Hull = 1;
        _log.Clear();
        _engine.PlayCard(run, TestContent.Take(combat, "ram"));
        return run;
    }

    [Fact]
    public void AWonFightGivesBackHullAndTheRewardShowsIt()
    {
        var run = NewRun();
        var still = Fit(run, "still", 0, 0);
        run.Hull = 40;
        Win(run);
        Assert.Equal(43, run.Hull);
        Assert.Equal(3, run.Reward!.HullRegained);
        Assert.Equal(
            [new PassiveFired(still.Id, PassiveTrigger.FightWon), new HullRegained(3, 43), new FightEnded(CombatOutcome.Won)],
            _log.TakeLast(3));
    }

    [Theory]
    [InlineData(58, 60, 2)]
    [InlineData(60, 60, 0)]
    public void HullIsNotRegainedPastItsMaximum(int hull, int after, int regained)
    {
        var run = NewRun();
        Fit(run, "still", 0, 0);
        run.Hull = hull;
        Win(run);
        Assert.Equal(after, run.Hull);
        Assert.Equal(regained, run.Reward!.HullRegained);
        Assert.Equal(regained > 0, _log.Any(e => e is PassiveFired));
    }

    [Theory]
    [InlineData(40, 47, 7, 2)]
    [InlineData(58, 60, 2, 1)]
    public void TwoPassivesOfDifferentNamesAddUpToTheMaximumHull(int hull, int after, int regained, int fired)
    {
        var run = NewRun();
        Fit(run, "still", 0, 0);
        Fit(run, "pond", 2, 0);
        run.Hull = hull;
        Win(run);
        Assert.Equal(after, run.Hull);
        Assert.Equal(regained, run.Reward!.HullRegained);
        Assert.Equal(fired, _log.Count(e => e is PassiveFired { Trigger: PassiveTrigger.FightWon }));
        Assert.Equal(regained, _log.OfType<HullRegained>().Sum(e => e.Amount));
    }

    [Fact]
    public void AWonFightWithoutSuchAPassiveGivesBackNothing()
    {
        var run = NewRun();
        run.Hull = 40;
        Win(run);
        Assert.Equal(40, run.Hull);
        Assert.Equal(0, run.Reward!.HullRegained);
    }

    [Fact]
    public void ADisabledPieceGivesBackNoHull()
    {
        var run = NewRun();
        Fit(run, "still", 0, 0);
        var combat = _engine.StartFight(run, "heavy");
        _engine.EndTurn(run);
        Assert.Single(combat.DisabledPieceIds);
        int hull = run.Hull;
        combat.Enemy.Hull = 1;
        _engine.PlayCard(run, TestContent.Take(combat, "ram"));
        Assert.Equal(hull, run.Hull);
        Assert.Equal(0, run.Reward!.HullRegained);
    }

    [Fact]
    public void ALostFightGivesBackNoHull()
    {
        var run = NewRun();
        Fit(run, "still", 2, 0);
        _engine.StartFight(run, "dummy");
        run.Hull = 1;
        Assert.Equal(CombatOutcome.Lost, _engine.EndTurn(run));
        Assert.Equal(0, run.Hull);
        Assert.DoesNotContain(_log, e => e is HullRegained);
    }

    [Fact]
    public void TheFightThatWinsTheRunAlsoGivesBackHull()
    {
        var run = NewRun();
        Fit(run, "still", 0, 0);
        run.Hull = 40;
        Win(run, "titan");
        Assert.Equal(RunResult.Won, run.Result);
        Assert.Null(run.Reward);
        Assert.Equal(43, run.Hull);
    }

    [Fact]
    public void ASaveKeepsTheHullThatARewardShows()
    {
        var run = NewRun();
        Fit(run, "still", 0, 0);
        run.Hull = 40;
        Win(run);
        var restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(run));
        Assert.Equal(3, restored.Reward!.HullRegained);
        Assert.Equal(43, restored.Hull);
    }

    // Rewards and upgrades

    [Fact]
    public void AnOfferHoldsAtMostOnePieceWithoutCards()
    {
        var rewards = new RewardEngine(Db);
        int withOne = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var run = RunFactory.NewRun(Db, "test", seed);
            var offer = rewards.RollPieces(run, 6);
            Assert.Equal(6, offer.Count);
            int bare = offer.Count(id => Db.Piece(id).Cards.Count == 0);
            Assert.InRange(bare, 0, 1);
            withOne += bare;
        }

        // Ten of the test pieces that can be offered add no cards, so nearly every offer of 6 holds one.
        Assert.InRange(withOne, 250, 300);
    }

    [Fact]
    public void AnOfferIsShorterWhenTooFewPiecesWithCardsAreLeft()
    {
        // The bridge is never offered, so this leaves the lance and ten pieces without cards.
        var db = ContentDb.Create(
            [.. Db.Pieces.Where(p => p.IsBridge || p.Id == "lance" || p.Cards.Count == 0)], Db.Cards, Db.Enemies, Db.Ships, Db.Sections, TestContent.PlainEvents);
        var offer = new RewardEngine(db).RollPieces(RunFactory.NewRun(db, "test", 1), Rules.RewardChoices);
        Assert.Equal(2, offer.Count);
        Assert.Contains("lance", offer);
    }

    [Fact]
    public void APieceCanBeUpgradedForItsPassiveAlone()
    {
        var run = NewRun();
        var moss = Fit(run, "moss", 0, 0);
        var stinger = Fit(run, "stinger", 2, 0);
        Assert.True(PieceUpgrades.CanUpgrade(Db, moss));
        Assert.False(PieceUpgrades.CanUpgrade(Db, stinger));
        var upgradable = PieceUpgrades.Upgradable(Db, run).Select(p => p.Id).ToList();
        Assert.Contains(moss.Id, upgradable);
        Assert.DoesNotContain(stinger.Id, upgradable);

        PieceUpgrades.Upgrade(Db, run, moss.Id);
        Assert.True(run.Ship.Find(moss.Id)!.Piece.Upgraded);
        Assert.Equal(5, Start(run).Block);
        var e = Assert.Throws<InvalidOperationException>(() => PieceUpgrades.Upgrade(Db, run, stinger.Id));
        Assert.Contains("no card and no passive that can be upgraded", e.Message);
    }

    [Fact]
    public void APieceWithoutCardsAddsNoneToTheDeck()
    {
        var run = NewRun();
        Fit(run, "moss", 0, 0);
        Fit(run, "wall", 0, 1);
        var deck = DeckBuilder.Build(run.Ship, Db);
        Assert.Equal(["ram", "ram", "brace", "brace", "deflect"], deck.Select(c => c.CardId));
    }

    // What the content may hold

    private static void AssertInvalid(string expected, params PieceDef[] pieces)
    {
        var e = Assert.Throws<ContentException>(() =>
            ContentDb.Create([.. TestContent.Db.Pieces, .. pieces], TestContent.Db.Cards, TestContent.Db.Enemies, TestContent.Db.Ships, TestContent.Db.Sections, TestContent.Db.Events));
        Assert.Contains(e.Errors, error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void APieceWithoutCardsNeedsAPassive() =>
        AssertInvalid("piece 'bare': must add at least one card or have a passive.", Db.Piece("moss") with { Id = "bare", Passive = null });

    [Fact]
    public void APieceNeedsItsListOfCardsEvenWhenItIsEmpty() =>
        AssertInvalid("piece 'bare': missing cards (use [] for a piece that has only a passive).", Db.Piece("moss") with { Id = "bare", Cards = null! });

    [Fact]
    public void APassiveNeedsAName() =>
        AssertInvalid("piece 'x': the passive needs a name.", Passive("x", PassiveTrigger.TurnStart, new BlockEffect(1), passive: " "));

    [Fact]
    public void APassiveNeedsATrigger()
    {
        AssertInvalid("piece 'x': the passive needs a trigger: FightStart, TurnStart, Standing, FightWon.", Passive("x", null, new BlockEffect(1)));
        AssertInvalid("piece 'x': the passive needs a trigger", Passive("x", (PassiveTrigger)9, new BlockEffect(1)));
    }

    [Fact]
    public void APassiveNeedsAnEffect() =>
        AssertInvalid("piece 'x': the passive needs an effect.", Passive("x", PassiveTrigger.TurnStart, null!));

    public static TheoryData<PassiveTrigger, Effect> WrongPairs => new()
    {
        { PassiveTrigger.FightStart, new LaneArmorEffect(2) },
        { PassiveTrigger.FightStart, new RegainHullEffect(2) },
        { PassiveTrigger.TurnStart, new RepairEffect(1) },
        { PassiveTrigger.TurnStart, new GrowEffect(1) },
        { PassiveTrigger.TurnStart, new DamageBonusEffect(1) },
        { PassiveTrigger.Standing, new BlockEffect(2) },
        { PassiveTrigger.Standing, new RegainHullEffect(2) },
        { PassiveTrigger.FightWon, new DamageEffect(2) },
        { PassiveTrigger.FightWon, new LaneArmorEffect(2) },
    };

    [Theory]
    [MemberData(nameof(WrongPairs))]
    public void ATriggerTakesOnlyItsOwnEffects(PassiveTrigger trigger, Effect effect) =>
        AssertInvalid($"piece 'x': the passive cannot be '{effect.GetType().Name}' with the trigger {trigger}.", Passive("x", trigger, effect));

    public static TheoryData<PassiveTrigger, Effect> RightPairs => new()
    {
        { PassiveTrigger.FightStart, new BlockEffect(2) },
        { PassiveTrigger.FightStart, new GainEnergyEffect(2) },
        { PassiveTrigger.FightStart, new DrawEffect(2) },
        { PassiveTrigger.FightStart, new FocusEffect(2) },
        { PassiveTrigger.FightStart, new DamageEffect(2) },
        { PassiveTrigger.TurnStart, new BlockEffect(2) },
        { PassiveTrigger.TurnStart, new GainEnergyEffect(2) },
        { PassiveTrigger.TurnStart, new DrawEffect(2) },
        { PassiveTrigger.TurnStart, new FocusEffect(2) },
        { PassiveTrigger.TurnStart, new DamageEffect(2) },
        { PassiveTrigger.Standing, new LaneArmorEffect(2) },
        { PassiveTrigger.FightWon, new RegainHullEffect(2) },
    };

    /// <summary>Every pair that the validator takes also plays: a fight with it starts, a turn ends and the fight is won.</summary>
    [Theory]
    [MemberData(nameof(RightPairs))]
    public void EveryPairThatLoadsAlsoPlays(PassiveTrigger trigger, Effect effect)
    {
        var piece = Passive("x", trigger, effect, shape: ["###"]);
        var db = ContentDb.Create([.. Db.Pieces, piece], Db.Cards, Db.Enemies, Db.Ships, Db.Sections, Db.Events);
        var engine = new CombatEngine(db);
        var run = RunFactory.NewRun(db, "test", 1);
        new ShipFitter(db).Place(run.Ship, run.NewPiece("x"), new Cell(0, 0), Rotation.R0);

        // The mixed enemy attacks for 6, then makes a heavy attack of 20 on lane 0, where the piece is.
        var combat = engine.StartFight(run, "mixed");
        engine.EndTurn(run);
        engine.NextActions(run);
        engine.EndTurn(run);
        combat.Enemy.Hull = 1;
        Assert.Equal(CombatOutcome.Won, engine.PlayCard(run, TestContent.Take(combat, "ram")));
    }

    [Fact]
    public void APassiveCannotHaveANegativeAmount()
    {
        AssertInvalid("piece 'x': the passive cannot have a negative amount.", Passive("x", PassiveTrigger.TurnStart, new BlockEffect(-1)));
        AssertInvalid("piece 'x': the passive's upgrade cannot have a negative amount.", Passive("x", PassiveTrigger.TurnStart, new BlockEffect(1), new BlockEffect(-2)));
    }

    [Fact]
    public void APassivesUpgradeMustBeTheSameKindOfEffect()
    {
        AssertInvalid(
            "piece 'x': the passive's upgrade must be the same kind of effect as the passive.",
            Passive("x", PassiveTrigger.TurnStart, new BlockEffect(1), new DamageEffect(2)));
        AssertInvalid(
            "piece 'x': the passive's upgrade cannot be 'LaneArmorEffect' with the trigger TurnStart.",
            Passive("x", PassiveTrigger.TurnStart, new BlockEffect(1), new LaneArmorEffect(2)));
    }

    [Theory]
    [InlineData("laneArmor", "LaneArmorEffect")]
    [InlineData("regainHull", "RegainHullEffect")]
    public void AnEffectOfPassivesCannotBeOnACard(string type, string name)
    {
        string cards = $$"""[{ "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "type": "{{type}}", "amount": 2 }] }]""";
        var e = Assert.Throws<ContentException>(() => ContentDb.FromJson(OnePiece(""), cards, FourEnemies, OneShip, TestContent.SectionsJson, TestContent.PlainEventsJson));
        Assert.Contains(e.Errors, error => error.Contains($"card 'ram': '{name}' can only be used in a passive.", StringComparison.Ordinal));
    }

    // Reading from JSON

    private const string OneCard = """[{ "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "type": "damage", "amount": 5 }] }]""";
    private const string FourEnemies = """
        [{ "id": "a", "name": "A", "pool": "Early", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
         { "id": "b", "name": "B", "pool": "Late", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
         { "id": "c", "name": "C", "pool": "Hard", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
         { "id": "d", "name": "D", "pool": "Boss", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] }]
        """;

    private const string OneShip = """[{ "id": "s", "name": "S", "hull": 60, "hullShape": ["####"], "hullOrigin": { "x": 0, "y": 0 }, "loadouts": [{ "id": "a", "name": "A", "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 0 } }] }] }]""";

    /// <summary>A bridge and, when <paramref name="still"/> is not empty, a second piece "still" written with that text after its shape.</summary>
    private static string OnePiece(string still) =>
        """[{ "id": "bridge", "name": "Bridge", "kind": "Equipment", "tags": ["Core"], "rarity": "Common", "shape": ["##"], "cards": [{ "card": "ram", "count": 2 }] }"""
        + (still.Length == 0 ? "" : $$""", { "id": "still", "name": "Still", "kind": "Equipment", "tags": ["Garden"], "rarity": "Common", "shape": ["##"], {{still}} }""")
        + "]";

    private static ContentDb Load(string still) =>
        ContentDb.FromJson(OnePiece(still), OneCard, FourEnemies, OneShip, TestContent.SectionsJson, TestContent.PlainEventsJson);

    [Fact]
    public void APassiveIsReadFromJson()
    {
        var db = Load("""
            "cards": [], "passive": { "name": "dew", "trigger": "FightWon", "effect": { "type": "regainHull", "amount": 3 }, "upgrade": { "type": "regainHull", "amount": 5 } }
            """);
        var still = db.Piece("still");
        Assert.Empty(still.Cards);
        Assert.Equal(new PassiveDef("dew", PassiveTrigger.FightWon, new RegainHullEffect(3), new RegainHullEffect(5)), still.Passive);

        var armor = Load("""
            "cards": [{ "card": "ram", "count": 1 }], "passive": { "name": "plates", "trigger": "Standing", "effect": { "type": "laneArmor", "amount": 4 } }
            """).Piece("still");
        Assert.Equal(new PassiveDef("plates", PassiveTrigger.Standing, new LaneArmorEffect(4)), armor.Passive);
        Assert.Null(db.Piece("bridge").Passive);
    }

    [Theory]
    [InlineData("""{ "name": "dew", "trigger": "FightWon", "efect": { "type": "regainHull", "amount": 3 } }""", "efect")]
    [InlineData("""{ "name": "dew", "trigger": "WhenWon", "effect": { "type": "regainHull", "amount": 3 } }""", "pieces.json")]
    [InlineData("""{ "name": "dew", "trigger": "FightWon", "effect": { "type": "healHull", "amount": 3 } }""", "pieces.json")]
    public void AMisspelledPassiveIsAnErrorThatNamesTheFile(string passive, string expected)
    {
        var e = Assert.Throws<ContentException>(() => Load($"\"cards\": [], \"passive\": {passive}"));
        Assert.Contains(e.Errors, error => error.Contains("pieces.json", StringComparison.Ordinal) && error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void APassiveWithoutATriggerInJsonIsReported()
    {
        var e = Assert.Throws<ContentException>(() => Load("""
            "cards": [], "passive": { "name": "dew", "effect": { "type": "regainHull", "amount": 3 } }
            """));
        Assert.Contains(e.Errors, error => error.Contains("piece 'still': the passive needs a trigger", StringComparison.Ordinal));
    }

    [Fact]
    public void APieceWithoutCardsOrAPassiveInJsonIsReported()
    {
        var e = Assert.Throws<ContentException>(() => Load("\"cards\": []"));
        Assert.Contains(e.Errors, error => error.Contains("piece 'still': must add at least one card or have a passive.", StringComparison.Ordinal));
    }
}
