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

    /// <summary>The layers of three recipes that use, between them, every wave and every effect.</summary>
    private static readonly string[] EveryWaveAndEffect =
    [
        """
        { "wave": "sine", "note": "D5", "decay": 200, "fm": { "ratio": 3.5, "index": 2, "to": 0.5 },
          "pitch": { "from": -2, "time": 30 } },
        { "wave": "triangle", "note": "A5", "decay": 200, "unison": { "voices": 3, "detune": 10 },
          "drift": { "cents": 15 } },
        { "wave": "saw", "note": "D5", "decay": 200, "filter": { "type": "ladder", "cutoff": 3000, "to": 600, "q": 4 },
          "lfo": { "rate": 6, "to": 12, "pitch": 20, "amp": 0.3, "cutoff": 1 } },
        { "wave": "square", "note": "G5", "decay": 200, "ring": { "ratio": 1.41 },
          "formant": { "from": "a", "to": "u", "shift": 3 } },
        { "wave": "sine", "notes": ["D6", "A6"], "step": 40, "decay": 80,
          "filter": { "type": "highpass", "cutoff": 500 } }
        """,
        """
        { "wave": "string", "note": "D5", "decay": 300, "hardness": 0.7 },
        { "wave": "wood", "note": "A5", "decay": 200 },
        { "wave": "tine", "note": "D6", "decay": 200 },
        { "wave": "metal", "note": "G5", "decay": 200 },
        { "wave": "glass", "note": "D6", "decay": 200, "unison": { "voices": 2, "detune": 7 } },
        { "wave": "skin", "note": "A4", "decay": 200 }
        """,
        """
        { "wave": "noise", "decay": 150, "filter": { "type": "bandpass", "cutoff": 1500, "q": 1 },
          "body": { "material": "glass", "note": "D6", "decay": 200 } },
        { "wave": "crackle", "decay": 200, "density": { "from": 2000, "to": 100 },
          "body": { "material": "wood", "note": "A5", "decay": 50, "dry": -6 } }
        """,
    ];

    /// <summary>
    /// The checks render each sound only once and the board reuses that render, so rendering must repeat exactly:
    /// nothing may read a clock, an unseeded generator, or state left over from an earlier render. A second pass in
    /// the opposite order would show state carried from one render into the next.
    /// </summary>
    [Fact]
    public void EveryWaveAndEffectRendersTheSameSamplesEveryTime()
    {
        var recipes = EveryWaveAndEffect.Select((layers, i) => Parse($$"""
            {
              "id": "every-{{i}}", "family": "flow", "description": "Every wave and effect.", "level": -6,
              "variants": 2, "vary": { "pitch": 0.5, "time": 0.1, "tone": 0.2 },
              "layers": [ {{layers}} ],
              "master": { "drive": 3, "echo": { "time": 80 }, "reverb": { "decay": 300, "predelay": 20 } }
            }
            """, $"every-{i}")).ToList();
        var renders = recipes.SelectMany(r => Enumerable.Range(0, r.Variants).Select(v => (Recipe: r, Variant: v))).ToList();

        var first = renders.Select(x => Synth.Render(x.Recipe, x.Variant).Samples).ToList();
        var second = Enumerable.Reverse(renders).Select(x => Synth.Render(x.Recipe, x.Variant).Samples).Reverse().ToList();

        for (int i = 0; i < renders.Count; i++)
        {
            Assert.True(first[i].AsSpan().SequenceEqual(second[i]), $"{renders[i].Recipe.Id}, variant {renders[i].Variant + 1}, rendered differently the second time.");
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
