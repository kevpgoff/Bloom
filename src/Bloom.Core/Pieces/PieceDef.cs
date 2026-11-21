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

/// <summary>
/// A piece of equipment or a crew member, as defined in pieces.json. <see cref="Placeholder"/> marks
/// pieces that are invented and not in the spec.
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
    bool Placeholder = false)
{
    /// <summary>The bridge carries the Core tag. It can be moved but never removed.</summary>
    [JsonIgnore]
    public bool IsBridge => Tags?.Contains(Tag.Core) == true;
}

/// <summary>One owned copy of a piece. Ids are unique within a run.</summary>
public sealed record PieceInstance(int Id, string DefId, bool Upgraded = false);
