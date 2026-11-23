using Bloom.Core.Content;
using Bloom.Core.Pieces;

namespace Bloom.Core.Run;

/// <summary>How a choice of an event stands for the ship now.</summary>
public enum ChoiceState
{
    /// <summary>The choice can be taken.</summary>
    Open,

    /// <summary>The choice is shown, but the run has less salvage than it costs.</summary>
    Unaffordable,

    /// <summary>No fitted piece carries the tag the choice needs, so the choice is not shown at all.</summary>
    Hidden,
}

/// <summary>
/// An event: the spec's short story with a choice. The player picks one of its choices, sees what came of it,
/// then continues: to the map, to the hull section it gave, or to the fight it started.
/// </summary>
/// <remarks>
/// From the plan the owner approved on 2026-11-22: a choice can ask for salvage, and outcomes are salvage, hull,
/// maximum hull, a piece into the cargo hold, an upgrade and a fight. The owner, while the events were sketched:
/// choices do not show their outcomes, because reading matters. Defaults taken when events were built: an event is
/// rolled when its node is chosen, all of the sector's events equally likely, and one repeats only once the sector's
/// others have been met; a choice can also need a fitted piece with a tag, and is hidden without one; a result is
/// picked by weight; an event can also give a free hull section; event damage never takes the hull below 1; and a
/// fight started by an event pays the reward of the enemy's pool.
/// </remarks>
public sealed class EventEngine(ContentDb content)
{
    /// <summary>The event the ship is at, or null away from one.</summary>
    public EventDef? Current(RunState run) =>
        run.Phase == RunPhase.Event && run.Event is { } visit ? content.Event(visit.EventId) : null;

    /// <summary>
    /// How <paramref name="choice"/> stands for the ship now. A choice that needs a tag no fitted piece carries is
    /// hidden, whatever it costs.
    /// </summary>
    public ChoiceState StateOf(RunState run, EventChoice choice) =>
        choice.NeedsTag is { } tag && !HasTag(run, tag) ? ChoiceState.Hidden
        : choice.Cost > run.Salvage ? ChoiceState.Unaffordable
        : ChoiceState.Open;

    /// <summary>Whether the choice at <paramref name="index"/> can be taken now.</summary>
    public bool CanChoose(RunState run, int index) =>
        Current(run) is { } ev && run.Event!.Choice is null && index >= 0 && index < ev.Choices.Count
        && StateOf(run, ev.Choices[index]) == ChoiceState.Open;

    /// <summary>
    /// Takes the choice at <paramref name="index"/>: pays its cost, picks one of its results by weight and applies
    /// what that result does. The result stays on show until <see cref="Continue"/>.
    /// </summary>
    public void Choose(RunState run, int index)
    {
        var visit = RequireEvent(run);
        var ev = content.Event(visit.EventId);
        if (visit.Choice is not null)
        {
            throw new InvalidOperationException("A choice has already been made here.");
        }

        if (index < 0 || index >= ev.Choices.Count)
        {
            throw new InvalidOperationException($"There is no choice {index}; '{ev.Id}' has {ev.Choices.Count}.");
        }

        var choice = ev.Choices[index];
        switch (StateOf(run, choice))
        {
            case ChoiceState.Hidden:
                throw new InvalidOperationException($"'{choice.Label}' needs a fitted {choice.NeedsTag} piece.");
            case ChoiceState.Unaffordable:
                throw new InvalidOperationException($"'{choice.Label}' costs {choice.Cost} salvage, but the ship has {run.Salvage}.");
        }

        int salvage = run.Salvage;
        int hull = run.Hull;
        int maxHull = run.MaxHull;
        run.Salvage -= choice.Cost;

        int resultIndex = choice.Results.Count == 1 ? 0 : PickWeighted(run, choice.Results);
        var gained = new List<int>();
        var upgraded = new List<int>();
        foreach (var outcome in choice.Results[resultIndex].AllOutcomes)
        {
            Apply(run, outcome, gained, upgraded);
        }

        run.Event = visit with
        {
            Choice = index,
            Result = resultIndex,
            Changes = new EventChanges(run.Salvage - salvage, run.Hull - hull, run.MaxHull - maxHull, gained, upgraded),
        };
    }

    /// <summary>The result that came up, or null before a choice is made.</summary>
    public EventResult? Result(RunState run) =>
        Current(run) is { } ev && run.Event is { Choice: int choice, Result: int result } ? ev.Choices[choice].Results[result] : null;

    /// <summary>
    /// Leaves the event once a choice is made. A result that started a fight commits to it, and the event's node
    /// is done when that fight's reward is; otherwise the node is done now, and a hull section the result gave is
    /// offered next.
    /// </summary>
    public void Continue(RunState run)
    {
        RequireEvent(run);
        var result = Result(run) ?? throw new InvalidOperationException("Make a choice first.");
        run.Event = null;
        if (result.AllOutcomes.OfType<FightOutcome>().FirstOrDefault() is { } fight)
        {
            run.EnemiesMet.Add(fight.Enemy);
            run.Encounter = new Encounter(fight.Enemy);
            return;
        }

        MapEngine.FinishNode(run);
        if (result.AllOutcomes.Any(o => o is SectionOutcome))
        {
            ShipyardEngine.OfferSections(content, run);
        }
    }

    /// <summary>The pieces a <see cref="PieceOutcome"/> picks from, in content order: never the bridge.</summary>
    public static IEnumerable<PieceDef> PiecesFor(ContentDb content, PieceOutcome outcome) =>
        outcome.Id is { } id
            ? content.Pieces.Where(p => p?.Id == id && !p.IsBridge)
            : content.Pieces.Where(p => p is not null && !p.IsBridge
                && (outcome.Rarity is null || p.Rarity == outcome.Rarity)
                && (outcome.Kind is null || p.Kind == outcome.Kind));

    /// <summary>
    /// Rolls an event for an event node from the run's random numbers: one of the sector's events, all equally
    /// likely, skipping those met this run while any other is left.
    /// </summary>
    internal static EventVisit Roll(ContentDb content, RunState run)
    {
        var events = content.Events.Where(e => e.Sector == run.Sector).ToList();
        if (events.Count == 0)
        {
            throw new InvalidOperationException($"Sector {run.Sector} has no event.");
        }

        var fresh = events.Where(e => !run.SeenEvents.Contains(e.Id)).ToList();
        var choices = fresh.Count > 0 ? fresh : events;
        var ev = choices[run.Rng.Next(choices.Count)];
        run.SeenEvents.Add(ev.Id);
        return new EventVisit(ev.Id);
    }

    private void Apply(RunState run, EventOutcome outcome, List<int> gained, List<int> upgraded)
    {
        switch (outcome)
        {
            case SalvageOutcome s:
                run.Salvage = Math.Max(0, run.Salvage + s.Amount);
                break;
            case HullOutcome h:
                run.Hull = h.Amount > 0 ? Math.Min(run.MaxHull, run.Hull + h.Amount) : Math.Max(1, run.Hull + h.Amount);
                break;
            case MaxHullOutcome m:
                run.MaxHull = Math.Max(1, run.MaxHull + m.Amount);
                run.Hull = m.Amount > 0 ? run.Hull + m.Amount : Math.Min(run.Hull, run.MaxHull);
                break;
            case PieceOutcome p:
                var candidates = PiecesFor(content, p).ToList();
                var def = candidates.Count == 1 ? candidates[0] : candidates[run.Rng.Next(candidates.Count)];
                var piece = run.NewPiece(def.Id);
                run.Cargo.Add(piece);
                gained.Add(piece.Id);
                break;
            case UpgradeOutcome:
                var upgradable = PieceUpgrades.Upgradable(content, run);
                if (upgradable.Count > 0)
                {
                    var pick = upgradable[run.Rng.Next(upgradable.Count)];
                    PieceUpgrades.Upgrade(content, run, pick.Id);
                    upgraded.Add(pick.Id);
                }

                break;
        }
    }

    private bool HasTag(RunState run, Tag tag) =>
        run.Ship.Fitted.Any(f => content.Piece(f.Piece.DefId).Tags.Contains(tag));

    private static int PickWeighted(RunState run, IReadOnlyList<EventResult> results)
    {
        int roll = run.Rng.Next(results.Sum(r => r.Weight));
        for (int i = 0; i < results.Count; i++)
        {
            roll -= results[i].Weight;
            if (roll < 0)
            {
                return i;
            }
        }

        return results.Count - 1;
    }

    private static EventVisit RequireEvent(RunState run) =>
        run.Phase == RunPhase.Event && run.Event is { } visit
            ? visit
            : throw new InvalidOperationException("The ship is not at an event.");
}
