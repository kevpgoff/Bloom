namespace Bloom.SfxGen;

/// <summary>The one curve that every envelope and sweep uses.</summary>
internal static class Shape
{
    public const double DefaultCurve = 5;

    /// <summary>
    /// Falls from 1 at <paramref name="x"/> = 0 to exactly 0 at <paramref name="x"/> = 1. With <paramref name="curve"/>
    /// above 0 it falls fast at first, like a plucked string; 0 is a straight line; below 0 it starts slowly.
    /// </summary>
    public static double Fall(double x, double curve)
    {
        if (x <= 0)
        {
            return 1;
        }

        if (x >= 1)
        {
            return 0;
        }

        if (Math.Abs(curve) < 1e-6)
        {
            return 1 - x;
        }

        double end = Math.Exp(-curve);
        return (Math.Exp(-curve * x) - end) / (1 - end);
    }

    /// <summary>A value that moves from <paramref name="from"/> to <paramref name="to"/> over <paramref name="time"/> seconds.</summary>
    public static double Sweep(double from, double to, double t, double time, double curve) =>
        to + ((from - to) * Fall(t / time, curve));

    /// <summary>A smooth rise from 0 to 1 (half a cosine).</summary>
    public static double Rise(double x) => x <= 0 ? 0 : x >= 1 ? 1 : 0.5 * (1 - Math.Cos(Math.PI * x));
}

/// <summary>
/// A 12 dB per octave state-variable filter (Zavalishin's design in Andrew Simper's form). It stays stable while
/// its cutoff moves on every sample, which the sweeps need.
/// </summary>
internal sealed class Svf(double sampleRate)
{
    private double _ic1;
    private double _ic2;
    private double _a1;
    private double _a2;
    private double _a3;
    private double _k;

    public void Set(double cutoff, double q)
    {
        double g = Math.Tan(Math.PI * Math.Clamp(cutoff, 20, 0.45 * sampleRate) / sampleRate);
        _k = 1 / q;
        _a1 = 1 / (1 + (g * (g + _k)));
        _a2 = g * _a1;
        _a3 = g * _a2;
    }

    public double Process(double x, FilterType type)
    {
        double v3 = x - _ic2;
        double v1 = (_a1 * _ic1) + (_a2 * v3);
        double v2 = _ic2 + (_a2 * _ic1) + (_a3 * v3);
        _ic1 = (2 * v1) - _ic1;
        _ic2 = (2 * v2) - _ic2;
        return type switch
        {
            FilterType.Lowpass => v2,
            FilterType.Highpass => x - (_k * v1) - v2,
            _ => _k * v1,
        };
    }
}

/// <summary>A two-pole filter with fixed coefficients, used by the loudness measure.</summary>
internal sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
{
    private double _z1;
    private double _z2;

    /// <summary>A Butterworth high-pass.</summary>
    public static Biquad Highpass(double cutoff, double sampleRate)
    {
        double w = 2 * Math.PI * cutoff / sampleRate;
        double alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
        double cos = Math.Cos(w);
        double a0 = 1 + alpha;
        return new Biquad((1 + cos) / 2 / a0, -(1 + cos) / a0, (1 + cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0);
    }

    public double Process(double x)
    {
        double y = (b0 * x) + _z1;
        _z1 = (b1 * x) - (a1 * y) + _z2;
        _z2 = (b2 * x) - (a2 * y);
        return y;
    }
}

/// <summary>Repeats the sound, each repeat quieter and duller than the last.</summary>
internal static class EchoEffect
{
    public static void Apply(double[] x, Echo echo, double sampleRate)
    {
        int delay = Math.Max(1, (int)Math.Round(echo.Time / 1000 * sampleRate));
        var line = new double[delay];
        double wet = Db.ToGain(echo.Mix);
        double damp = 1 - Math.Exp(-2 * Math.PI * echo.Damp / sampleRate);
        double low = 0;
        int at = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double delayed = line[at];
            low += damp * (delayed - low);
            line[at] = x[i] + (low * echo.Feedback);
            at = (at + 1) % delay;
            x[i] += wet * low;
        }
    }

    /// <summary>How long the repeats take to fall 30 dB below the sound, in milliseconds.</summary>
    public static double TailMs(Echo echo)
    {
        if (echo.Feedback <= 0)
        {
            return echo.Time;
        }

        int repeats = (int)Math.Ceiling(-1.5 / Math.Log10(echo.Feedback));
        return echo.Time * (Math.Min(repeats, 12) + 1);
    }
}

/// <summary>
/// A reverb: four all-pass filters smear the sound, then eight delay lines feed each other through a Hadamard
/// matrix. Each line loses just enough on every pass for the tail to fall 60 dB in the asked time, and each one's
/// length wobbles very slightly, which keeps the tail from ringing like a metal box.
/// </summary>
internal static class ReverbEffect
{
    private static readonly double[] LineMs = [23.1, 27.7, 31.9, 35.3, 41.3, 45.7, 52.1, 57.7];
    private static readonly double[] WobbleHz = [0.31, 0.47, 0.67, 0.83, 0.37, 0.59, 0.73, 0.97];
    private static readonly double[] AllpassMs = [4.7, 6.1, 8.3, 11.9];
    private const double WobbleMs = 0.25;
    private const double AllpassGain = 0.6;

    public static void Apply(double[] x, Reverb reverb, double sampleRate)
    {
        int count = LineMs.Length;
        double wobble = WobbleMs / 1000 * sampleRate;
        var lengths = LineMs.Select(ms => Math.Max(4, ms * reverb.Size / 1000 * sampleRate)).ToArray();
        var lines = lengths.Select(l => new double[(int)(l + wobble) + 4]).ToArray();
        var gains = lengths.Select(l => Math.Pow(10, -3.0 * (l / sampleRate) / (reverb.Decay / 1000))).ToArray();
        var low = new double[count];
        var mixed = new double[count];
        var allpass = AllpassMs.Select(ms => new double[Math.Max(2, (int)Math.Round(ms * reverb.Size / 1000 * sampleRate))]).ToArray();
        var allpassAt = new int[allpass.Length];
        var waiting = new double[Math.Max(1, (int)Math.Round(reverb.Predelay / 1000 * sampleRate))];
        double damp = 1 - Math.Exp(-2 * Math.PI * reverb.Damp / sampleRate);
        double wet = Db.ToGain(reverb.Mix);
        double norm = 1 / Math.Sqrt(count);

        for (int i = 0; i < x.Length; i++)
        {
            double input = reverb.Predelay > 0 ? waiting[i % waiting.Length] : x[i];
            if (reverb.Predelay > 0)
            {
                waiting[i % waiting.Length] = x[i];
            }

            for (int a = 0; a < allpass.Length; a++)
            {
                double delayed = allpass[a][allpassAt[a]];
                double fed = input + (delayed * AllpassGain);
                allpass[a][allpassAt[a]] = fed;
                allpassAt[a] = (allpassAt[a] + 1) % allpass[a].Length;
                input = delayed - (fed * AllpassGain);
            }

            double sum = 0;
            for (int l = 0; l < count; l++)
            {
                double delay = lengths[l] - (wobble * 0.5 * (1 + Math.Sin(2 * Math.PI * WobbleHz[l] * i / sampleRate)));
                low[l] += damp * (Read(lines[l], i - delay) - low[l]);
                mixed[l] = low[l];
                sum += low[l];
            }

            Hadamard(mixed);
            for (int l = 0; l < count; l++)
            {
                lines[l][i % lines[l].Length] = input + (gains[l] * norm * mixed[l]);
            }

            x[i] += wet * norm * sum;
        }
    }

    /// <summary>Reads a circular line between two samples.</summary>
    private static double Read(double[] line, double position)
    {
        if (position < 0)
        {
            return 0;
        }

        int i = (int)position;
        double frac = position - i;
        return (line[i % line.Length] * (1 - frac)) + (line[(i + 1) % line.Length] * frac);
    }

    /// <summary>The unscaled Hadamard transform, in place: every output is every input, added or taken away.</summary>
    private static void Hadamard(double[] v)
    {
        for (int half = 1; half < v.Length; half <<= 1)
        {
            for (int i = 0; i < v.Length; i += half * 2)
            {
                for (int j = i; j < i + half; j++)
                {
                    (v[j], v[j + half]) = (v[j] + v[j + half], v[j] - v[j + half]);
                }
            }
        }
    }
}

/// <summary>Halves the sample rate. Sounds are made at twice the output rate, so that bright tones cannot alias.</summary>
internal static class HalfBand
{
    private const int Taps = 63;
    private const double KaiserBeta = 7.857;
    private static readonly double[] Kernel = Design();

    /// <summary>Returns every second sample of the filtered signal, with no delay.</summary>
    public static double[] Decimate(double[] x)
    {
        int half = Taps / 2;
        var y = new double[x.Length / 2];
        for (int m = 0; m < y.Length; m++)
        {
            int center = 2 * m;
            double sum = 0;
            int from = Math.Max(0, center - half);
            int to = Math.Min(x.Length - 1, center + half);
            for (int i = from; i <= to; i++)
            {
                sum += x[i] * Kernel[i - center + half];
            }

            y[m] = sum;
        }

        return y;
    }

    private static double[] Design()
    {
        int half = Taps / 2;
        var h = new double[Taps];
        double total = 0;
        for (int n = 0; n < Taps; n++)
        {
            int k = n - half;
            double sinc = k == 0 ? 0.5 : Math.Sin(Math.PI * k / 2) / (Math.PI * k);
            double r = (double)k / half;
            h[n] = sinc * BesselI0(KaiserBeta * Math.Sqrt(1 - (r * r))) / BesselI0(KaiserBeta);
            total += h[n];
        }

        for (int n = 0; n < Taps; n++)
        {
            h[n] /= total;
        }

        return h;
    }

    private static double BesselI0(double x)
    {
        double sum = 1;
        double term = 1;
        for (int k = 1; k < 40; k++)
        {
            term *= (x / (2 * k)) * (x / (2 * k));
            sum += term;
        }

        return sum;
    }
}

internal static class Db
{
    public static double ToGain(double db) => Math.Pow(10, db / 20);

    public static double FromGain(double gain) => gain <= 0 ? double.NegativeInfinity : 20 * Math.Log10(gain);

    public static double FromPower(double power) => power <= 0 ? double.NegativeInfinity : 10 * Math.Log10(power);
}
