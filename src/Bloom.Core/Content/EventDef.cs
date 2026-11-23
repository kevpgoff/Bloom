using System.Text.Json.Serialization;
using Bloom.Core.Pieces;

namespace Bloom.Core.Content;

/// <summary>
/// An event as defined in events.json: the spec's "short story with a choice". An event node on the map rolls
/// one of its sector's events. <see cref="Placeholder"/> marks events invented for testing.
/// </summary>
public sealed record EventDef(
    string Id,
    string Name,
    string Text,
    IReadOnlyList<EventChoice> Choices,
    int Sector = 1,
    bool Placeholder = false);

/// <summary>
/// One choice of an event. It can ask for salvage, paid when it is chosen (<see cref="Cost"/>), or for a fitted
/// piece with a tag (<see cref="NeedsTag"/>). Choosing it picks one of its <see cref="Results"/> by weight.
/// </summary>
public sealed record EventChoice(
    string Label,
    IReadOnlyList<EventResult> Results,
    int Cost = 0,
    Tag? NeedsTag = null);

/// <summary>What can come of a choice: the text shown afterwards and what changes. A heavier result is likelier.</summary>
public sealed record EventResult(string Text, IReadOnlyList<EventOutcome>? Outcomes = null, int Weight = 1)
{
    /// <summary>The outcomes, or none when the data leaves them out.</summary>
    [JsonIgnore]
    public IReadOnlyList<EventOutcome> AllOutcomes => Outcomes ?? [];
}

/// <summary>
/// One thing an event result does. In data files it is an object whose first property is <c>"type"</c>,
/// as for card effects: <c>{ "type": "salvage", "amount": 20 }</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SalvageOutcome), "salvage")]
[JsonDerivedType(typeof(HullOutcome), "hull")]
[JsonDerivedType(typeof(MaxHullOutcome), "maxHull")]
[JsonDerivedType(typeof(PieceOutcome), "piece")]
[JsonDerivedType(typeof(UpgradeOutcome), "upgrade")]
[JsonDerivedType(typeof(SectionOutcome), "section")]
[JsonDerivedType(typeof(FightOutcome), "fight")]
public abstract record EventOutcome;

/// <summary>Gain salvage, or lose it when negative. Salvage never drops below 0.</summary>
public sealed record SalvageOutcome(int Amount) : EventOutcome;

/// <summary>Repair the hull, up to its maximum, or damage it when negative. Damage never takes the hull below 1.</summary>
public sealed record HullOutcome(int Amount) : EventOutcome;

/// <summary>Raise the maximum hull and the hull with it, or lower the maximum (to 1 at least), which caps the hull.</summary>
public sealed record MaxHullOutcome(int Amount) : EventOutcome;

/// <summary>
/// A new piece goes into the cargo hold: the piece <see cref="Id"/>, or a random one of the pieces that match
/// <see cref="Rarity"/> and <see cref="Kind"/>, all equally likely. The bridge is never given.
/// </summary>
public sealed record PieceOutcome(string? Id = null, Rarity? Rarity = null, PieceKind? Kind = null) : EventOutcome;

/// <summary>One random owned piece that can still be upgraded is upgraded. Nothing happens when none can.</summary>
public sealed record UpgradeOutcome : EventOutcome;

/// <summary>A free hull section is offered once the event is over, as at a shipyard.</summary>
public sealed record SectionOutcome : EventOutcome;

/// <summary>The ship commits to a fight against <see cref="Enemy"/> once the event is over. The enemy's pool sets the reward.</summary>
public sealed record FightOutcome(string Enemy) : EventOutcome;
