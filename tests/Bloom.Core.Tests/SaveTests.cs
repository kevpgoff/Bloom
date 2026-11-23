using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Grid;
using Bloom.Core.Map;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class SaveTests
{
    [Fact]
    public void ANewRunSurvivesARoundTrip()
    {
        string json = SaveSerializer.Serialize(NewRun());
        Assert.Equal(json, SaveSerializer.Serialize(SaveSerializer.Deserialize(json)));
    }

    [Fact]
    public void ARunInAFightResumesExactly()
    {
        var engine = new CombatEngine(Db);
        var original = NewRun(seed: 11);
        Fit(original, "lance", 0, 0);
        Fit(original, "plating", 0, 1);
        engine.StartFight(original, "dummy");
        engine.PlayCard(original, original.Combat!.Hand.First(c => c.CardId != "lance").Id);

        var restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(original));

        // The same action on both copies must give the same state, including the shuffle after the reshuffle.
        engine.EndTurn(original);
        engine.EndTurn(restored);
        Assert.Equal(SaveSerializer.Serialize(original), SaveSerializer.Serialize(restored));
    }

    [Fact]
    public void TheSaveIsReadableJson()
    {
        string json = SaveSerializer.Serialize(NewRun());
        Assert.Contains("\"rng\":{\"state\":", json);
        Assert.Contains("\"rotation\":\"R0\"", json);
        Assert.Contains($"\"schemaVersion\":{RunState.CurrentSchemaVersion}", json);
        Assert.DoesNotContain("\"combat\"", json); // nulls are left out
    }

    [Fact]
    public void AnUnknownSchemaVersionIsRejected()
    {
        string json = SaveSerializer.Serialize(NewRun())
            .Replace($"\"schemaVersion\":{RunState.CurrentSchemaVersion}", "\"schemaVersion\":99");
        Assert.Throws<SaveException>(() => SaveSerializer.Deserialize(json));
    }

    [Fact]
    public void AnUnreadableSaveIsRejected() =>
        Assert.Throws<SaveException>(() => SaveSerializer.Deserialize("not json"));

    [Fact]
    public void TheSessionSavesAfterEveryAction()
    {
        var store = new MemorySaveStore();
        var session = RunSession.StartNew(Db, store, "test", seed: 7);
        Assert.Equal(1, store.SaveCount);

        session.Do(s => s.Combat.StartFight(s.State, "dummy"));
        Assert.Equal(2, store.SaveCount);

        var resumed = RunSession.TryResume(Db, store);
        Assert.NotNull(resumed?.State.Combat);
        Assert.Equal(store.Json, SaveSerializer.Serialize(resumed!.State));
    }

    [Fact]
    public void AFailedActionIsNotSaved()
    {
        var store = new MemorySaveStore();
        var session = RunSession.StartNew(Db, store, "test", seed: 7);
        int bridge = BridgeId(session.State);
        Assert.Throws<InvalidOperationException>(() => session.Do(s => s.Fitter.Remove(s.State.Ship, bridge)));
        Assert.Equal(1, store.SaveCount);
    }

    /// <summary>
    /// Plays a whole fight through <see cref="RunSession"/>. After every action the session is thrown away
    /// and resumed from the saved JSON, and a copy that is never saved gets the same actions. Both must
    /// stay identical, so quitting at any point and coming back changes nothing.
    /// </summary>
    [Fact]
    public void ResumingAfterEveryActionPlaysTheSameFight()
    {
        var store = new MemorySaveStore();
        var session = RunSession.StartNew(Db, store, "test", seed: 3);
        Fit(session.State, "plating", 0, 0); // front of lane 0, where "mixed" makes heavy attacks
        Fit(session.State, "lance", 1, 1);
        Fit(session.State, "grinder", 1, 2);
        Fit(session.State, "garden", 3, 0);
        Fit(session.State, "singer", 4, 1);
        Fit(session.State, "tinker", 4, 2);
        session.Save();
        var reference = SaveSerializer.Deserialize(store.Json!);
        var engine = new CombatEngine(Db);

        session.Do(s => s.Combat.StartFight(s.State, "mixed"));
        engine.StartFight(reference, "mixed");
        bool sawFocus = false, sawGrowth = false, sawRepair = false;
        for (int step = 0; reference.Combat is { } combat && step < 500; step++)
        {
            Action<RunState> action = NextAction(engine, reference, ref sawRepair);
            session.Do(s => action(s.State));
            action(reference);
            sawFocus |= reference.Combat?.PendingFocus > 0;
            sawGrowth |= reference.Combat is { } c
                         && c.DrawPile.Concat(c.Hand).Concat(c.DiscardPile).Any(card => card.Growth > 1);

            session = RunSession.TryResume(Db, store)!;
            Assert.Equal(SaveSerializer.Serialize(reference), store.Json);
        }

        Assert.Null(reference.Combat);
        Assert.NotNull(reference.LastFight);
        Assert.True(sawFocus && sawGrowth && sawRepair, $"focus {sawFocus}, growth {sawGrowth}, repair {sawRepair}");
    }

    /// <summary>
    /// A fixed policy: repair when possible, then grow, then play the first playable card that is not
    /// plain block (so heavy attacks get through), otherwise end the turn.
    /// </summary>
    private static Action<RunState> NextAction(CombatEngine engine, RunState run, ref bool sawRepair)
    {
        var combat = run.Combat!;
        var previews = combat.Hand.Select(c => engine.Preview(run, c)).Where(p => p.Playable).ToList();
        if (previews.FirstOrDefault(p => p.RepairTargets > 0) is { } repair)
        {
            sawRepair = true;
            var targets = combat.DisabledPieceIds.Take(repair.RepairTargets).ToList();
            return r => engine.PlayCard(r, repair.Card.Id, targets);
        }

        var play = previews.FirstOrDefault(p => p.Def.Effects.Any(e => e is GrowEffect))
                   ?? previews.FirstOrDefault(p => !p.Def.Effects.All(e => e is BlockEffect));
        if (play is not null)
        {
            return r => engine.PlayCard(r, play.Card.Id);
        }

        return r => engine.EndTurn(r);
    }

    /// <summary>
    /// Plays a whole run through <see cref="RunSession"/> on a short fixed map: a skirmish, a dock, a shipyard,
    /// a grove, an event, a hard fight and the boss, with rewards, trading (buy, sell, repair), a hull section, an upgrade, an event's choice,
    /// and refitting (fit, move, turn, undo) before each fight. After every action the session is resumed from the saved JSON and
    /// compared with a copy that is never saved.
    /// </summary>
    [Fact]
    public void ResumingAfterEveryActionPlaysTheSameVoyage()
    {
        var store = new MemorySaveStore();
        var session = RunSession.StartNew(Db, store, "test", seed: 5);
        session.State.Map = new SectorMap
        {
            Sector = 1,
            Nodes =
            [
                new MapNode(0, NodeType.Skirmish, 0, 0),
                new MapNode(1, NodeType.Dock, 1, 0),
                new MapNode(2, NodeType.Shipyard, 2, 0),
                new MapNode(3, NodeType.Grove, 3, 0),
                new MapNode(4, NodeType.Event, 4, 0),
                new MapNode(5, NodeType.HardFight, 5, 0),
                new MapNode(6, NodeType.Boss, 6, 0),
            ],
            Edges = [new MapEdge(0, 1), new MapEdge(1, 2), new MapEdge(2, 3), new MapEdge(3, 4), new MapEdge(4, 5), new MapEdge(5, 6)],
        };
        Fit(session.State, "lance", 1, 1);
        Fit(session.State, "plating", 1, 3);
        session.State.Hull = 40; // so the dock has something to repair
        session.State.Salvage = 60; // and enough to buy as well
        session.Save();
        var reference = SaveSerializer.Deserialize(store.Json!);
        var combat = new CombatEngine(Db);
        var rewards = new RewardEngine(Db);
        var refit = new RefitEngine(Db);
        var map = new MapEngine(Db);
        var grove = new GroveEngine(Db);
        var dock = new DockEngine(Db);
        var yard = new ShipyardEngine(Db);
        var events = new EventEngine(Db);

        var seen = new HashSet<string>();
        int refitStep = 0;
        bool unusedRepair = false;
        for (int step = 0; reference.Phase is not (RunPhase.Won or RunPhase.Lost) && step < 2000; step++)
        {
            Action<RunState>? action = null;
            switch (reference.Phase)
            {
                case RunPhase.Map:
                    seen.Add("map");
                    refitStep = 0;
                    int next = map.Reachable(reference)[0].Id;
                    action = r => map.Choose(r, next);
                    break;
                case RunPhase.Fight:
                    action = NextAction(combat, reference, ref unusedRepair);
                    break;
                case RunPhase.Reward:
                    seen.Add("reward");
                    action = r => rewards.Take(r, 0);
                    break;
                case RunPhase.Grove:
                    seen.Add("upgrade");
                    int upgraded = PieceUpgrades.Upgradable(Db, reference)[0].Id;
                    action = r => grove.Upgrade(r, upgraded);
                    break;
                case RunPhase.Dock:
                    action = NextDockAction(dock, reference, seen);
                    break;
                case RunPhase.Event when reference.Event!.Choice is null:
                    seen.Add("choose");
                    int open = Enumerable.Range(0, events.Current(reference)!.Choices.Count).First(i => events.CanChoose(reference, i));
                    action = r => events.Choose(r, open);
                    break;
                case RunPhase.Event:
                    seen.Add("continue");
                    action = events.Continue;
                    break;
                case RunPhase.Shipyard:
                    seen.Add("attach");
                    var (origin, rotation) = ShipyardEngine.Placements(reference.Ship.Grid, yard.Offered(reference, 0)!.Shape).First();
                    action = r => yard.Attach(r, 0, origin, rotation);
                    break;
                default:
                    while (action is null)
                    {
                        action = NextRefitAction(refit, reference, refitStep++, seen);
                    }

                    break;
            }

            session.Do(s => action(s.State));
            action(reference);

            session = RunSession.TryResume(Db, store)!;
            Assert.Equal(SaveSerializer.Serialize(reference), store.Json);
        }

        Assert.Equal(RunPhase.Won, reference.Phase);
        Assert.Equal(["attach", "buy", "choose", "continue", "fit", "leave", "map", "move", "repair", "reward", "rotate", "sell", "undo", "upgrade"], seen.Order());
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], reference.Path);
        Assert.True(reference.Ship.Grid.CellCount > NewRun().Ship.Grid.CellCount);
        Assert.Contains(reference.Ship.Fitted, f => f.Piece.Upgraded);
    }

    /// <summary>At the dock: sell a held piece, repair once, buy the first piece for sale, then leave.</summary>
    private static Action<RunState> NextDockAction(DockEngine dock, RunState run, HashSet<string> seen)
    {
        if (run.Cargo.Pieces.Count > 0 && !seen.Contains("sell"))
        {
            seen.Add("sell");
            int sold = run.Cargo.Pieces[0].Id;
            return r => dock.Sell(r, sold);
        }

        if (DockEngine.CanRepair(run) && !seen.Contains("repair"))
        {
            seen.Add("repair");
            return r => dock.Repair(r);
        }

        if (dock.CanBuy(run, 0) && !seen.Contains("buy"))
        {
            seen.Add("buy");
            return r => dock.Buy(r, 0);
        }

        seen.Add("leave");
        return r => dock.Leave(r);
    }

    /// <summary>One refit step of a fixed script, or null to skip that step. The last step starts the chosen fight.</summary>
    private static Action<RunState>? NextRefitAction(RefitEngine refit, RunState run, int step, HashSet<string> seen)
    {
        switch (step)
        {
            case 0 when run.Cargo.Pieces.Count > 0 && FirstSpot(refit, run, run.Cargo.Pieces[0].Id) is { } spot:
                seen.Add("fit");
                int held = run.Cargo.Pieces[0].Id;
                return r => refit.FitFromCargo(r, held, spot.Origin, spot.Rotation);
            case 1 when run.Ship.Fitted.Count > 1 && FirstSpot(refit, run, run.Ship.Fitted[^1].Piece.Id) is { } spot:
                seen.Add("move");
                int moved = run.Ship.Fitted[^1].Piece.Id;
                return r => refit.Move(r, moved, spot.Origin, spot.Rotation);
            case 2 when run.Ship.Fitted.FirstOrDefault(f => refit.CanRotate(run, f.Piece.Id)) is { } turned:
                seen.Add("rotate");
                return r => refit.Rotate(r, turned.Piece.Id);
            case 3 when refit.CanUndo(run):
                seen.Add("undo");
                return r => refit.Undo(r);
            case 4:
                return r => refit.StartFight(r);
            default:
                return step > 4 ? throw new InvalidOperationException("The refit script ran out of steps.") : null;
        }
    }

    /// <summary>The first spot, in row order, where a piece fits other than where it is now.</summary>
    private static (Cell Origin, Rotation Rotation)? FirstSpot(RefitEngine refit, RunState run, int pieceId)
    {
        var current = run.Ship.Find(pieceId);
        for (int y = 0; y < Rules.GridRows; y++)
        {
            for (int x = 0; x < Rules.GridColumns; x++)
            {
                foreach (var rotation in Enum.GetValues<Rotation>())
                {
                    var origin = new Cell(x, y);
                    if ((current?.Origin, current?.Rotation) != (origin, rotation)
                        && refit.PreviewPlacement(run, pieceId, origin, rotation).Fits)
                    {
                        return (origin, rotation);
                    }
                }
            }
        }

        return null;
    }

    [Fact]
    public void ThereIsNothingToResumeWithoutASave() =>
        Assert.Null(RunSession.TryResume(Db, new MemorySaveStore()));
}
