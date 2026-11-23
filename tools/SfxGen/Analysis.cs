using System.Globalization;

namespace Bloom.SfxGen;

/// <summary>What can be said about a sound without hearing it. <see cref="Line"/> prints it.</summary>
internal sealed record Stats(
    double Ms,
    double PeakDb,
    double Loudness,
    double PhoneLoss,
    double CentroidHz,
    IReadOnlyList<double> Bands,
    double AttackMs,
    double Fall20Ms,
    double Fall40Ms,
    double PitchHz,
    double DcDb,
    double TailDb,
    string Envelope)
{
    /// <summary>The top of each frequency band in Hz; the last band has no top.</summary>
    public static readonly IReadOnlyList<double> BandEdges = [150, 400, 1000, 2500, 6000];

    public const string Header =
        "sound                    ms    peak   loud  phone  centre  <150/400/1k/2.5k/6k/+   rise  fall20/40  pitch     shape";

    public string Line(string name) => string.Create(CultureInfo.InvariantCulture,
        $"{name,-22} {Ms,5:0}  {PeakDb,6:0.0}  {Loudness,5:0.0}  {-PhoneLoss,5:0.0}  {CentroidHz,6:0}  {string.Join('/', Bands.Select(b => (b * 100).ToString("0", CultureInfo.InvariantCulture))),-22}  {AttackMs,4:0}  {Fall20Ms,4:0}/{Fall40Ms,-4:0}  {Analysis.NoteName(PitchHz),-8}  {Envelope}");
}

/// <summary>
/// Measurements that stand in for ears. The loudness measure is the one the recipes' <c>level</c> is set by.
/// </summary>
internal static class Analysis
{
    /// <summary>
    /// The loudness of the game's music as a phone plays it: the median of the 20 built tracks, measured with
    /// <see cref="Loudness"/>'s weighting over each whole track. A recipe with <c>level</c> 0 is this loud.
    /// </summary>
    public const double MusicOnPhone = -21;

    /// <summary>A phone speaker plays little below this many Hz.</summary>
    public const double PhoneCutoff = 350;

    private const double WindowMs = 100;
    private const double Rate = Synth.OutputRate;

    /// <summary>
    /// How loud a sound is, in LUFS. The standard measure (ITU-R BS.1770) averages over at least 400 ms, which is
    /// longer than most of these sounds, so this takes the loudest 100 ms instead. The sound is weighted the way
    /// the ear hears (the standard's K-weighting) and, with <paramref name="phone"/>, stripped of what a phone
    /// speaker cannot play. A sound shorter than 100 ms reads quieter, as it also sounds.
    /// </summary>
    public static double Loudness(double[] x, bool phone)
    {
        var (shelf, highpass) = KWeighting();
        var speaker = phone ? Biquad.Highpass(PhoneCutoff, Rate) : null;
        var sums = new double[x.Length + 1];
        for (int i = 0; i < x.Length; i++)
        {
            double y = highpass.Process(shelf.Process(x[i]));
            if (speaker is not null)
            {
                y = speaker.Process(y);
            }

            sums[i + 1] = sums[i] + (y * y);
        }

        int window = (int)(WindowMs / 1000 * Rate);
        double best = 0;
        for (int start = 0; start < x.Length; start++)
        {
            best = Math.Max(best, sums[Math.Min(x.Length, start + window)] - sums[start]);
        }

        return -0.691 + Db.FromPower(best / window);
    }

    public static Stats Measure(short[] samples)
    {
        var x = ToDoubles(samples);
        double peak = x.Length == 0 ? 0 : x.Max(Math.Abs);
        double loud = Loudness(x, phone: true);
        var (bands, centroid, pitch) = Spectrum(x);
        var (attack, fall20, fall40, shape) = Envelope(x);
        int tail = Math.Min(x.Length, (int)(0.002 * Rate));
        return new Stats(
            Ms: x.Length / Rate * 1000,
            PeakDb: Db.FromGain(peak),
            Loudness: loud,
            PhoneLoss: Loudness(x, phone: false) - loud,
            CentroidHz: centroid,
            Bands: bands,
            AttackMs: attack,
            Fall20Ms: fall20,
            Fall40Ms: fall40,
            PitchHz: pitch,
            DcDb: Db.FromGain(Math.Abs(x.DefaultIfEmpty(0).Average())),
            TailDb: Db.FromGain(x.Skip(x.Length - tail).DefaultIfEmpty(0).Max(Math.Abs)),
            Envelope: shape);
    }

    public static double[] ToDoubles(short[] samples) => Array.ConvertAll(samples, s => s / 32768.0);

    /// <summary>A frequency as the nearest note and the distance from it in cents, such as "D5 +3".</summary>
    public static string NoteName(double hz)
    {
        if (hz <= 0)
        {
            return "-";
        }

        double midi = 69 + (12 * Math.Log2(hz / 440));
        int nearest = (int)Math.Round(midi);
        int cents = (int)Math.Round((midi - nearest) * 100);
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        string name = $"{names[((nearest % 12) + 12) % 12]}{(nearest / 12) - 1}";
        return cents == 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{name} {cents:+0;-0}");
    }

    /// <summary>The share of the energy in each band, the power-weighted mean frequency, and the strongest frequency.</summary>
    private static (double[] Bands, double Centroid, double Pitch) Spectrum(double[] x)
    {
        int n = 1;
        while (n < Math.Max(2, x.Length))
        {
            n <<= 1;
        }

        var re = new double[n];
        var im = new double[n];
        Array.Copy(x, re, x.Length);
        Fft(re, im);

        var bands = new double[Stats.BandEdges.Count + 1];
        var power = new double[n / 2];
        double total = 0;
        double weighted = 0;
        int loudest = 1;
        for (int k = 1; k < n / 2; k++)
        {
            double hz = k * Rate / n;
            power[k] = (re[k] * re[k]) + (im[k] * im[k]);
            total += power[k];
            weighted += power[k] * hz;
            int band = 0;
            while (band < Stats.BandEdges.Count && hz >= Stats.BandEdges[band])
            {
                band++;
            }

            bands[band] += power[k];
            if (power[k] > power[loudest])
            {
                loudest = k;
            }
        }

        if (total <= 0)
        {
            return (bands, 0, 0);
        }

        for (int b = 0; b < bands.Length; b++)
        {
            bands[b] /= total;
        }

        // The true peak lies between bins; a parabola through the three around it finds it.
        double offset = 0;
        if (loudest > 1 && loudest < (n / 2) - 1 && power[loudest - 1] > 0 && power[loudest + 1] > 0)
        {
            double a = Math.Log(power[loudest - 1]);
            double b = Math.Log(power[loudest]);
            double c = Math.Log(power[loudest + 1]);
            double bend = a - (2 * b) + c;
            offset = bend == 0 ? 0 : 0.5 * (a - c) / bend;
        }

        return (bands, weighted / total, (loudest + offset) * Rate / n);
    }

    /// <summary>When the sound is loudest, how long it then takes to fall 20 and 40 dB, and its outline in 16 steps.</summary>
    private static (double AttackMs, double Fall20Ms, double Fall40Ms, string Shape) Envelope(double[] x)
    {
        const double StepMs = 2;
        int step = (int)(StepMs / 1000 * Rate);
        int count = Math.Max(1, x.Length / step);
        var level = new double[count];
        for (int w = 0; w < count; w++)
        {
            double sum = 0;
            for (int i = w * step; i < Math.Min(x.Length, (w + 1) * step); i++)
            {
                sum += x[i] * x[i];
            }

            level[w] = sum / step;
        }

        int top = Array.IndexOf(level, level.Max());
        double FallTo(double db)
        {
            double floor = level[top] * Math.Pow(10, -db / 10);
            int last = top;
            for (int w = top; w < count; w++)
            {
                if (level[w] > floor)
                {
                    last = w;
                }
            }

            return (last + 1 - top) * StepMs;
        }

        const string Blocks = " ▁▂▃▄▅▆▇█";
        var shape = new char[16];
        for (int s = 0; s < shape.Length; s++)
        {
            int from = s * count / shape.Length;
            int to = Math.Max(from + 1, (s + 1) * count / shape.Length);
            double mean = level.Skip(from).Take(to - from).DefaultIfEmpty(0).Average();
            double db = level[top] <= 0 ? -100 : Db.FromPower(mean / level[top]);

            // Each block is 6 dB; anything more than 48 dB below the loudest part shows as a gap.
            shape[s] = Blocks[Math.Clamp((int)Math.Ceiling((db + 48) / 6), 0, 8)];
        }

        return (top * StepMs, FallTo(20), FallTo(40), new string(shape));
    }

    /// <summary>
    /// The two filters of BS.1770's K-weighting, worked out for this sample rate from the standard's analog
    /// design (the same derivation as libebur128, whose 48 kHz result matches the standard's table).
    /// </summary>
    private static (Biquad Shelf, Biquad Highpass) KWeighting()
    {
        double k = Math.Tan(Math.PI * 1681.974450955533 / Rate);
        const double Q = 0.7071752369554196;
        double vh = Math.Pow(10, 3.999843853973347 / 20);
        double vb = Math.Pow(vh, 0.4996667741545416);
        double a0 = 1 + (k / Q) + (k * k);
        var shelf = new Biquad(
            (vh + (vb * k / Q) + (k * k)) / a0,
            2 * ((k * k) - vh) / a0,
            (vh - (vb * k / Q) + (k * k)) / a0,
            2 * ((k * k) - 1) / a0,
            (1 - (k / Q) + (k * k)) / a0);

        k = Math.Tan(Math.PI * 38.13547087602444 / Rate);
        const double Q2 = 0.5003270373238773;
        a0 = 1 + (k / Q2) + (k * k);
        var highpass = new Biquad(1, -2, 1, 2 * ((k * k) - 1) / a0, (1 - (k / Q2) + (k * k)) / a0);
        return (shelf, highpass);
    }

    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            double wr = Math.Cos(angle);
            double wi = Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1;
                double ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k;
                    int b = a + (len / 2);
                    double tr = (re[b] * cr) - (im[b] * ci);
                    double ti = (re[b] * ci) + (im[b] * cr);
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    double next = (cr * wr) - (ci * wi);
                    ci = (cr * wi) + (ci * wr);
                    cr = next;
                }
            }
        }
    }
}
