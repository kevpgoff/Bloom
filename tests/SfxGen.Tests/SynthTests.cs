using Xunit;

namespace Bloom.SfxGen.Tests;

/// <summary>The sound-making code, checked with tiny recipes written here, so that tuning the real ones breaks nothing.</summary>
public class SynthTests
{
    private const string Tone = """
        {
          "id": "tone", "family": "ship", "description": "A plain test tone.", "level": -6,
          "layers": [ { "wave": "sine", "note": 1500, "attack": 5, "hold": 300, "decay": 100 } ]
        }
        """;

    private const string Varied = """
        {
          "id": "varied", "family": "cards", "description": "A test pluck with noise.", "level": -6,
          "variants": 3, "vary": { "pitch": 1, "time": 0.2, "tone": 0.3 },
          "layers": [
            { "wave": "saw", "note": "A5", "decay": 120, "filter": { "cutoff": 3000, "to": 800 } },
            { "wave": "noise", "gain": -12, "decay": 40 }
          ],
          "master": { "reverb": { "decay": 200 }, "echo": { "time": 60 } }
        }
        """;

    private static Recipe Parse(string json, string id) => RecipeFiles.Parse(json, id + ".json");

    [Fact]
    public void TheSameRecipeAlwaysRendersTheSameSamples()
    {
        var recipe = Parse(Varied, "varied");
        for (int v = 0; v < recipe.Variants; v++)
        {
            Assert.Equal(Synth.Render(recipe, v).Samples, Synth.Render(recipe, v).Samples);
        }
    }

    [Fact]
    public void VariantsDifferFromEachOther()
    {
        var recipe = Parse(Varied, "varied");
        var renders = Enumerable.Range(0, 3).Select(v => Synth.Render(recipe, v).Samples).ToList();
        Assert.NotEqual(renders[0], renders[1]);
        Assert.NotEqual(renders[1], renders[2]);
        Assert.NotEqual(renders[0], renders[2]);
    }

    [Fact]
    public void ASeedChangesTheNoiseButNotTheLength()
    {
        var a = Synth.Render(Parse(Varied, "varied")).Samples;
        var b = Synth.Render(Parse(Varied.Replace("\"variants\": 3", "\"seed\": 4, \"variants\": 3"), "varied")).Samples;
        Assert.Equal(a.Length, b.Length);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ASoundStartsAndEndsAtZeroAndHasItsLength()
    {
        var recipe = Parse(Tone, "tone");
        var samples = Synth.Render(recipe).Samples;
        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[^1]);

        // 5 ms attack, 300 ms hold, 100 ms decay and the 2 ms pad.
        Assert.Equal(407, samples.Length * 1000.0 / Synth.OutputRate, 0.1);
    }

    [Fact]
    public void ASoundIsAsLoudAsItsLevelAsks()
    {
        foreach (double level in new[] { -12.0, -6.0, 0.0 })
        {
            var recipe = Parse(Tone.Replace("\"level\": -6", $"\"level\": {level}"), "tone");
            var stats = Analysis.Measure(Synth.Render(recipe).Samples);
            Assert.Equal(Analysis.MusicOnPhone + level, stats.Loudness, 0.1);
        }
    }

    [Fact]
    public void ASineKeepsItsEnergyAtItsPitch()
    {
        var stats = Analysis.Measure(Synth.Render(Parse(Tone, "tone")).Samples);

        // 1500 Hz lies in the fourth band, 1 kHz to 2.5 kHz.
        Assert.True(stats.Bands[3] > 0.99, $"Only {stats.Bands[3]:P1} of the energy is in the band of the tone.");
        Assert.Equal(1500, stats.PitchHz, 3.0);
    }

    [Fact]
    public void ALevelThatWouldClipIsReported()
    {
        // A click this short would need to be far above full scale to be as loud as the music.
        var recipe = Parse("""
            {
              "id": "spike", "family": "ui", "description": "A click far too short for its level.", "level": 6,
              "layers": [ { "wave": "sine", "note": 2000, "attack": 1, "decay": 30 } ]
            }
            """, "spike");
        Assert.True(Synth.Render(recipe).Clipped);
        Assert.Contains(Checks.Sound(recipe), f => f.IsError && f.Message.Contains("peaks", StringComparison.Ordinal));
    }

    [Fact]
    public void ASoundThatAPhoneCannotPlayIsRejected()
    {
        var recipe = Parse("""
            {
              "id": "rumble", "family": "ship", "description": "A low hum with nothing above it.", "level": -6,
              "layers": [ { "wave": "sine", "note": 110, "attack": 20, "hold": 200, "decay": 200 } ],
              "master": { "highpass": 40 }
            }
            """, "rumble");
        Assert.Contains(Checks.Sound(recipe), f => f.IsError && f.Message.Contains("phone speaker", StringComparison.Ordinal));
    }

    [Fact]
    public void ASoundLongerThanItsFamilyAllowsIsRejected()
    {
        var recipe = Parse(Tone.Replace("\"ship\"", "\"ui\""), "tone");
        Assert.Contains(Checks.Sound(recipe), f => f.IsError && f.Message.Contains("ms long", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTestRecipesPassEveryCheck()
    {
        Assert.DoesNotContain(Checks.Sound(Parse(Tone, "tone")), f => f.IsError);
        Assert.DoesNotContain(Checks.Sound(Parse(Varied, "varied")), f => f.IsError);
    }
}
