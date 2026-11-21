using Bloom.Core.Combat;
using Bloom.Core.Run;
using Bloom.Core.Save;
using static Bloom.Core.Tests.TestContent;

namespace Bloom.Core.Tests;

public class SaveTests
{
    [Fact]
    public void ANewRunSurvivesARoundTrip()
    {
        string json = SaveSerializer.Serialize(NewRun());
        Assert.Equal(json, SaveSerializer.Serialize(SaveSerializer.Deserialize(json)));
    }

    [Fact]
    public void ARunInAFightResumesExactly()
    {
        var engine = new CombatEngine(Db);
        var original = NewRun(seed: 11);
        Fit(original, "lance", 0, 0);
        Fit(original, "plating", 0, 1);
        engine.StartFight(original, "dummy");
        engine.PlayCard(original, original.Combat!.Hand.First(c => c.CardId != "lance").Id);

        var restored = SaveSerializer.Deserialize(SaveSerializer.Serialize(original));

        // The same action on both copies must give the same state, including the shuffle after the reshuffle.
        engine.EndTurn(original);
        engine.EndTurn(restored);
        Assert.Equal(SaveSerializer.Serialize(original), SaveSerializer.Serialize(restored));
    }

    [Fact]
    public void TheSaveIsReadableJson()
    {
        string json = SaveSerializer.Serialize(NewRun());
        Assert.Contains("\"rng\":{\"state\":", json);
        Assert.Contains("\"rotation\":\"R0\"", json);
        Assert.Contains("\"schemaVersion\":1", json);
        Assert.DoesNotContain("\"combat\"", json); // nulls are left out
    }

    [Fact]
    public void AnUnknownSchemaVersionIsRejected()
    {
        string json = SaveSerializer.Serialize(NewRun()).Replace("\"schemaVersion\":1", "\"schemaVersion\":99");
        Assert.Throws<SaveException>(() => SaveSerializer.Deserialize(json));
    }

    [Fact]
    public void AnUnreadableSaveIsRejected() =>
        Assert.Throws<SaveException>(() => SaveSerializer.Deserialize("not json"));

    [Fact]
    public void TheSessionSavesAfterEveryAction()
    {
        var store = new MemorySaveStore();
        var session = RunSession.StartNew(Db, store, "test", seed: 7);
        Assert.Equal(1, store.SaveCount);

        session.Do(s => s.Combat.StartFight(s.State, "dummy"));
        Assert.Equal(2, store.SaveCount);

        var resumed = RunSession.TryResume(Db, store);
        Assert.NotNull(resumed?.State.Combat);
        Assert.Equal(store.Json, SaveSerializer.Serialize(resumed!.State));
    }

    [Fact]
    public void AFailedActionIsNotSaved()
    {
        var store = new MemorySaveStore();
        var session = RunSession.StartNew(Db, store, "test", seed: 7);
        int bridge = BridgeId(session.State);
        Assert.Throws<InvalidOperationException>(() => session.Do(s => s.Fitter.Remove(s.State.Ship, bridge)));
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void ThereIsNothingToResumeWithoutASave() =>
        Assert.Null(RunSession.TryResume(Db, new MemorySaveStore()));
}
