using System.Text.Json.Serialization;
using Bloom.Core.Cards;
using Bloom.Core.Grid;

namespace Bloom.Core.Pieces;

public enum PieceKind
{
    Equipment,
    Crew,
}

public enum Tag
{
    Core,
    Weapon,
    Shield,
    Solar,
    Garden,
    Drone,
}

public enum Rarity
{
    Common,
    Uncommon,
    Rare,
}

/// <summary>A fitted piece adds <see cref="Count"/> copies of a card to the deck.</summary>
public sealed record CardGrant(string Card, int Count);

/// <summary>
/// A bonus a piece gives to adjacent pieces that carry <see cref="TargetTag"/>. A piece can receive a
/// bonus with the same <see cref="Name"/> only once.
/// </summary>
public sealed record AdjacencyBonus(string Name, Tag TargetTag, BonusEffect Effect);

/// <summary>When a piece's passive takes effect.</summary>
public enum PassiveTrigger
{
    /// <summary>Once, when the first turn of a fight starts, before the passives of <see cref="TurnStart"/>.</summary>
    FightStart,

    /// <summary>When each turn starts, the first one included, before the turn's cards are drawn.</summary>
    TurnStart,

    /// <summary>All the time while the piece works. It changes what happens to the piece; nothing fires.</summary>
    Standing,

    /// <summary>When a fight is won, before its reward.</summary>
    FightWon,
}

/// <summary>
/// What a piece does by being fitted, without a card. The owner decided on 2026-11-23 that the game's items are
/// pieces like this: they take room on the ship and may add no cards at all.
/// </summary>
/// <remarks>
/// A ship counts a passive of a given <see cref="Name"/> once, as a piece receives a named bonus once: of the
/// working pieces that carry the name, the one whose first cell is nearest the front, then the left, counts
/// (<see cref="ShipFitter.ActivePassives"/>). A disabled piece's passive is off. <see cref="Upgrade"/> replaces
/// <see cref="Effect"/> when the piece is upgraded; it never changes the trigger. <see cref="Trigger"/> is
/// required in data; it is nullable only so the validator can report it missing.
/// </remarks>
public sealed record PassiveDef(string Name, PassiveTrigger? Trigger, Effect Effect, Effect? Upgrade = null)
{
    /// <summary>The effect as it applies for a piece that is upgraded or not.</summary>
    public Effect EffectFor(bool upgraded) => upgraded && Upgrade is { } better ? better : Effect;
}

/// <summary>
/// A piece of equipment or a crew member, as defined in pieces.json. <see cref="Placeholder"/> marks
/// pieces that are invented and not in the spec. A piece with a <see cref="Passive"/> may add no cards.
/// </summary>
public sealed record PieceDef(
    string Id,
    string Name,
    PieceKind Kind,
    IReadOnlyList<Tag> Tags,
    Rarity Rarity,
    Shape Shape,
    IReadOnlyList<CardGrant> Cards,
    AdjacencyBonus? Bonus = null,
    string? Commune = null,
    bool Placeholder = false,
    PassiveDef? Passive = null)
{
    /// <summary>The bridge carries the Core tag. It can be moved but never removed.</summary>
    [JsonIgnore]
    public bool IsBridge => Tags?.Contains(Tag.Core) == true;
}

/// <summary>One owned copy of a piece. Ids are unique within a run.</summary>
public sealed record PieceInstance(int Id, string DefId, bool Upgraded = false);
