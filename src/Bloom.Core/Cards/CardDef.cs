namespace Bloom.Core.Cards;

/// <summary>A card as defined in cards.json. <see cref="Placeholder"/> marks cards that are invented and not in the spec.</summary>
public sealed record CardDef(string Id, string Name, int Cost, IReadOnlyList<Effect> Effects, bool Placeholder = false);

/// <summary>
/// One copy of a card during a fight. Ids are unique within the fight's deck.
/// <see cref="SourcePieceId"/> links the card to the piece that adds it, so the UI can highlight that piece.
/// </summary>
public sealed record CardInstance(int Id, string CardId, int SourcePieceId)
{
    /// <summary>Times the card has grown in the current fight.</summary>
    public int Growth { get; set; }
}
