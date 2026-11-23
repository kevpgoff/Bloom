using Bloom.Core.Rng;

namespace Bloom.SfxGen;

/// <summary>
/// The struck materials. Each is a list of modes: the frequencies at which the object rings, as ratios of its
/// lowest one, with how loud each is and how long it rings compared with the lowest. Real objects ring at
/// frequencies that are not whole multiples of the lowest, which is what makes them sound like wood or glass
/// and not like a synth.
/// </summary>
internal static class Materials
{
    /// <param name="Ratio">The mode's frequency over the lowest mode's.</param>
    /// <param name="Gain">How loud the mode is when the object is struck hard.</param>
    /// <param name="Ring">How long the mode rings, as a fraction of the lowest mode's time.</param>
    internal readonly record struct Mode(double Ratio, double Gain, double Ring);

    /// <summary>
    /// The modes of each material. Wood is a marimba bar, tuned so that its overtones fall near 4 and 10 times
    /// its pitch, and quickly damped. Tine is the steel tongue of a thumb piano. Metal is a free bar or a wind
    /// chime. Glass is a wine glass. Skin is a drum head, a seed pod or a gourd: many modes close together.
    /// </summary>
    public static IReadOnlyList<Mode> Modes(Material material) => material switch
    {
        Material.Wood => [new(1, 1, 1), new(3.93, 0.45, 0.28), new(9.2, 0.25, 0.12), new(16.4, 0.12, 0.06)],
        Material.Tine => [new(1, 1, 1), new(6.267, 0.4, 0.3), new(17.55, 0.15, 0.12)],
        Material.Metal =>
        [
            new(1, 1, 1), new(2.756, 0.75, 0.85), new(5.404, 0.55, 0.65), new(8.933, 0.4, 0.5),
            new(13.34, 0.25, 0.38), new(18.64, 0.15, 0.3),
        ],
        Material.Glass => [new(1, 1, 1), new(2.32, 0.6, 0.75), new(4.25, 0.4, 0.55), new(6.63, 0.25, 0.4), new(9.38, 0.12, 0.3)],
        _ =>
        [
            new(1, 1, 1), new(1.594, 0.8, 0.75), new(2.136, 0.65, 0.6), new(2.296, 0.5, 0.55),
            new(2.653, 0.45, 0.5), new(2.918, 0.35, 0.45), new(3.156, 0.3, 0.4), new(3.501, 0.25, 0.35),
        ],
    };

    public static bool IsMaterial(Wave wave) => wave >= Wave.Wood;

    public static Material Of(Wave wave) => (Material)(wave - Wave.Wood);

    /// <summary>
    /// How much of a mode a strike brings out. A soft mallet is a low-pass at the lowest mode; a hard one reaches
    /// 32 times higher.
    /// </summary>
    public static double Mallet(double ratio, double hardness)
    {
        double reach = ratio / Math.Pow(2, hardness * 5);
        return 1 / Math.Sqrt(1 + (reach * reach * reach * reach));
    }
}

/// <summary>One voice of a layer: it makes the next sample at the frequency it is given.</summary>
internal interface IVoice
{
    double Next(double hz);
}

/// <summary>The plain oscillators: sine (with optional FM), triangle, and band-limited saw and square.</summary>
internal sealed class OscillatorVoice(Wave wave, Fm? fm, double rate) : IVoice
{
    // A saw starts where it crosses zero; the other waves already start at zero or under the attack.
    private double _phase = wave == Wave.Saw ? 0.5 : 0;
    private double _modPhase;

    /// <summary>The FM depth for the next sample, set by the layer as it sweeps.</summary>
    public double Index { get; set; }

    public double Next(double hz)
    {
        double dt = hz / rate;
        double s = wave switch
        {
            Wave.Sine when fm is not null => Math.Sin((2 * Math.PI * _phase) + (Index * Math.Sin(2 * Math.PI * _modPhase))),
            Wave.Sine => Math.Sin(2 * Math.PI * _phase),
            Wave.Triangle => _phase < 0.25 ? 4 * _phase : _phase < 0.75 ? 2 - (4 * _phase) : (4 * _phase) - 4,
            Wave.Saw => (2 * _phase) - 1 - PolyBlep(_phase, dt),
            _ => (_phase < 0.5 ? 1 : -1) + PolyBlep(_phase, dt) - PolyBlep(Wrap(_phase + 0.5), dt),
        };
        _phase = Wrap(_phase + dt);
        if (fm is not null)
        {
            _modPhase = Wrap(_modPhase + (dt * fm.Ratio));
        }

        return s;
    }

    internal static double Wrap(double phase) => phase - Math.Floor(phase);

    /// <summary>Rounds off the jump of a saw or square wave over the two samples around it, which removes most aliasing.</summary>
    private static double PolyBlep(double phase, double dt)
    {
        if (phase < dt)
        {
            double x = phase / dt;
            return x + x - (x * x) - 1;
        }

        if (phase > 1 - dt)
        {
            double x = (phase - 1) / dt;
            return (x * x) + x + x + 1;
        }

        return 0;
    }
}

/// <summary>
/// A struck object: one decaying sine per mode of its material, plus a short burst of noise where the mallet
/// meets it. The lowest mode falls 60 dB in <c>ringSeconds</c>.
/// </summary>
internal sealed class StruckVoice : IVoice
{
    private readonly double[] _ratio;
    private readonly double[] _level;
    private readonly double[] _fall;
    private readonly double[] _phase;
    private readonly double _rate;
    private readonly Pcg32 _noise;
    private readonly double _contact;
    private readonly double _contactFall;
    private double _contactLevel = 1;

    public StruckVoice(Material material, double hardness, double ringSeconds, double rate, Pcg32 noise)
    {
        var modes = Materials.Modes(material);
        _ratio = modes.Select(m => m.Ratio).ToArray();
        _level = modes.Select(m => m.Gain * Materials.Mallet(m.Ratio, hardness)).ToArray();
        _fall = modes.Select(m => Math.Pow(10, -3 / (ringSeconds * m.Ring * rate))).ToArray();
        _phase = new double[modes.Count];
        _rate = rate;
        _noise = noise;

        // A harder mallet makes a louder and shorter click.
        _contact = 0.5 * hardness * hardness;
        double contactSeconds = (0.2 + (1.3 * (1 - hardness))) / 1000;
        _contactFall = Math.Exp(-1 / (contactSeconds * rate));
    }

    public double Next(double hz)
    {
        double s = 0;
        for (int k = 0; k < _ratio.Length; k++)
        {
            double f = hz * _ratio[k];
            if (f < 0.45 * _rate)
            {
                s += _level[k] * Math.Sin(2 * Math.PI * _phase[k]);
            }

            _phase[k] = OscillatorVoice.Wrap(_phase[k] + (f / _rate));
            _level[k] *= _fall[k];
        }

        if (_contact > 0)
        {
            s += _contact * _contactLevel * ((_noise.NextUInt() / 2147483648.0) - 1);
            _contactLevel *= _contactFall;
        }

        return s;
    }
}

/// <summary>
/// A plucked string (the Karplus-Strong method): a burst of noise runs round a delay line one period long, losing
/// a little of its top on every pass, so it settles into a warm, ringing note. A harder pluck starts brighter and
/// stays brighter.
/// </summary>
internal sealed class StringVoice : IVoice
{
    private const double LowestHz = 25;
    private readonly double[] _line;
    private readonly double[] _pluck;
    private readonly double _rate;
    private readonly double _ringSeconds;
    private readonly double _loop;
    private int _at;
    private int _played;
    private double _last;

    public StringVoice(double hz, double hardness, double ringSeconds, double rate, Pcg32 noise)
    {
        _rate = rate;
        _ringSeconds = ringSeconds;
        _line = new double[(int)(rate / LowestHz) + 8];

        // The pluck: one period of noise, low-passed more for a softer pluck, with its ends faded.
        int period = Math.Max(4, (int)Math.Round(rate / Math.Max(LowestHz, hz)));
        _pluck = new double[period];
        double cutoff = Math.Min(0.45 * rate, hz * Math.Pow(2, 1 + (hardness * 5)));
        double smooth = 1 - Math.Exp(-2 * Math.PI * cutoff / rate);
        double low = 0;
        double mean = 0;
        for (int i = 0; i < period; i++)
        {
            low += smooth * (((noise.NextUInt() / 2147483648.0) - 1) - low);
            _pluck[i] = low;
            mean += low / period;
        }

        for (int i = 0; i < period; i++)
        {
            _pluck[i] = (_pluck[i] - mean) * Shape.Rise(Math.Min(1, 2.0 * Math.Min(i, period - 1 - i) / period * 4));
        }

        // The loop's low-pass: 0.5 is the classic two-point average; less is a brighter, steelier string.
        _loop = 0.5 - (0.4 * hardness);
    }

    public double Next(double hz)
    {
        hz = Math.Max(LowestHz, hz);

        // The loop filter delays by _loop samples, so the line is read that much sooner to stay in tune.
        double delay = (_rate / hz) - _loop;
        double read = Read(_at - delay);
        double filtered = ((1 - _loop) * read) + (_loop * _last);
        _last = read;

        double keep = Math.Pow(10, -3 / (_ringSeconds * hz));
        double y = (keep * filtered) + (_played < _pluck.Length ? _pluck[_played] : 0);
        _played++;
        _line[_at % _line.Length] = y;
        _at++;
        return y;
    }

    /// <summary>Reads the line between samples with a cubic (Catmull-Rom) curve.</summary>
    private double Read(double position)
    {
        int i = (int)Math.Floor(position);
        double x = position - i;
        double Get(int n) => n < 0 ? 0 : _line[n % _line.Length];
        double p0 = Get(i - 1);
        double p1 = Get(i);
        double p2 = Get(i + 1);
        double p3 = Get(i + 2);
        return p1 + (0.5 * x * (p2 - p0 + (x * ((2 * p0) - (5 * p1) + (4 * p2) - p3 + (x * ((3 * (p1 - p2)) + p3 - p0))))));
    }
}

/// <summary>A value that wanders smoothly and at random between -1 and 1, changing direction about <c>rate</c> times a second.</summary>
internal sealed class Wander(Pcg32 random, double rate, double sampleRate)
{
    private readonly List<double> _knots = [];

    public double At(int sample)
    {
        double position = sample / sampleRate * rate;
        int k = (int)position;
        while (_knots.Count < k + 2)
        {
            _knots.Add((random.NextUInt() / 2147483648.0) - 1);
        }

        return _knots[k] + ((_knots[k + 1] - _knots[k]) * Shape.Rise(position - k));
    }
}

/// <summary>
/// The 24 dB per octave low-pass of the old analog synths: four smoothing stages in a row, with the output fed
/// back to the input for resonance. The input passes through a soft saturation, which warms the tone and keeps
/// the resonance from running away.
/// </summary>
internal sealed class Ladder(double sampleRate)
{
    private double _s1;
    private double _s2;
    private double _s3;
    private double _s4;
    private double _g;
    private double _k;

    /// <param name="q">0.707 is no resonance; higher rings at the cutoff, and 12 nearly sings on its own.</param>
    public void Set(double cutoff, double q)
    {
        _g = 1 - Math.Exp(-2 * Math.PI * Math.Clamp(cutoff, 20, 0.2 * sampleRate) / sampleRate);
        _k = Math.Clamp(4 * (1 - (0.707 / q)), 0, 3.9);
    }

    public double Process(double x)
    {
        double u = Math.Tanh(x - (_k * _s4));
        _s1 += _g * (u - _s1);
        _s2 += _g * (_s1 - _s2);
        _s3 += _g * (_s2 - _s3);
        _s4 += _g * (_s3 - _s4);

        // Resonance thins the low end; this gives some of it back.
        return _s4 * (1 + (0.5 * _k));
    }
}

/// <summary>
/// One mode of an object: a two-pole resonator. A click makes it ring as a sine of amplitude 1 that falls 60 dB in
/// the time asked; steady noise makes it hum at its frequency.
/// </summary>
internal sealed class Resonator
{
    private readonly double _a1;
    private readonly double _a2;
    private readonly double _b;
    private double _y1;
    private double _y2;

    public Resonator(double hz, double ringSeconds, double sampleRate)
    {
        double w = 2 * Math.PI * hz / sampleRate;
        double r = Math.Pow(10, -3 / (ringSeconds * sampleRate));
        _a1 = 2 * r * Math.Cos(w);
        _a2 = -r * r;
        _b = Math.Sin(w);
    }

    public double Process(double x)
    {
        double y = (_a1 * _y1) + (_a2 * _y2) + (_b * x);
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}

/// <summary>An object of one material that a layer is played on: one <see cref="Resonator"/> per mode.</summary>
internal sealed class BodyFilter
{
    private readonly Resonator[] _modes;
    private readonly double[] _gains;
    private readonly double _dry;

    public BodyFilter(Body body, double pitchFactor, double timeFactor, double sampleRate)
    {
        var modes = Materials.Modes(body.Material)
            .Where(m => body.Note.Hz * pitchFactor * m.Ratio < 0.45 * sampleRate)
            .ToList();
        _modes = modes.Select(m => new Resonator(body.Note.Hz * pitchFactor * m.Ratio, body.Decay / 1000 * timeFactor * m.Ring, sampleRate)).ToArray();
        _gains = modes.Select(m => m.Gain).ToArray();
        _dry = body.Dry is { } dry ? Db.ToGain(dry) : 0;
    }

    public double Process(double x)
    {
        double y = _dry * x;
        for (int k = 0; k < _modes.Length; k++)
        {
            y += _gains[k] * _modes[k].Process(x);
        }

        return y;
    }
}

/// <summary>
/// The first three resonances of a voice (an alto's, from the classic formant tables) as three band-pass filters,
/// moving from one vowel to another.
/// </summary>
internal sealed class FormantFilter(Formant spec, double seconds, double sampleRate)
{
    /// <summary>For each vowel: the frequency in Hz, the level in dB and the bandwidth in Hz of each resonance.</summary>
    private static readonly IReadOnlyDictionary<Vowel, (double Hz, double Db, double Width)[]> Table =
        new Dictionary<Vowel, (double, double, double)[]>
        {
            [Vowel.A] = [(800, 0, 80), (1150, -4, 90), (2800, -20, 120)],
            [Vowel.E] = [(400, 0, 60), (1600, -24, 80), (2700, -30, 120)],
            [Vowel.I] = [(350, 0, 50), (1700, -20, 100), (2700, -30, 120)],
            [Vowel.O] = [(450, 0, 70), (800, -9, 80), (2830, -16, 100)],
            [Vowel.U] = [(325, 0, 50), (700, -12, 60), (2530, -30, 170)],
        };

    private readonly Svf[] _bands = [new(sampleRate), new(sampleRate), new(sampleRate)];
    private readonly double _shift = Math.Pow(2, spec.Shift / 12);

    public double Process(double x, double t)
    {
        var from = Table[spec.From];
        var to = Table[spec.To ?? spec.From];
        double w = 1 - Shape.Fall(t / seconds, spec.Curve);
        double y = 0;
        for (int b = 0; b < _bands.Length; b++)
        {
            double hz = Math.Exp(Math.Log(from[b].Hz) + ((Math.Log(to[b].Hz) - Math.Log(from[b].Hz)) * w)) * _shift;
            double width = (from[b].Width + ((to[b].Width - from[b].Width) * w)) * _shift;
            double db = from[b].Db + ((to[b].Db - from[b].Db) * w);
            _bands[b].Set(hz, hz / width);
            y += Db.ToGain(db) * _bands[b].Process(x, FilterType.Bandpass);
        }

        return y;
    }
}
