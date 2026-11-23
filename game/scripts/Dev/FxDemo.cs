using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Feedback;
using Godot;

namespace Bloom.Game.Dev;

/// <summary>
/// A developer aid for reviewing combat feedback: it plays the first fight of a fixed run by itself, through
/// <see cref="GameRoot.Act"/> like a player would, with pauses so each effect can be seen. It prefers cards whose
/// effects haven't been shown yet, repairs as soon as it can, and holds back a killing blow until every card effect
/// and the enemy's block have been shown (or turn 8). It stops on the Reward screen.
/// </summary>
/// <remarks>
/// Run a debug build with <c>godot --path game -- --fx-demo</c>, or <c>-- --fx-demo=lose</c> to only end turns
/// until the ship is lost, or <c>-- --fx-demo=boss</c> to fight the boss instead and aim at each of its parts in
/// turn until they are disabled. <see cref="GameRoot"/> keeps the demo's run in memory, so the saved run is untouched.
/// Each action and its events are printed, and so is each haptic pulse.
/// </remarks>
public partial class FxDemo : Node
{
    private const string Flag = "--fx-demo";
    private const int LastHeldTurn = 8;

    private static readonly Type[] CardEffects =
    [
        typeof(DamageEffect), typeof(BlockEffect), typeof(GainEnergyEffect), typeof(DrawEffect),
        typeof(FocusEffect), typeof(GrowEffect), typeof(RepairEffect),
    ];

    private readonly HashSet<Type> _shown = [];
    private bool _sawEnemyBlock;
    private double _wait = 1.5;
    private bool _finishing;

    public static bool IsRequested =>
        OS.IsDebugBuild() && OS.GetCmdlineUserArgs().Any(a => a == Flag || a.StartsWith(Flag + "=", StringComparison.Ordinal));

    private static bool LoseMode => OS.GetCmdlineUserArgs().Contains(Flag + "=lose");

    private static bool BossMode => OS.GetCmdlineUserArgs().Contains(Flag + "=boss");

    public override void _Ready()
    {
        Haptics.LogPulses = true;
        GD.Print($"FxDemo: playing {(BossMode ? "the boss" : "the first fight")}{(LoseMode ? " by only ending turns" : "")}.");
    }

    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait <= 0)
        {
            _wait = Step();
        }
    }

    /// <summary>Takes the next action and returns how long to watch it.</summary>
    private double Step()
    {
        if (GameRoot.Instance.Session is not { } session)
        {
            return 1;
        }

        var run = session.State;
        switch (run.Phase)
        {
            case RunPhase.Map when run.Path.Count == 0 && BossMode:
                // A shortcut no player has: the boss's node is out of reach, so the demo commits to its fight directly.
                string boss = session.Content.Enemies.First(e => e.Pool == EnemyPool.Boss).Id;
                Act("go straight to the boss", s => s.State.Encounter = new Encounter(boss));
                return 1.0;
            case RunPhase.Map when run.Path.Count == 0:
                int first = session.Map.Reachable(run)[0].Id;
                Act("choose the first node", s => s.Map.Choose(s.State, first));
                return 1.0;
            case RunPhase.Prepare:
                Act("start the fight", s => s.Refit.StartFight(s.State));
                return 2.0;
            case RunPhase.Fight:
                return Fight(session, run);
            default:
                if (_finishing)
                {
                    GD.Print("FxDemo: done.");
                    GetTree().Quit();
                    return 60;
                }

                _finishing = true;
                return 3.0;
        }
    }

    private double Fight(RunSession session, RunState run)
    {
        var combat = run.Combat!;
        if (BossMode && combat.Target is null && combat.Enemy.Parts.FindIndex(p => !p.Disabled) is var part and >= 0)
        {
            string name = session.Content.Enemy(combat.Enemy.DefId).AllParts[part].Name;
            Act($"aim at the {name}", s => s.Combat.SetTarget(s.State, part));
            return 1.0;
        }

        if (!LoseMode)
        {
            bool holdKills = combat.Turn < LastHeldTurn && !(_sawEnemyBlock && CardEffects.All(_shown.Contains));
            var playable = combat.Hand
                .Select(card => session.Combat.Preview(run, card))
                .Where(p => p.Playable && !(holdKills && Kills(p, combat)))
                .ToList();
            var choice = playable.FirstOrDefault(p => p.RepairTargets > 0)
                         ?? playable.FirstOrDefault(p => p.Def.Effects.Any(e => !_shown.Contains(e.GetType())))
                         ?? playable.FirstOrDefault(p => p.Def.Effects.Any(e => e is DamageEffect))
                         ?? playable.FirstOrDefault();
            if (choice is not null)
            {
                var targets = combat.DisabledPieceIds.Take(choice.RepairTargets).ToList();
                foreach (var effect in choice.Def.Effects)
                {
                    _shown.Add(effect.GetType());
                }

                Act($"play {choice.Def.Name}", s => s.Combat.PlayCard(s.State, choice.Card.Id, targets));
                return 1.4;
            }
        }

        Act("end the turn", s => s.Combat.EndTurn(s.State));
        return 2.4;
    }

    private static bool Kills(CardPreview preview, CombatState combat) =>
        combat.Target is null && preview.Effects.OfType<DamageEffect>().Sum(e => e.Amount) >= combat.Enemy.Hull + combat.Enemy.Block;

    private void Act(string what, Action<RunSession> action)
    {
        GD.Print($"FxDemo: {what}.");
        GameRoot.Instance.Act(action);
        if (GameRoot.Instance.Session?.LastEvents is { Count: > 0 } events)
        {
            _sawEnemyBlock |= events.Any(e => e is EnemyBlocked);
            GD.Print($"FxDemo:   {string.Join(", ", events.Select(e => e.ToString()))}");
        }
    }
}
