using Bloom.Core.Rng;

namespace Bloom.Core.Tests;

public class RngTests
{
    [Fact]
    public void MatchesThePcgReferenceOutput()
    {
        // First outputs of the reference pcg32-demo (seed 42, stream 54). Pins the algorithm on every platform.
        var rng = new Pcg32(42, 54);
        Assert.Equal(0xa15c02b7u, rng.NextUInt());
        Assert.Equal(0x7b47f409u, rng.NextUInt());
        Assert.Equal(0xba1d3330u, rng.NextUInt());
    }

    [Fact]
    public void TheSameSeedGivesTheSameSequence()
    {
        var a = new Pcg32(1234);
        var b = new Pcg32(1234);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt(), b.NextUInt());
        }
    }

    [Fact]
    public void DifferentSeedsGiveDifferentSequences()
    {
        var a = new Pcg32(1);
        var b = new Pcg32(2);
        Assert.NotEqual(Enumerable.Range(0, 8).Select(_ => a.NextUInt()), Enumerable.Range(0, 8).Select(_ => b.NextUInt()));
    }

    [Fact]
    public void ARestoredStateContinuesTheSameSequence()
    {
        var original = new Pcg32(99);
        original.NextUInt();
        var restored = Pcg32.FromState(original.State);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(original.NextUInt(), restored.NextUInt());
        }
    }

    [Fact]
    public void AStateWithAnEvenIncrementIsRejected() =>
        Assert.Throws<ArgumentException>(() => Pcg32.FromState(new RngState(1, 2)));

    [Fact]
    public void NextStaysInRangeAndCoversIt()
    {
        var rng = new Pcg32(7);
        var seen = new HashSet<int>();
        for (int i = 0; i < 10_000; i++)
        {
            int value = rng.Next(7);
            Assert.InRange(value, 0, 6);
            seen.Add(value);
        }

        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void ShuffleKeepsEveryItem()
    {
        var items = Enumerable.Range(0, 50).ToList();
        new Pcg32(3).Shuffle(items);
        Assert.Equal(Enumerable.Range(0, 50), items.Order());
        Assert.NotEqual(Enumerable.Range(0, 50), items);
    }
}
