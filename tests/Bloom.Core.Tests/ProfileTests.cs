using Bloom.Core.Profile;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

/// <summary>The profile: records of the voyages made with each ship class and loadout, and the locks that they open.</summary>
public class ProfileTests
{
    private readonly ProfileEngine _engine = new(Db);

    /// <summary>A run of the "test" ship class that has entered <paramref name="rows"/> rows and ended with <paramref name="result"/>.</summary>
    private static RunState Run(ulong seed = 1, RunResult result = RunResult.InProgress, int rows = 0, string ship = "test", string? loadout = null)
    {
        var run = RunFactory.NewRun(Db, new RunSetup(ship, seed, LoadoutId: loadout));
        run.Path.AddRange(run.Map!.RouteTo(run.Map.Boss.Id).Take(rows));
        run.Result = result;
        return run;
    }

    /// <summary>Launches a run and closes it with a result, as a whole voyage does.</summary>
    private ClosedVoyage Voyage(ProfileState profile, RunResult result, ulong seed = 1, int rows = 0, string ship = "test", string? loadout = null)
    {
        var run = Run(seed, rows: rows, ship: ship, loadout: loadout);
        _engine.Launch(profile, run);
        run.Result = result;
        return _engine.Finish(profile, run);
    }

    [Fact]
    public void ANewProfileHasNoRecordsAndOnlyTheShipsWithoutALockAreOpen()
    {
        var profile = new ProfileState();
        Assert.Empty(profile.Records);
        Assert.Null(profile.Open);
        Assert.True(ProfileEngine.IsUnlocked(profile, Db.Ship("test")));
        Assert.False(ProfileEngine.IsUnlocked(profile, Db.Ship("solar")));
    }

    [Fact]
    public void ALaunchOpensTheVoyageAndCountsIt()
    {
        var profile = new ProfileState();
        var run = Run(seed: 9, loadout: "armed");
        _engine.Launch(profile, run);

        Assert.Equal(new OpenVoyage("test", "armed", 9), profile.Open);
        Assert.True(ProfileEngine.IsOpen(profile, run));
        var record = Assert.Single(profile.Records);
        Assert.Equal(("test", "armed", 1, 0, 0, 0), (record.ShipClassId, record.LoadoutId, record.Launched, record.Won, record.Lost, record.Abandoned));
        Assert.Null(record.Furthest);
    }

    [Fact]
    public void OnlyOneVoyageIsOpenAtATime()
    {
        var profile = new ProfileState();
        _engine.Launch(profile, Run(seed: 1));
        string before = ProfileSerializer.Serialize(profile);

        Assert.Throws<InvalidOperationException>(() => _engine.Launch(profile, Run(seed: 2)));
        Assert.Equal(before, ProfileSerializer.Serialize(profile));
    }

    [Fact]
    public void ARunThatIsOverCannotBeLaunched()
    {
        var profile = new ProfileState();
        Assert.Throws<InvalidOperationException>(() => _engine.Launch(profile, Run(result: RunResult.Lost)));
        Assert.Empty(profile.Records);
        Assert.Null(profile.Open);
    }

    [Theory]
    [InlineData(RunResult.Won, VoyageOutcome.Won, 1, 0, 0)]
    [InlineData(RunResult.Lost, VoyageOutcome.Lost, 0, 1, 0)]
    [InlineData(RunResult.InProgress, VoyageOutcome.Abandoned, 0, 0, 1)]
    public void FinishingClosesTheVoyageWithHowTheRunEnded(RunResult result, VoyageOutcome outcome, int won, int lost, int abandoned)
    {
        var profile = new ProfileState();
        var run = Run(seed: 4, rows: 3);
        _engine.Launch(profile, run);
        run.Result = result;
        var end = _engine.Finish(profile, run);

        Assert.Null(profile.Open);
        Assert.Equal(outcome, end.Outcome);
        Assert.Same(end, profile.Last);
        Assert.Equal(("test", "a", 4UL), (end.ShipClassId, end.LoadoutId, end.Seed));
        var record = Assert.Single(profile.Records);
        Assert.Equal((1, won, lost, abandoned), (record.Launched, record.Won, record.Lost, record.Abandoned));
        Assert.Equal(new VoyageProgress(1, 3), record.Furthest);
    }

    [Fact]
    public void OnlyTheOpenVoyageCanBeFinished()
    {
        var profile = new ProfileState();
        Assert.Throws<InvalidOperationException>(() => _engine.Finish(profile, Run()));

        _engine.Launch(profile, Run(seed: 1));
        string before = ProfileSerializer.Serialize(profile);
        Assert.Throws<InvalidOperationException>(() => _engine.Finish(profile, Run(seed: 2, result: RunResult.Won)));
        Assert.Throws<InvalidOperationException>(() => _engine.Finish(profile, Run(seed: 1, result: RunResult.Won, loadout: "armed")));
        Assert.Throws<InvalidOperationException>(() => _engine.Finish(profile, Run(seed: 1, result: RunResult.Won, ship: "solar")));
        Assert.Equal(before, ProfileSerializer.Serialize(profile));
    }

    [Fact]
    public void EachShipAndLoadoutHasItsOwnRecordInTheOrderOfTheFirstLaunch()
    {
        var profile = new ProfileState();
        Voyage(profile, RunResult.Lost, seed: 1, loadout: "armed");
        Voyage(profile, RunResult.Won, seed: 2);
        Voyage(profile, RunResult.Lost, seed: 3, loadout: "armed");
        Voyage(profile, RunResult.InProgress, seed: 4, ship: "solar");

        Assert.Equal([("test", "armed"), ("test", "a"), ("solar", "a")], profile.Records.Select(r => (r.ShipClassId, r.LoadoutId)));
        Assert.Equal((2, 0, 2, 0), Counts(profile.Record("test", "armed")!));
        Assert.Equal((1, 1, 0, 0), Counts(profile.Record("test", "a")!));
        Assert.Equal((1, 0, 0, 1), Counts(profile.Record("solar", "a")!));
        Assert.Null(profile.Record("solar", "armed"));
    }

    private static (int Launched, int Won, int Lost, int Abandoned) Counts(LoadoutRecord record) =>
        (record.Launched, record.Won, record.Lost, record.Abandoned);

    [Fact]
    public void TheFurthestVoyageIsKept()
    {
        var profile = new ProfileState();
        Voyage(profile, RunResult.Lost, seed: 1, rows: 2);
        Assert.Equal(new VoyageProgress(1, 2), profile.Record("test", "a")!.Furthest);

        Voyage(profile, RunResult.Lost, seed: 2, rows: 5);
        Assert.Equal(new VoyageProgress(1, 5), profile.Record("test", "a")!.Furthest);

        Voyage(profile, RunResult.InProgress, seed: 3, rows: 1);
        Assert.Equal(new VoyageProgress(1, 5), profile.Record("test", "a")!.Furthest);

        // A later sector counts as further, whatever the row.
        var run = Run(seed: 4, rows: 1);
        _engine.Launch(profile, run);
        run.Sector = 2;
        run.Result = RunResult.Lost;
        _engine.Finish(profile, run);
        Assert.Equal(new VoyageProgress(2, 1), profile.Record("test", "a")!.Furthest);
    }

    [Fact]
    public void AWonVoyageOpensTheShipThatNamesItsShip()
    {
        var profile = new ProfileState();
        Assert.Empty(Voyage(profile, RunResult.Lost, seed: 1).Unlocked);
        Assert.Empty(Voyage(profile, RunResult.InProgress, seed: 2).Unlocked);
        Assert.False(ProfileEngine.IsUnlocked(profile, Db.Ship("solar")));

        // Any loadout of the named ship counts.
        Assert.Equal(["solar"], Voyage(profile, RunResult.Won, seed: 3, loadout: "armed").Unlocked);
        Assert.True(ProfileEngine.IsUnlocked(profile, Db.Ship("solar")));

        // It is opened only once.
        Assert.Empty(Voyage(profile, RunResult.Won, seed: 4).Unlocked);
        Assert.True(ProfileEngine.IsUnlocked(profile, Db.Ship("solar")));
    }

    [Fact]
    public void AWinWithAnotherShipOpensNothing()
    {
        var profile = new ProfileState();
        Assert.Empty(Voyage(profile, RunResult.Won, ship: "solar").Unlocked);
        Assert.False(ProfileEngine.IsUnlocked(profile, Db.Ship("solar")));
    }

    [Fact]
    public void TheEndOfARunIsFoundOnlyForTheVoyageThatEndedLast()
    {
        var profile = new ProfileState();
        var run = Run(seed: 6);
        Assert.Null(ProfileEngine.EndOf(profile, run));

        _engine.Launch(profile, run);
        run.Result = RunResult.Won;
        Assert.Null(ProfileEngine.EndOf(profile, run)); // not closed yet

        var end = _engine.Finish(profile, run);
        Assert.Same(end, ProfileEngine.EndOf(profile, run));
        Assert.False(ProfileEngine.IsOpen(profile, run));
        Assert.Null(ProfileEngine.EndOf(profile, Run(seed: 7, result: RunResult.Won)));

        // A practice replay of the same seed that is still in progress is not that voyage.
        Assert.Null(ProfileEngine.EndOf(profile, Run(seed: 6)));
    }

    [Fact]
    public void ReconcilingChangesNothingWithoutAnOpenVoyage()
    {
        var profile = new ProfileState();
        Voyage(profile, RunResult.Lost);
        string before = ProfileSerializer.Serialize(profile);

        Assert.False(_engine.Reconcile(profile, null));
        Assert.False(_engine.Reconcile(profile, Run(seed: 8)));
        Assert.False(_engine.Reconcile(profile, Run(seed: 8, result: RunResult.Won)));
        Assert.Equal(before, ProfileSerializer.Serialize(profile));
    }

    [Fact]
    public void ReconcilingKeepsAVoyageThatIsStillInProgress()
    {
        var profile = new ProfileState();
        var run = Run(seed: 2, rows: 2);
        _engine.Launch(profile, run);
        string before = ProfileSerializer.Serialize(profile);

        Assert.False(_engine.Reconcile(profile, run));
        Assert.Equal(before, ProfileSerializer.Serialize(profile));
    }

    [Theory]
    [InlineData(RunResult.Won, VoyageOutcome.Won)]
    [InlineData(RunResult.Lost, VoyageOutcome.Lost)]
    public void ReconcilingClosesAVoyageWhoseEndWasNotRecorded(RunResult result, VoyageOutcome outcome)
    {
        var profile = new ProfileState();
        var run = Run(seed: 2, rows: 4);
        _engine.Launch(profile, run);
        run.Result = result;

        Assert.True(_engine.Reconcile(profile, run));
        Assert.Null(profile.Open);
        Assert.Equal(outcome, profile.Last!.Outcome);
        Assert.Equal(result == RunResult.Won ? ["solar"] : [], profile.Last.Unlocked);
        Assert.Equal(new VoyageProgress(1, 4), profile.Record("test", "a")!.Furthest);
    }

    [Fact]
    public void ReconcilingAbandonsAVoyageWhoseSaveIsGoneOrHoldsAnotherRun()
    {
        foreach (var saved in new[] { null, Run(seed: 3), Run(seed: 2, loadout: "armed"), Run(seed: 3, result: RunResult.Won) })
        {
            var profile = new ProfileState();
            _engine.Launch(profile, Run(seed: 2));

            Assert.True(_engine.Reconcile(profile, saved));
            Assert.Null(profile.Open);
            Assert.Equal(new ClosedVoyage("test", "a", 2, VoyageOutcome.Abandoned, []), profile.Last, ClosedVoyageComparer.Instance);
            Assert.Equal((1, 0, 0, 1), Counts(profile.Record("test", "a")!));
            Assert.Null(profile.Record("test", "a")!.Furthest); // nothing is known of how far it came
            Assert.False(ProfileEngine.IsUnlocked(profile, Db.Ship("solar")));
        }
    }

    [Fact]
    public void TheProfileSurvivesSavingAndLoading()
    {
        var profile = new ProfileState();
        Voyage(profile, RunResult.Won, seed: 1, rows: 7, loadout: "armed");
        Voyage(profile, RunResult.Lost, seed: 2, rows: 3);
        _engine.Launch(profile, Run(seed: 3, ship: "solar"));

        var store = new MemorySaveStore();
        store.Save(ProfileSerializer.Serialize(profile));
        var loaded = ProfileSerializer.Load(store);

        Assert.Equal(ProfileSerializer.Serialize(profile), ProfileSerializer.Serialize(loaded));
        Assert.Equal(new OpenVoyage("solar", "a", 3), loaded.Open);
        Assert.Equal(new VoyageProgress(1, 7), loaded.Record("test", "armed")!.Furthest);
        Assert.Equal(VoyageOutcome.Lost, loaded.Last!.Outcome);
        Assert.True(ProfileEngine.IsUnlocked(loaded, Db.Ship("solar")));
    }

    [Fact]
    public void AnEmptyStoreGivesANewProfile()
    {
        var profile = ProfileSerializer.Load(new MemorySaveStore());
        Assert.Equal(ProfileState.CurrentSchemaVersion, profile.SchemaVersion);
        Assert.Empty(profile.Records);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("""{ "schemaVersion": 0, "records": [] }""")]
    [InlineData("""{ "schemaVersion": 1, "records": [{ "launched": 1 }] }""")]
    [InlineData("""{ "schemaVersion": 1, "records": [null] }""")]
    public void AProfileThatCannotBeReadIsRefused(string json)
    {
        Assert.Throws<SaveException>(() => ProfileSerializer.Deserialize(json));
    }

    [Fact]
    public void ARecordOfAShipThatTheContentNoLongerHasIsKept()
    {
        var profile = ProfileSerializer.Deserialize(
            """{ "schemaVersion": 1, "records": [{ "shipClassId": "gone", "loadoutId": "a", "launched": 2, "won": 1, "lost": 1 }] }""");
        Assert.Equal(1, profile.Record("gone", "a")!.Won);
        Assert.False(ProfileEngine.IsUnlocked(profile, Db.Ship("solar")));

        Voyage(profile, RunResult.Lost);
        Assert.Equal(2, profile.Records.Count);
    }

    /// <summary>Compares two ends by value: the list of opened ships is compared by its items.</summary>
    private sealed class ClosedVoyageComparer : IEqualityComparer<ClosedVoyage?>
    {
        public static readonly ClosedVoyageComparer Instance = new();

        public bool Equals(ClosedVoyage? x, ClosedVoyage? y) =>
            x is null || y is null
                ? ReferenceEquals(x, y)
                : (x.ShipClassId, x.LoadoutId, x.Seed, x.Outcome) == (y.ShipClassId, y.LoadoutId, y.Seed, y.Outcome) && x.Unlocked.SequenceEqual(y.Unlocked);

        public int GetHashCode(ClosedVoyage? obj) => obj?.Seed.GetHashCode() ?? 0;
    }
}
