using Xunit;

namespace Bloom.SfxGen.Tests;

/// <summary>The struck materials, the plucked string, the crackle and the filters added for them.</summary>
public class SourcesTests
{
    private const double Rate = 88200;

    private static Recipe Sound(string layer, string family = "ship") => RecipeFiles.Parse($$"""
        {
          "id": "test", "family": "{{family}}", "description": "A sound made for a test.", "level": -6,
          "layers": [ {{layer}} ]
        }
        """, "test.json");

    private static Stats Measure(string layer) => Analysis.Measure(Synth.Render(Sound(layer)).Samples);

    private static double RmsDb(IEnumerable<double> x) => Db.FromPower(x.Select(v => v * v).Average());

    [Theory]
    [InlineData("wood")]
    [InlineData("tine")]
    [InlineData("metal")]
    [InlineData("glass")]
    [InlineData("skin")]
    public void AStruckMaterialSoundsAtItsNote(string material)
    {
        var stats = Measure($$"""{ "wave": "{{material}}", "note": 1000, "decay": 400, "hardness": 0.3 }""");
        Assert.Equal(1000, stats.PitchHz, 5.0);
    }

    [Fact]
    public void AStruckMaterialRingsForItsDecay()
    {
        var x = Analysis.ToDoubles(Synth.Render(Sound("""{ "wave": "glass", "note": 1000, "decay": 600, "hardness": 0 }""")).Samples);
        double Level(double ms) => RmsDb(x.Skip((int)(ms / 1000 * Synth.OutputRate)).Take(441));

        // 60 dB in 600 ms is 20 dB in 200 ms.
        Assert.Equal(20, Level(100) - Level(300), 2.0);
    }

    [Fact]
    public void AHarderStrikeIsBrighter()
    {
        double soft = Measure("""{ "wave": "metal", "note": 800, "decay": 400, "hardness": 0.1 }""").CentroidHz;
        double hard = Measure("""{ "wave": "metal", "note": 800, "decay": 400, "hardness": 0.9 }""").CentroidHz;
        Assert.True(hard > soft * 1.5, $"A hard strike is centred at {hard:0} Hz and a soft one at {soft:0} Hz.");
    }

    [Theory]
    [InlineData(440.0)]
    [InlineData(1174.66)]
    public void APluckedStringIsInTune(double hz)
    {
        var stats = Measure($$"""{ "wave": "string", "note": {{hz}}, "decay": 600, "hardness": 0.3 }""");
        Assert.Equal(hz, stats.PitchHz, hz * 0.003);
    }

    [Fact]
    public void ACrackleClicksAsOftenAsItsDensityAsks()
    {
        var x = Analysis.ToDoubles(Synth.Render(Sound("""
            { "wave": "crackle", "attack": 1, "hold": 1000, "decay": 1, "density": { "from": 300 } }
            """)).Samples);

        // At the output rate a click is spread over a few samples by the rate halving; count the starts.
        double peak = x.Max(Math.Abs);
        int clicks = 0;
        bool inside = false;
        foreach (double v in x)
        {
            bool loud = Math.Abs(v) > peak * 0.1;
            if (loud && !inside)
            {
                clicks++;
            }

            inside = loud;
        }

        Assert.InRange(clicks, 240, 360);
    }

    [Fact]
    public void ABodyTunesNoiseToItsNote()
    {
        var stats = Measure("""
            { "wave": "noise", "attack": 20, "decay": 200,
              "body": { "material": "glass", "note": 1500, "decay": 300 } }
            """);
        Assert.Equal(1500, stats.PitchHz, 5.0);
    }

    [Fact]
    public void AResonatorRingsAtAmplitudeOneAfterAClick()
    {
        var resonator = new Resonator(1000, 10, Rate);
        double top = 0;
        for (int i = 0; i < (int)(Rate / 100); i++)
        {
            top = Math.Max(top, Math.Abs(resonator.Process(i == 0 ? 1 : 0)));
        }

        Assert.Equal(1, top, 0.01);
    }

    [Fact]
    public void TheLadderFallsSteeplyAboveItsCutoffAndStaysStableAtFullResonance()
    {
        double Gain(double hz, double q)
        {
            var ladder = new Ladder(Rate);
            ladder.Set(1000, q);
            var y = Enumerable.Range(0, (int)(Rate * 0.5)).Select(i => ladder.Process(0.1 * Math.Sin(2 * Math.PI * hz * i / Rate)));
            return RmsDb(y.Skip((int)(Rate * 0.1))) - RmsDb(Enumerable.Range(0, (int)(Rate * 0.4)).Select(i => 0.1 * Math.Sin(2 * Math.PI * hz * i / Rate)));
        }

        Assert.True(Gain(4000, 0.707) < -30, "Two octaves above the cutoff should be far down.");
        Assert.True(Gain(250, 0.707) > -3, "Two octaves below the cutoff should pass.");
        Assert.True(Gain(1000, 12) > Gain(1000, 0.707) + 6, "Resonance should ring at the cutoff.");
        Assert.True(Gain(1000, 12) < 30, "Full resonance must not run away.");
    }

    [Fact]
    public void AVowelPutsItsEnergyWhereThatVowelHasIt()
    {
        // An open "a" resonates near 800 and 1150 Hz; a closed "u" near 325 and 700.
        double a = Measure("""{ "wave": "saw", "note": 110, "attack": 10, "hold": 300, "decay": 50, "formant": { "from": "a" } }""").CentroidHz;
        double u = Measure("""{ "wave": "saw", "note": 110, "attack": 10, "hold": 300, "decay": 50, "formant": { "from": "u" } }""").CentroidHz;
        Assert.True(a > u * 1.3, $"The a is centred at {a:0} Hz and the u at {u:0} Hz.");
    }

    [Fact]
    public void ADriftedSoundStillRendersTheSameEveryTime()
    {
        var recipe = Sound("""
            { "wave": "saw", "note": "D5", "decay": 300, "unison": { "voices": 3, "detune": 10 },
              "drift": { "cents": 20 }, "lfo": { "rate": 6, "pitch": 30, "amp": 0.3 } }
            """);
        Assert.Equal(Synth.Render(recipe).Samples, Synth.Render(recipe).Samples);
    }

    [Fact]
    public void AReverbWaitsForItsPredelay()
    {
        var x = new double[(int)(Rate * 0.2)];
        x[0] = 1;
        ReverbEffect.Apply(x, new Reverb { Decay = 300, Mix = 0, Predelay = 50 }, Rate);
        Assert.All(x.Skip(1).Take((int)(Rate * 0.05)), v => Assert.Equal(0, v));
        Assert.Contains(x.Skip((int)(Rate * 0.05)), v => v != 0);
    }

    [Theory]
    [InlineData("""{ "wave": "sine", "note": "D5", "decay": 100, "hardness": 0.9 }""", "hardness")]
    [InlineData("""{ "wave": "noise", "decay": 100, "density": { "from": 100 } }""", "density")]
    [InlineData("""{ "wave": "crackle", "note": "D5", "decay": 100 }""", "no note")]
    [InlineData("""{ "wave": "saw", "note": "D5", "decay": 100, "lfo": { "rate": 5 } }""", "lfo needs")]
    [InlineData("""{ "wave": "saw", "note": "D5", "decay": 100, "lfo": { "rate": 5, "cutoff": 1 } }""", "lfo.cutoff needs a filter")]
    [InlineData("""{ "wave": "noise", "decay": 100, "ring": { "ratio": 2 } }""", "ring")]
    [InlineData("""{ "wave": "noise", "decay": 100, "body": { "material": "brass", "note": "D5", "decay": 100 } }""", "material")]
    [InlineData("""{ "wave": "saw", "note": "D5", "decay": 100, "formant": { "from": "y" } }""", "formant")]
    public void AMisusedNewFieldIsRefused(string layer, string names)
    {
        var error = Assert.Throws<RecipeException>(() => Sound(layer));
        Assert.Contains(names, error.Message, StringComparison.Ordinal);
    }
}
