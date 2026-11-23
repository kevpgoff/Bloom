using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

/// <summary>
/// A boss's parts (owner, 2026-11-22): the body and each working part act every turn with their own patterns, a
/// part whose hull reaches 0 stops acting, and the fight is won when the body's hull reaches 0. The player taps
/// the body or a part to aim, and every attack hits it until changed.
/// </summary>
/// <remarks>
/// The titan of <see cref="TestContent"/>: body 40 hull, attacks for 5. Part 0 "Arm" (10 hull) attacks for 3, part 1
/// "Shell" (8) blocks 4, and part 2 "Ram" (12) alternates a heavy attack of 4 on lane 0 with an attack of 2.
/// </remarks>
public class BossPartTests
{
    private readonly List<CombatEvent> _log = [];
    private readonly CombatEngine _engine;

    public BossPartTests() => _engine = new CombatEngine(Db, _log.Add);

    /// <summary>Starts a fight with the titan and forgets the events of its start.</summary>
    private CombatState Start(RunState run, CombatEngine? engine = null)
    {
        var combat = (engine ?? _engine).StartFight(run, "titan");
        _log.Clear();
        return combat;
    }

    [Fact]
    public void EachPartStartsWithItsHullAndTheAimOnTheBody()
    {
        var combat = Start(NewRun());
        Assert.Equal([10, 8, 12], combat.Enemy.Parts.Select(p => p.Hull));
        Assert.Equal([10, 8, 12], combat.Enemy.Parts.Select(p => p.MaxHull));
        Assert.Equal(40, combat.Enemy.Hull);
        Assert.Null(combat.Target);
    }

    [Fact]
    public void AnEnemyWithoutPartsHasNone() => Assert.Empty(_engine.StartFight(NewRun(), "dummy").Enemy.Parts);

    [Fact]
    public void TheBodyActsThenEveryWorkingPartInOrder()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        var combat = Start(run);
        _engine.EndTurn(run);

        Assert.Equal(
            [
                new ShipHit(5, 0, 5, 55, null),
                new ShipHit(3, 0, 3, 52, null, Part: 0),
                new EnemyBlocked(4, 4, Part: 1),
                new ShipHit(4, 0, 4, 48, 0, Part: 2),
                new PieceDisabled(plating.Id, 0),
            ],
            _log.Where(e => e is ShipHit or EnemyBlocked or PieceDisabled));
        Assert.Equal(4, combat.Enemy.Block);
        Assert.Equal(1, combat.Enemy.PatternIndex);
        Assert.Equal([1, 1, 1], combat.Enemy.Parts.Select(p => p.PatternIndex));
    }

    [Fact]
    public void EachPartFollowsItsOwnPattern()
    {
        var run = NewRun();
        Start(run);
        _engine.EndTurn(run);
        var ram = _engine.NextActions(run).Single(a => a.Part == 2);
        Assert.Equal(new Intent(IntentKind.Attack, 2), ram.Intent);
        Assert.Null(ram.Lane);
    }

    [Fact]
    public void NextActionsListTheBodyThenTheWorkingParts()
    {
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        var combat = Start(run);
        Assert.Equal([null, 0, 1, 2], _engine.NextActions(run).Select(a => a.Part));

        combat.Enemy.Parts[1].Hull = 0;
        var actions = _engine.NextActions(run);
        Assert.Equal([null, 0, 2], actions.Select(a => a.Part));
        Assert.Equal(new EnemyAction(2, new Intent(IntentKind.HeavyAttack, 4, 0), 0, run.Ship.Find(plating.Id)), actions[2]);
    }

    [Fact]
    public void ADisabledPartNoLongerActs()
    {
        var run = NewRun();
        var combat = Start(run);
        combat.Enemy.Parts[0].Hull = 0;
        combat.Enemy.Parts[2].Hull = 0;
        _engine.EndTurn(run);
        Assert.Equal(60 - 5, run.Hull);
        Assert.Equal(4, combat.Enemy.Block);
        Assert.Equal([0, 1, 0], combat.Enemy.Parts.Select(p => p.PatternIndex));
    }

    [Fact]
    public void AttacksHitTheAimedPartThroughTheSharedBlock()
    {
        var run = NewRun();
        var combat = Start(run);
        _engine.SetTarget(run, 2);
        Assert.Equal(2, combat.Target);
        combat.Enemy.Block = 3;
        _log.Clear();

        _engine.PlayCard(run, Take(combat, "ram"));
        Assert.Equal(new EnemyHit(5, 3, 2, 10, Part: 2), _log[^1]);
        Assert.Equal(10, combat.Enemy.Parts[2].Hull);
        Assert.Equal(0, combat.Enemy.Block);
        Assert.Equal(40, combat.Enemy.Hull);
        Assert.Equal(2, combat.Target);
    }

    [Fact]
    public void APartAtZeroIsDisabledAndTheAimGoesBackToTheBody()
    {
        var run = NewRun();
        var combat = Start(run);
        _engine.SetTarget(run, 1);
        combat.Enemy.Parts[1].Hull = 3;
        _log.Clear();

        _engine.PlayCard(run, Take(combat, "ram"));
        Assert.Equal([new EnemyHit(5, 0, 5, 0, Part: 1), new PartDisabled(1)], _log.Skip(1));
        Assert.True(combat.Enemy.Parts[1].Disabled);
        Assert.Null(combat.Target);

        // The damage past the part's hull is lost; the next attack hits the body.
        Assert.Equal(40, combat.Enemy.Hull);
        _engine.PlayCard(run, Take(combat, "ram"));
        Assert.Equal(35, combat.Enemy.Hull);
    }

    [Fact]
    public void OnlyAWorkingPartCanBeAimedAt()
    {
        var run = NewRun();
        var combat = Start(run);
        combat.Enemy.Parts[0].Hull = 0;
        Assert.True(_engine.CanTarget(run, null));
        Assert.True(_engine.CanTarget(run, 1));
        Assert.False(_engine.CanTarget(run, 0));
        Assert.False(_engine.CanTarget(run, 3));
        Assert.False(_engine.CanTarget(run, -1));

        Assert.Throws<InvalidOperationException>(() => _engine.SetTarget(run, 0));
        Assert.Throws<InvalidOperationException>(() => _engine.SetTarget(run, 3));
        Assert.Null(combat.Target);
        Assert.Empty(_log);
    }

    [Fact]
    public void AnEnemyWithoutPartsCanOnlyBeAimedAtItsBody()
    {
        var run = NewRun();
        _engine.StartFight(run, "dummy");
        Assert.True(_engine.CanTarget(run, null));
        Assert.False(_engine.CanTarget(run, 0));
        Assert.Throws<InvalidOperationException>(() => _engine.SetTarget(run, 0));
    }

    [Fact]
    public void AimingNeedsAFight()
    {
        Assert.False(_engine.CanTarget(NewRun(), null));
        Assert.Throws<InvalidOperationException>(() => _engine.SetTarget(NewRun(), null));
    }

    [Fact]
    public void AimingIsReportedOnlyWhenItChanges()
    {
        var run = NewRun();
        Start(run);
        _engine.SetTarget(run, 1);
        _engine.SetTarget(run, 1);
        _engine.SetTarget(run, null);
        Assert.Equal([new TargetChanged(1), new TargetChanged(null)], _log);
    }

    [Fact]
    public void DisablingEveryPartDoesNotWinTheFight()
    {
        var run = NewRun();
        var combat = Start(run);
        combat.Enemy.Parts[0].Hull = 0;
        combat.Enemy.Parts[1].Hull = 0;
        combat.Enemy.Parts[2].Hull = 1;
        _engine.SetTarget(run, 2);

        Assert.Equal(CombatOutcome.Ongoing, _engine.PlayCard(run, Take(combat, "ram")));
        Assert.All(combat.Enemy.Parts, p => Assert.True(p.Disabled));
        Assert.Equal(RunPhase.Fight, run.Phase);
        Assert.Equal([null], _engine.NextActions(run).Select(a => a.Part));
    }

    [Fact]
    public void TheFightIsWonWhenTheBodyFallsWhileItsPartsStillWork()
    {
        var run = NewRun();
        var combat = Start(run);
        combat.Enemy.Hull = 1;
        Assert.Equal(CombatOutcome.Won, _engine.PlayCard(run, Take(combat, "ram")));
        Assert.Equal(RunPhase.Won, run.Phase);
    }

    [Fact]
    public void TheEnemysTurnStopsWhenTheShipsHullIsGone()
    {
        var run = NewRun();
        Start(run);
        run.Hull = 5;
        Assert.Equal(CombatOutcome.Lost, _engine.EndTurn(run));
        Assert.Single(_log.OfType<ShipHit>());
    }

    /// <summary>The body and the ram both make a heavy attack on lane 0: the plating in front, then the lance behind it.</summary>
    [Fact]
    public void TwoHeavyAttacksOnOneLaneAimAtTheFrontPieceThenTheOneBehindIt()
    {
        var heavyBody = Db.Enemies.Select(e => e.Id == "titan" ? e with { Pattern = [new Intent(IntentKind.HeavyAttack, 6, 0)] } : e).ToArray();
        var db = ContentDb.Create([.. Db.Pieces], [.. Db.Cards], heavyBody, [.. Db.Ships], [.. Db.Sections], [.. Db.Events]);
        var engine = new CombatEngine(db);
        var run = NewRun();
        var plating = Fit(run, "plating", 0, 0);
        var lance = Fit(run, "lance", 0, 1);
        var combat = Start(run, engine);
        combat.Enemy.Parts[1].Hull = 0;

        var heavies = engine.NextActions(run).Where(a => a.Lane is not null).ToList();
        Assert.Equal([null, 2], heavies.Select(a => a.Part));
        Assert.Equal([plating.Id, lance.Id], heavies.Select(a => a.Target!.Piece.Id));

        engine.EndTurn(run);
        Assert.Equal([plating.Id, lance.Id], combat.DisabledPieceIds);
    }

    [Fact]
    public void ASaveKeepsThePartsAndTheAim()
    {
        var run = NewRun(seed: 5);
        var combat = Start(run);
        _engine.SetTarget(run, 2);
        _engine.PlayCard(run, Take(combat, "ram"));
        _engine.EndTurn(run);

        var restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(run));
        Assert.Equal(2, restored.Combat!.Target);
        Assert.Equal(7, restored.Combat.Enemy.Parts[2].Hull);
        Assert.Equal([1, 1, 1], restored.Combat.Enemy.Parts.Select(p => p.PatternIndex));

        _engine.EndTurn(run);
        _engine.EndTurn(restored);
        Assert.Equal(SaveSerializer.Serialize(run), SaveSerializer.Serialize(restored));
    }

    [Fact]
    public void AimingIsAnActionTheSessionSaves()
    {
        var store = new MemorySaveStore();
        var session = RunSession.StartNew(Db, store, "test", seed: 7);
        session.Do(s => s.Combat.StartFight(s.State, "titan"));
        session.Do(s => s.Combat.SetTarget(s.State, 1));
        Assert.Equal([new TargetChanged(1)], session.LastEvents);
        Assert.Equal(1, RunSession.TryResume(Db, store)!.State.Combat!.Target);
    }
}
