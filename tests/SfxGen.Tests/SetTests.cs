using Xunit;

namespace Bloom.SfxGen.Tests;

/// <summary>The rules that hold a ship's sound set to the base set, checked with tiny recipes written here.</summary>
public class SetTests
{
    /// <summary>A short pluck under any id, family and level.</summary>
    private static Recipe Pluck(string id, string family = "cards", double level = -6, int decay = 100) =>
        RecipeFiles.Parse(
            $$"""
            {
              "id": "{{id}}", "family": "{{family}}", "description": "A test pluck.", "level": {{level}},
              "layers": [ { "wave": "triangle", "note": "D5", "decay": {{decay}} } ]
            }
            """,
            id + ".json");

    private static SoundSet SetOf(params Recipe[] recipes) => new("sun", "Sun", "A test set.", recipes);

    private static readonly IReadOnlyList<Recipe> Base = [Pluck("card-play"), Pluck("turn-start", family: "flow", level: -8)];

    private static IEnumerable<string> Errors(SoundSet set) =>
        Checks.Set(set, Base).Where(f => f.IsError).Select(f => f.ToString());

    [Fact]
    public void AShipsVersionOfItsOwnSoundPasses() => Assert.Empty(Errors(SetOf(Pluck("card-play"))));

    [Fact]
    public void ASoundEveryShipSharesMayNotBeInASet()
    {
        string error = Assert.Single(Errors(SetOf(Pluck("turn-start", family: "flow", level: -8))));
        Assert.StartsWith("error: sun/turn-start:", error, StringComparison.Ordinal);
        Assert.Contains("every ship shares", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASetsSoundKeepsTheLevelOfTheBaseSound()
    {
        string error = Assert.Single(Errors(SetOf(Pluck("card-play", level: -3))));
        Assert.Contains("level", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASetsSoundKeepsTheFamilyOfTheBaseSound()
    {
        string error = Assert.Single(Errors(SetOf(Pluck("card-play", family: "ship"))));
        Assert.Contains("family", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASetsSoundMustStandInForABaseSound()
    {
        // "hit-light" is one of a ship's own sounds, but this base set has none.
        string error = Assert.Single(Errors(SetOf(Pluck("hit-light", family: "attack"))));
        Assert.Contains("base set", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASetsSoundStillHasToPassTheChecksOnASound()
    {
        // The cards family allows 700 ms at most.
        string error = Assert.Single(Errors(SetOf(Pluck("card-play", decay: 900))));
        Assert.StartsWith("error: sun/card-play:", error, StringComparison.Ordinal);
        Assert.Contains("ms long", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALengthFarFromTheBaseSoundsIsOnlyAWarning()
    {
        var found = Checks.Set(SetOf(Pluck("card-play", decay: 400)), Base);
        Assert.DoesNotContain(found, f => f.IsError);
        Assert.Contains(found, f => f.Sound == "sun/card-play" && f.Message.Contains("far from the base sound", StringComparison.Ordinal));
    }

    [Fact]
    public void ASetThatLacksSomeOfAShipsSoundsIsOnlyWarnedAbout()
    {
        var found = Checks.Set(SetOf(Pluck("card-play")), Base);
        var warning = Assert.Single(found, f => f.Sound == "sun");
        Assert.False(warning.IsError);
        Assert.Contains("hit-light", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("card-play,", warning.Message, StringComparison.Ordinal);
        Assert.Equal(SoundSets.ShipSounds.Count - 1, SoundSets.Missing(SetOf(Pluck("card-play"))).Count);
    }

    [Fact]
    public void ASetAndATakeMayNotShareAName()
    {
        var take = new Take("sun", "Sun", "A test take.", []);
        string error = Assert.Single(Checks.Names([SetOf()], [take])).ToString();
        Assert.Contains("both a set and a take", error, StringComparison.Ordinal);
        Assert.Empty(Checks.Names([SetOf()], [take with { Name = "moon" }]));
    }

    [Fact]
    public void ASetIsReadFromAFolderLikeATakes()
    {
        string dir = Directory.CreateTempSubdirectory("sfxgen-sets").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(dir, "sun")).FullName;
            File.WriteAllText(Path.Combine(folder, "about.json"), """{ "title": "Sun", "description": "A test set." }""");
            File.WriteAllText(
                Path.Combine(folder, "card-play.json"),
                """{ "id": "card-play", "family": "cards", "description": "A test pluck.", "level": -6, "layers": [ { "wave": "triangle", "note": "D5", "decay": 100 } ] }""");

            var set = Assert.Single(SoundSets.LoadAll(dir));
            Assert.Equal("sun", set.Name);
            Assert.Equal("Sun", set.Title);
            Assert.Equal("card-play", Assert.Single(set.Recipes).Id);

            File.Delete(Path.Combine(folder, "about.json"));
            var error = Assert.Throws<RecipeException>(() => SoundSets.LoadAll(dir));
            Assert.StartsWith("sets/sun:", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheRenderedFilesOfASetAreCheckedInItsOwnFolder()
    {
        string dir = Directory.CreateTempSubdirectory("sfxgen-out").FullName;
        try
        {
            var set = SetOf(Pluck("card-play"));

            // Nothing is rendered yet, and a folder that no set makes is in the way.
            Directory.CreateDirectory(Path.Combine(dir, "moon"));
            var found = Checks.SetFiles([set], dir).Select(f => f.ToString()).ToList();
            Assert.Contains(found, f => f.StartsWith("error: sun/card-play.wav: the file is missing", StringComparison.Ordinal));
            Assert.Contains(found, f => f.StartsWith("error: moon/: no set", StringComparison.Ordinal));

            // With the file rendered, only Godot's import of it is still missing.
            Directory.Delete(Path.Combine(dir, "moon"));
            string folder = Directory.CreateDirectory(Path.Combine(dir, "sun")).FullName;
            File.WriteAllBytes(Path.Combine(folder, "card-play.wav"), Wav.Encode(Synth.Render(set.Recipes[0]).Samples));
            string left = Assert.Single(Checks.SetFiles([set], dir)).ToString();
            Assert.StartsWith("error: sun/card-play.wav: Godot has not imported", left, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AShipThatNamesASetNobodyMadeIsAnError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sfxgen-ships-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """[{ "id": "seedling" }, { "id": "heliotrope", "soundSet": "sun" }, { "id": "comb", "soundSet": "wax" }]""");
            string error = Assert.Single(Checks.Ships([SetOf()], path)).ToString();
            Assert.Contains("'comb'", error, StringComparison.Ordinal);
            Assert.Contains("'wax'", error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
