using Xunit;

namespace Bloom.SfxGen.Tests;

public class DspTests
{
    private static double[] Sine(double hz, double seconds, double rate, double amplitude = 1) =>
        Enumerable.Range(0, (int)(seconds * rate)).Select(i => amplitude * Math.Sin(2 * Math.PI * hz * i / rate)).ToArray();

    private static double RmsDb(IEnumerable<double> x) => Db.FromPower(x.Select(v => v * v).Average());

    [Fact]
    public void TheFallCurveGoesFromOneToExactlyZero()
    {
        foreach (double curve in new[] { -4.0, 0.0, 5.0, 9.0 })
        {
            Assert.Equal(1, Shape.Fall(0, curve));
            Assert.Equal(0, Shape.Fall(1, curve));
            double last = 1;
            for (double x = 0.05; x < 1; x += 0.05)
            {
                double now = Shape.Fall(x, curve);
                Assert.True(now < last, $"The curve {curve} rises at {x}.");
                last = now;
            }
        }

        // A higher curve falls faster at first.
        Assert.True(Shape.Fall(0.2, 8) < Shape.Fall(0.2, 2));
    }

    [Fact]
    public void ASweepStartsAndEndsWhereItIsTold()
    {
        Assert.Equal(7, Shape.Sweep(7, -2, 0, 0.1, 5));
        Assert.Equal(-2, Shape.Sweep(7, -2, 0.1, 0.1, 5));
        Assert.Equal(-2, Shape.Sweep(7, -2, 5, 0.1, 5));
    }

    [Fact]
    public void TheLoudnessMeasureAgreesWithTheStandardOnItsReferenceTone()
    {
        // ITU-R BS.1770: a full-scale 997 Hz sine in one channel reads -3.01 LKFS.
        var tone = Sine(997, 2, Synth.OutputRate);
        Assert.Equal(-3.01, Analysis.Loudness(tone, phone: false), 0.05);
    }

    [Fact]
    public void APhoneLosesTheLowNotesAndKeepsTheHighOnes()
    {
        double low = Analysis.Loudness(Sine(120, 1, Synth.OutputRate), false) - Analysis.Loudness(Sine(120, 1, Synth.OutputRate), true);
        double high = Analysis.Loudness(Sine(2000, 1, Synth.OutputRate), false) - Analysis.Loudness(Sine(2000, 1, Synth.OutputRate), true);
        Assert.True(low > 15, $"A 120 Hz tone lost only {low:0.0} dB.");
        Assert.True(high < 0.2, $"A 2 kHz tone lost {high:0.0} dB.");
    }

    [Fact]
    public void AShortSoundReadsQuieterThanALongOneAtTheSamePeak()
    {
        double brief = Analysis.Loudness(Sine(1000, 0.02, Synth.OutputRate), false);
        double steady = Analysis.Loudness(Sine(1000, 0.5, Synth.OutputRate), false);

        // 20 ms fills a fifth of the 100 ms window: 7 dB less.
        Assert.Equal(7, steady - brief, 0.3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFilterIsThreeDbDownAtItsCutoffAndFallsTwelveDbPerOctave(bool lowpass)
    {
        var type = lowpass ? FilterType.Lowpass : FilterType.Highpass;
        const double Rate = 88200;
        const double Cutoff = 1000;
        double Gain(double hz)
        {
            var filter = new Svf(Rate);
            filter.Set(Cutoff, 0.7071);
            var y = Sine(hz, 0.5, Rate).Select(v => filter.Process(v, type)).Skip((int)(Rate * 0.1));
            return RmsDb(y) - RmsDb(Sine(hz, 0.4, Rate));
        }

        Assert.Equal(-3.0, Gain(Cutoff), 0.1);
        double twoOctavesPast = lowpass ? Gain(Cutoff * 4) : Gain(Cutoff / 4);
        Assert.Equal(-24, twoOctavesPast, 0.5);
        double twoOctavesInside = lowpass ? Gain(Cutoff / 4) : Gain(Cutoff * 4);
        Assert.Equal(0, twoOctavesInside, 0.1);
    }

    [Fact]
    public void ABandpassPassesItsCentreUnchanged()
    {
        const double Rate = 88200;
        var filter = new Svf(Rate);
        filter.Set(2000, 2);
        var y = Sine(2000, 0.5, Rate).Select(v => filter.Process(v, FilterType.Bandpass)).Skip((int)(Rate * 0.1));
        Assert.Equal(RmsDb(Sine(2000, 0.4, Rate)), RmsDb(y), 0.1);
    }

    [Fact]
    public void HalvingTheRateKeepsWhatCanBeHeardAndRemovesWhatWouldAlias()
    {
        const double Rate = 88200;
        var kept = HalfBand.Decimate(Sine(5000, 0.2, Rate));
        Assert.Equal(-3.01, RmsDb(kept.Skip(100).SkipLast(100)), 0.05);

        // 30 kHz cannot exist at 44.1 kHz; left in, it would come back as a 14.1 kHz whistle.
        var removed = HalfBand.Decimate(Sine(30000, 0.2, Rate));
        Assert.True(RmsDb(removed.Skip(100).SkipLast(100)) < -70);
    }

    [Fact]
    public void AReverbTailFallsSixtyDbInTheTimeAsked()
    {
        const double Rate = 88200;
        var x = new double[(int)Rate];
        x[0] = 1;
        ReverbEffect.Apply(x, new Reverb { Decay = 400, Mix = 0, Damp = 18000 }, Rate);

        // Compare the level around 100 ms with the level around 300 ms: half the decay time apart, so 30 dB.
        double Level(double ms) => RmsDb(x.Skip((int)((ms - 40) / 1000 * Rate)).Take((int)(0.08 * Rate)));
        Assert.Equal(30, Level(100) - Level(300), 5.0);
    }

    [Fact]
    public void AnEchoRepeatsAtItsTime()
    {
        const double Rate = 88200;
        var x = new double[(int)(0.3 * Rate)];
        x[0] = 1;
        EchoEffect.Apply(x, new Echo { Time = 100, Feedback = 0.5, Mix = 0, Damp = 18000 }, Rate);
        int first = (int)(0.1 * Rate);
        Assert.True(Math.Abs(x[first]) > 0.3 || Math.Abs(x[first + 1]) > 0.3);
        Assert.Equal(0, x[first / 2]);
    }

    [Fact]
    public void WavFilesRoundTrip()
    {
        short[] samples = [0, 1, -1, short.MaxValue, short.MinValue, 1234, 0];
        byte[] bytes = Wav.Encode(samples);
        Assert.Equal(44 + (samples.Length * 2), bytes.Length);
        Assert.Equal(samples, Wav.Decode(bytes));
        Assert.Throws<InvalidDataException>(() => Wav.Decode([1, 2, 3]));
    }
}
