using Bloom.Core.Combat;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class RewardTests
{
    private readonly CombatEngine _combat = new(Db);
    private readonly RewardEngine _rewards = new(Db);

    /// <summary>Starts a fight and wins it with one Ram.</summary>
    private RunState WinFight(RunState run, string enemyId = "dummy")
    {
        var combat = _combat.StartFight(run, enemyId);
        combat.Enemy.Hull = 1;
        _combat.PlayCard(run, Take(combat, "ram"));
        return run;
    }

    [Fact]
    public void AWinOffersThreeDifferentPiecesButNeverTheBridge()
    {
        var run = WinFight(NewRun());
        Assert.Equal(RunPhase.Reward, run.Phase);
        Assert.Equal(1, run.FightsWon);
        var offer = run.Reward!.Pieces;
        Assert.Equal(Rules.RewardChoices, offer.Count);
        Assert.Equal(offer.Count, offer.Distinct().Count());
        Assert.DoesNotContain("bridge", offer);
    }

    [Fact]
    public void BeatingTheBossOfTheLastSectorWinsTheRunWithoutAReward()
    {
        var run = WinFight(NewRun(), "titan");
        Assert.Equal(RunResult.Won, run.Result);
        Assert.Equal(RunPhase.Won, run.Phase);
        Assert.Null(run.Reward);
    }

    [Fact]
    public void ABossBeforeTheLastSectorGivesARewardThenTheNextSector()
    {
        var run = UseTinyMap(NewRun());
        run.Sectors = 2;
        run.Path.AddRange([0, 1, 2, 3]);
        WinFight(run, "titan");
        Assert.Equal(RunPhase.Reward, run.Phase);
        Assert.Equal(Rarity.Rare, Db.Piece(run.Reward!.Pieces[0]).Rarity);

        _rewards.Skip(run);
        Assert.Equal(2, run.Sector);
        Assert.Equal(2, run.Map!.Sector);
        Assert.Empty(run.Path);
        Assert.Equal(RunPhase.Map, run.Phase);
    }

    [Theory]
    [InlineData("dummy", 20, 30)]
    [InlineData("heavy", 20, 30)]
    [InlineData("brute", 40, 55)]
    public void AWonFightPaysSalvage(string enemyId, int min, int max)
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var run = NewRun(seed);
            run.Salvage = 7;
            WinFight(run, enemyId);
            Assert.InRange(run.Reward!.Salvage, min, max);
            Assert.Equal(7 + run.Reward.Salvage, run.Salvage);
        }
    }

    [Fact]
    public void AHardFightAlwaysOffersARarePieceFirst()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            var offer = WinFight(NewRun(seed), "brute").Reward!.Pieces;
            Assert.Equal("spear", offer[0]); // the only rare piece in the test content
            Assert.Equal(Rules.RewardChoices, offer.Distinct().Count());
        }
    }

    [Fact]
    public void TheSameRunGivesTheSameOffer() =>
        Assert.Equal(WinFight(NewRun(seed: 5)).Reward!.Pieces, WinFight(NewRun(seed: 5)).Reward!.Pieces);

    /// <summary>
    /// The test content has one uncommon and one rare piece, so a second roll of the same rarity must fall
    /// back to the other pieces. Every offer must still hold three different pieces.
    /// </summary>
    [Fact]
    public void WhenARarityRunsOutTheOtherPiecesFillTheOffer()
    {
        for (ulong seed = 0; seed < 500; seed++)
        {
            var offer = _rewards.Roll(NewRun(seed))!.Pieces;
            Assert.Equal(Rules.RewardChoices, offer.Distinct().Count());
            Assert.DoesNotContain("bridge", offer);
        }
    }

    /// <summary>The first pick of an offer never falls back, so over many fixed seeds it follows the odds (60/30/10).</summary>
    [Fact]
    public void TheFirstPickFollowsTheRarityOdds()
    {
        const int Runs = 3000;
        var counts = new Dictionary<Rarity, int> { [Rarity.Common] = 0, [Rarity.Uncommon] = 0, [Rarity.Rare] = 0 };
        for (ulong seed = 0; seed < Runs; seed++)
        {
            counts[Db.Piece(_rewards.Roll(NewRun(seed))!.Pieces[0]).Rarity]++;
        }

        Assert.InRange(counts[Rarity.Common] * 100 / Runs, 56, 64);
        Assert.InRange(counts[Rarity.Uncommon] * 100 / Runs, 26, 34);
        Assert.InRange(counts[Rarity.Rare] * 100 / Runs, 7, 13);
    }

    /// <summary>
    /// With a Weapon piece fitted, the common Grinder (crew whose bonus targets Weapon) should come up as
    /// the first pick about three times as often as the common Plating (Shield), which matches nothing.
    /// </summary>
    [Fact]
    public void PiecesThatShareAShipTagAreFavored()
    {
        int grinder = 0, plating = 0;
        for (ulong seed = 0; seed < 3000; seed++)
        {
            var run = NewRun(seed);
            Fit(run, "lance", 0, 0);
            string first = _rewards.Roll(run)!.Pieces[0];
            grinder += first == "grinder" ? 1 : 0;
            plating += first == "plating" ? 1 : 0;
        }

        Assert.InRange((double)grinder / plating, 2.2, 3.8);
    }

    [Fact]
    public void WithoutMatchingTagsEveryPieceOfARarityIsAsLikely()
    {
        int grinder = 0, plating = 0;
        for (ulong seed = 0; seed < 3000; seed++)
        {
            string first = _rewards.Roll(NewRun(seed))!.Pieces[0]; // only the bridge is fitted
            grinder += first == "grinder" ? 1 : 0;
            plating += first == "plating" ? 1 : 0;
        }

        Assert.InRange((double)grinder / plating, 0.75, 1.33);
    }

    [Fact]
    public void ATakenPieceGoesToTheCargoHold()
    {
        var run = WinFight(NewRun());
        string defId = run.Reward!.Pieces[1];
        int id = run.NextPieceId;

        var piece = _rewards.Take(run, 1);

        Assert.Equal(new PieceInstance(id, defId), piece);
        Assert.Equal([piece], run.Cargo.Pieces);
        Assert.Null(run.Reward);
        Assert.Equal(RunPhase.Map, run.Phase);
    }

    [Fact]
    public void ATakenPieceMayOverfillTheHold()
    {
        var run = WinFight(NewRun());
        for (int i = 0; i < Rules.CargoCapacity; i++)
        {
            run.Cargo.Add(run.NewPiece("tinker"));
        }

        _rewards.Take(run, 0);
        Assert.Equal(Rules.CargoCapacity + 1, run.Cargo.Pieces.Count);
        Assert.Equal(1, run.Cargo.Excess);
    }

    [Fact]
    public void SkippingTakesNothing()
    {
        var run = WinFight(NewRun());
        _rewards.Skip(run);
        Assert.Null(run.Reward);
        Assert.Empty(run.Cargo.Pieces);
        Assert.Equal(RunPhase.Map, run.Phase);
    }

    [Fact]
    public void WithoutAnOfferTakingAndSkippingThrowAndChangeNothing()
    {
        var run = NewRun();
        string before = SaveSerializer.Serialize(run);
        Assert.Throws<InvalidOperationException>(() => _rewards.Take(run, 0));
        Assert.Throws<InvalidOperationException>(() => _rewards.Skip(run));
        Assert.Equal(before, SaveSerializer.Serialize(run));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void TakingAPieceThatIsNotOfferedThrowsAndChangesNothing(int index)
    {
        var run = WinFight(NewRun());
        string before = SaveSerializer.Serialize(run);
        Assert.Throws<InvalidOperationException>(() => _rewards.Take(run, index));
        Assert.Equal(before, SaveSerializer.Serialize(run));
    }

    [Fact]
    public void TheOfferSurvivesASave()
    {
        var run = WinFight(NewRun());
        var restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(run));
        Assert.Equal(run.Reward!.Pieces, restored.Reward!.Pieces);
        Assert.Equal(RunPhase.Reward, restored.Phase);
    }
}
