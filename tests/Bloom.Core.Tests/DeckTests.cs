using Bloom.Core.Cards;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class DeckTests
{
    [Fact]
    public void TheDeckComesFromFittedPieces()
    {
        var run = NewRun();
        int bridge = BridgeId(run);
        var deck = DeckBuilder.Build(run.Ship, Db);
        Assert.Equal(["ram", "ram", "brace", "brace"], deck.Select(c => c.CardId));
        Assert.All(deck, c => Assert.Equal(bridge, c.SourcePieceId));

        var lance = Fit(run, "lance", 0, 0);
        deck = DeckBuilder.Build(run.Ship, Db);
        Assert.Equal(6, deck.Count);
        Assert.Equal(2, deck.Count(c => c.SourcePieceId == lance.Id));
        Assert.Equal(Enumerable.Range(1, 6), deck.Select(c => c.Id));
    }

    [Fact]
    public void PiecesInTheCargoHoldAddNoCards()
    {
        var run = NewRun();
        run.Cargo.TryAdd(run.NewPiece("lance"));
        Assert.Equal(4, DeckBuilder.Build(run.Ship, Db).Count);
    }

    [Fact]
    public void DisabledPiecesAddNoCards()
    {
        var run = NewRun();
        var lance = Fit(run, "lance", 0, 0);
        Assert.Equal(4, DeckBuilder.Build(run.Ship, Db, [lance.Id]).Count);
    }
}
