using Bloom.Core.Content;
using Bloom.Core.Pieces;

namespace Bloom.Core.Cards;

/// <summary>The ship is the deck: every card comes from a fitted piece.</summary>
public static class DeckBuilder
{
    /// <summary>
    /// Builds the deck from the fitted pieces that are not disabled, in layout order. Pieces in the
    /// cargo hold add nothing. Card ids start at 1.
    /// </summary>
    public static List<CardInstance> Build(ShipLayout layout, ContentDb content, IReadOnlyCollection<int>? disabledPieceIds = null)
    {
        var deck = new List<CardInstance>();
        int nextId = 1;
        foreach (var fitted in layout.Fitted)
        {
            if (disabledPieceIds?.Contains(fitted.Piece.Id) == true)
            {
                continue;
            }

            foreach (var grant in content.Piece(fitted.Piece.DefId).Cards)
            {
                for (int i = 0; i < grant.Count; i++)
                {
                    deck.Add(new CardInstance(nextId++, grant.Card, fitted.Piece.Id));
                }
            }
        }

        return deck;
    }
}
