using Bloom.Core.Cards;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

/// <summary>What the hangar chooses for a run: the ship class, one of its loadouts and the seed.</summary>
public class RunSetupTests
{
    [Fact]
    public void ALoadoutStartsWithItsPieces()
    {
        var run = RunFactory.NewRun(Db, new RunSetup("test", Seed: 1, LoadoutId: "armed"));
        Assert.Equal("armed", run.LoadoutId);
        Assert.Equal(["bridge", "lance"], run.Ship.Fitted.Select(f => f.Piece.DefId).Order());
        Assert.Equal(6, DeckBuilder.Build(run.Ship, Db).Count); // 2 Ram, 2 Brace, 2 Lance
    }

    [Fact]
    public void ARunWithoutALoadoutUsesTheFirst()
    {
        var run = RunFactory.NewRun(Db, new RunSetup("test", Seed: 1));
        Assert.Equal("a", run.LoadoutId);
        Assert.Equal(["bridge"], run.Ship.Fitted.Select(f => f.Piece.DefId));
    }

    [Fact]
    public void AnUnknownLoadoutIsRefused() =>
        Assert.Throws<KeyNotFoundException>(() => RunFactory.NewRun(Db, new RunSetup("test", Seed: 1, LoadoutId: "nope")));

    [Fact]
    public void ALoadoutChangesOnlyThePieces()
    {
        var bare = RunFactory.NewRun(Db, new RunSetup("test", Seed: 5));
        var armed = RunFactory.NewRun(Db, new RunSetup("test", Seed: 5, LoadoutId: "armed"));
        Assert.Equal(bare.MaxHull, armed.MaxHull);
        Assert.Equal(bare.Ship.Grid.Cells, armed.Ship.Grid.Cells);
        Assert.Equal(SaveSerializer.Serialize(bare).Split("\"map\":")[1], SaveSerializer.Serialize(armed).Split("\"map\":")[1]); // the same voyage
    }

    [Fact]
    public void TheSameSeedTextGivesTheSameRun()
    {
        var typed = RunFactory.NewRun(Db, RunSetup.FromSeedText("test", "  bloom "));
        var again = RunFactory.NewRun(Db, RunSetup.FromSeedText("test", "BLOOM"));
        Assert.Equal("BLOOM", typed.SeedText);
        Assert.Equal(RunSeed.FromText("bloom"), typed.Seed);
        Assert.Equal(SaveSerializer.Serialize(typed), SaveSerializer.Serialize(again));
    }

    [Fact]
    public void DifferentSeedTextsGiveDifferentSeeds()
    {
        string[] texts = ["A", "B", "AB", "BA", "BLOOM", "BLOOM1", "0", ""];
        Assert.Equal(texts.Length, texts.Select(RunSeed.FromText).Distinct().Count());
    }

    [Fact]
    public void SeedTextIsCutToItsLongestLength()
    {
        string longText = new('x', RunSeed.MaxLength + 5);
        Assert.Equal(new string('X', RunSeed.MaxLength), RunSeed.Normalize(longText));
        Assert.Equal(RunSeed.FromText(new string('x', RunSeed.MaxLength)), RunSeed.FromText(longText));
    }

    [Fact]
    public void ASeedCodeIsShortAndEasyToRead()
    {
        string code = RunSeed.CodeFrom(0x0123_4567_89AB_CDEFUL);
        Assert.Equal(RunSeed.CodeLength, code.Length);
        Assert.Equal(code, RunSeed.Normalize(code)); // typing it back gives the same text
        Assert.NotEqual(code, RunSeed.CodeFrom(0x0123_4567_89AB_CDEEUL));

        for (ulong bits = 0; bits < 2000; bits++)
        {
            Assert.DoesNotContain(RunSeed.CodeFrom(bits * 0x9E37_79B9_7F4A_7C15UL), c => c is 'I' or 'L' or 'O' or 'U');
        }
    }

    [Fact]
    public void TheSeedTextAndTheLoadoutSurviveASave()
    {
        var store = new MemorySaveStore();
        RunSession.StartNew(Db, store, RunSetup.FromSeedText("test", "k7m2", "armed"));
        Assert.Equal(1, store.SaveCount);

        var resumed = RunSession.TryResume(Db, store)!.State;
        Assert.Equal("K7M2", resumed.SeedText);
        Assert.Equal("armed", resumed.LoadoutId);
        Assert.Contains("\"loadoutId\":\"armed\"", store.Json);
    }

    [Fact]
    public void ARunStartedFromANumberHasNoSeedText() => Assert.Null(NewRun(seed: 4).SeedText);
}
