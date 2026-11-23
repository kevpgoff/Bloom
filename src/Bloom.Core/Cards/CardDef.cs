namespace Bloom.Core.Cards;

/// <summary>
/// A card as defined in cards.json. <see cref="Placeholder"/> marks cards that are invented and not in the spec.
/// <see cref="Upgrade"/> is what the card becomes when its piece is upgraded; read upgraded cards through
/// <see cref="Content.ContentDb.Card(string, bool)"/>, which applies it.
/// </summary>
public sealed record CardDef(string Id, string Name, int Cost, IReadOnlyList<Effect> Effects, bool Placeholder = false, CardUpgrade? Upgrade = null)
{
    /// <summary>
    /// The card with its upgrade applied: "+" after the name, and the upgrade's cost and effects in place of
    /// the card's own where it gives them. The result has no upgrade of its own.
    /// </summary>
    public CardDef Upgraded() => Upgrade is { } upgrade
        ? this with
        {
            Name = Name + "+",
            Cost = upgrade.Cost ?? Cost,
            Effects = upgrade.Effects ?? Effects,
            Upgrade = null,
        }
        : this;
}

/// <summary>
/// What a card becomes when its piece is upgraded: a new cost, a new list of effects that replaces the
/// card's own, or both. A field left out keeps the card's value.
/// </summary>
public sealed record CardUpgrade(int? Cost = null, IReadOnlyList<Effect>? Effects = null);

/// <summary>
/// One copy of a card during a fight. Ids are unique within the fight's deck.
/// <see cref="SourcePieceId"/> links the card to the piece that adds it, so the UI can highlight that piece.
/// <see cref="Upgraded"/> is true when that piece is upgraded, so the card plays as its upgrade.
/// </summary>
public sealed record CardInstance(int Id, string CardId, int SourcePieceId, bool Upgraded = false)
{
    /// <summary>Times the card has grown in the current fight.</summary>
    public int Growth { get; set; }
}
