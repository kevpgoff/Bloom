using System.Text;
using Bloom.Core.Rng;

namespace Bloom.SfxGen;

/// <summary>One rendered file: 16-bit mono samples at <see cref="Synth.OutputRate"/>.</summary>
/// <param name="Samples">The sound.</param>
/// <param name="Clipped">Whether the level asked for pushed a sample past full scale. The checks reject that.</param>
internal sealed record Rendered(short[] Samples, bool Clipped);

/// <summary>How one variant differs from the recipe: semitones of pitch, a factor on times, octaves of tone.</summary>
internal readonly record struct Variation(double Pitch, double Time, double Tone)
{
    public static readonly Variation None = new(0, 1, 0);

    /// <summary>
    /// The variants are spread evenly across each range, and a seeded shuffle per range decides which variant
    /// gets which step. So no two variants are alike, and the same recipe always gives the same files.
    /// </summary>
    public static Variation For(Recipe recipe, int variant)
    {
        if (recipe.Variants <= 1 || recipe.Vary is not { } vary)
        {
            return None;
        }

        double Offset(double range, ulong stream)
        {
            if (range == 0)
            {
                return 0;
            }

            var order = Enumerable.Range(0, recipe.Variants).ToList();
            new Pcg32(Synth.SeedOf(recipe), stream).Shuffle(order);
            return (((double)order[variant] / (recipe.Variants - 1) * 2) - 1) * range;
        }

        return new Variation(Offset(vary.Pitch, 101), 1 + Offset(vary.Time, 102), Offset(vary.Tone, 103));
    }
}

/// <summary>
/// Turns a <see cref="Recipe"/> into sound. Every note of every layer runs its voices, filters, envelope and gain
/// (see <see cref="AddLayer"/>); the sum runs saturation, high-pass, low-pass, echo and reverb. All of that happens at twice the output rate,
/// which is then halved. Last, the file is faded at both ends and scaled to the recipe's level.
/// </summary>
/// <remarks>
/// Nothing here reads a clock or an unseeded random source, so a recipe renders to the same samples every time
/// on one machine. Another machine's math library may differ in the last bit of a sine, which can move a sample
/// by one step.
/// </remarks>
internal static class Synth
{
    public const int OutputRate = 44100;
    private const int Oversampling = 2;
    private const double Rate = OutputRate * Oversampling;
    private const double FadeInMs = 0.5;
    private const double PadMs = 2;

    public static string FileName(Recipe recipe, int variant) =>
        recipe.Variants == 1 ? $"{recipe.Id}.wav" : $"{recipe.Id}_{variant + 1}.wav";

    public static Rendered Render(Recipe recipe, int variant = 0)
    {
        var x = RenderUnscaled(recipe, variant);
        double loudness = Analysis.Loudness(x, phone: true);
        if (double.IsNegativeInfinity(loudness) || double.IsNaN(loudness))
        {
            throw new RecipeException($"{recipe.Id}: the sound is silent on a phone, so it has no level to set.");
        }

        double gain = Db.ToGain(Analysis.MusicOnPhone + recipe.Level - loudness);
        var samples = new short[x.Length];
        bool clipped = false;
        for (int i = 0; i < x.Length; i++)
        {
            double v = x[i] * gain;
            clipped |= Math.Abs(v) > 1;
            samples[i] = (short)Math.Round(Math.Clamp(v, -1, 1) * short.MaxValue);
        }

        return new Rendered(samples, clipped);
    }

    /// <summary>The length of the file. It is worked out from the recipe, never found by listening for silence.</summary>
    public static double LengthMs(Recipe recipe, Variation v)
    {
        double end = recipe.Layers.Max(l =>
            l.At + (((l.Notes?.Count ?? 1) - 1) * l.Step) + l.Attack + ((l.Hold + l.Decay + (l.Body?.Decay ?? 0)) * v.Time));
        double tail = 0;
        if (recipe.Master.Echo is { } echo)
        {
            tail = Math.Max(tail, EchoEffect.TailMs(echo));
        }

        if (recipe.Master.Reverb is { } reverb)
        {
            tail = Math.Max(tail, reverb.Predelay + reverb.Decay);
        }

        return end + tail + PadMs;
    }

    /// <summary>A stable number for a recipe: an FNV-1a hash of its id, plus its seed.</summary>
    public static ulong SeedOf(Recipe recipe)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(recipe.Id))
        {
            hash = unchecked((hash ^ b) * 1099511628211UL);
        }

        return unchecked(hash + (ulong)recipe.Seed);
    }

    private static double[] RenderUnscaled(Recipe recipe, int variant)
    {
        var v = Variation.For(recipe, variant);
        int frames = (int)Math.Ceiling(LengthMs(recipe, v) / 1000 * OutputRate);
        var mix = new double[frames * Oversampling];
        for (int i = 0; i < recipe.Layers.Count; i++)
        {
            AddLayer(mix, recipe.Layers[i], v, SeedOf(recipe), (ulong)((i * 16) + variant + 1));
        }

        ApplyMaster(mix, recipe.Master);
        var x = HalfBand.Decimate(mix);
        Fade(x, recipe.Master.FadeOut);
        return x;
    }

    /// <summary>
    /// Adds one layer to the mix. For each note, every sample runs: the voices at their pitch (the note, its glide,
    /// the wobble and each voice's detune and drift), the ring modulator, the filter, the vowel filter, the
    /// envelope and gain, and last the body, which rings on after the envelope ends.
    /// </summary>
    /// <param name="stream">Picks this layer's own stream of random numbers, so that layers do not share noise.</param>
    private static void AddLayer(double[] mix, Layer layer, Variation v, ulong seed, ulong stream)
    {
        var noise = new Pcg32(seed, stream);
        IReadOnlyList<Note> notes = layer.Notes ?? [layer.Note ?? new Note(0)];
        double attack = layer.Attack / 1000;
        double hold = layer.Hold / 1000 * v.Time;
        double decay = layer.Decay / 1000 * v.Time;
        double sounding = attack + hold + decay;
        double ringOn = layer.Body is { } b ? b.Decay / 1000 * v.Time : 0;
        int count = (int)Math.Ceiling((sounding + ringOn) * Rate);
        double gain = Db.ToGain(layer.Gain);
        double tone = Math.Pow(2, v.Tone);
        double pitchFactor = Math.Pow(2, v.Pitch / 12);

        int voices = layer.Tonal ? layer.Unison?.Voices ?? 1 : 1;

        // A variant's change of tone moves a strike's hardness by the same amount: one octave of brightness is a fifth of the scale.
        double hardness = Math.Clamp(layer.Hardness + (v.Tone / 5), 0, 1);
        var detune = new double[voices];
        for (int u = 0; u < voices; u++)
        {
            double cents = voices == 1 ? 0 : (((double)u / (voices - 1)) - 0.5) * layer.Unison!.Detune;
            detune[u] = Math.Pow(2, cents / 1200);
        }

        // A ringing wave fades out over the last part of its ring, so that it never stops with a click.
        double fade = Math.Max(0.002, 0.15 * decay);

        for (int n = 0; n < notes.Count; n++)
        {
            double f0 = notes[n].Hz * pitchFactor;
            int start = (int)Math.Round((layer.At + (n * layer.Step)) / 1000 * Rate);
            var voice = new IVoice[voices];
            var drift = new Wander?[voices];
            for (int u = 0; u < voices; u++)
            {
                voice[u] = layer.Wave switch
                {
                    Wave.String => new StringVoice(f0 * detune[u], hardness, decay, Rate, noise),
                    _ when Materials.IsMaterial(layer.Wave) =>
                        new StruckVoice(Materials.Of(layer.Wave), hardness, decay, Rate, noise),
                    _ => new OscillatorVoice(layer.Wave, layer.Fm, Rate),
                };
                if (layer.Drift is { } d)
                {
                    drift[u] = new Wander(new Pcg32(seed, stream + (ulong)(1000 + (n * 8) + u)), d.Rate, Rate);
                }
            }

            var filter = layer.Filter is { Type: not FilterType.Ladder } ? new Svf(Rate) : null;
            var ladder = layer.Filter is { Type: FilterType.Ladder } ? new Ladder(Rate) : null;
            var formant = layer.Formant is { } fs ? new FormantFilter(fs, (fs.Time ?? layer.Decay) / 1000 * v.Time, Rate) : null;
            var body = layer.Body is { } bs ? new BodyFilter(bs, pitchFactor, v.Time, Rate) : null;
            double lfoPhase = 0;
            double ringPhase = 0;

            for (int i = 0; i < count && start + i < mix.Length; i++)
            {
                double t = i / Rate;
                double envelope = Envelope(layer, t, attack, hold, decay, sounding, fade);
                if (t >= sounding && body is null)
                {
                    break;
                }

                double wobble = 0;
                if (layer.Lfo is { } lfo)
                {
                    wobble = Math.Sin(2 * Math.PI * lfoPhase);
                    double rate = Shape.Sweep(lfo.Rate, lfo.To ?? lfo.Rate, t, (lfo.Time ?? layer.Decay) / 1000 * v.Time, lfo.Curve);
                    lfoPhase = OscillatorVoice.Wrap(lfoPhase + (rate / Rate));
                    envelope *= 1 - (lfo.Amp * 0.5 * (1 - Math.Cos(2 * Math.PI * lfoPhase)));
                }

                double s = 0;
                double f = f0;
                if (layer.Wave == Wave.Noise)
                {
                    s = (noise.NextUInt() / 2147483648.0) - 1;
                }
                else if (layer.Wave == Wave.Crackle)
                {
                    var density = layer.Density;
                    double perSecond = density is null ? 200
                        : Shape.Sweep(density.From, density.To ?? density.From, t, (density.Time ?? layer.Decay) / 1000 * v.Time, density.Curve);
                    if (noise.NextUInt() / 4294967296.0 < perSecond / Rate)
                    {
                        // Each click has its own size and sign, as seeds or sparks would.
                        double size = 0.3 + (0.7 * (noise.NextUInt() / 4294967296.0));
                        s = (noise.NextUInt() & 1) == 0 ? size : -size;
                    }
                }
                else
                {
                    double semitones = 0;
                    if (layer.Pitch is { } glide)
                    {
                        semitones += Shape.Sweep(glide.From, glide.To, t, glide.Time / 1000 * v.Time, glide.Curve);
                    }

                    if (layer.Lfo is { Pitch: > 0 } vibrato)
                    {
                        semitones += vibrato.Pitch / 100 * wobble;
                    }

                    f = f0 * Math.Pow(2, semitones / 12);
                    double index = layer.Fm is { } fm
                        ? Shape.Sweep(fm.Index, fm.To, t, (fm.Time ?? layer.Decay) / 1000 * v.Time, fm.Curve) * tone
                        : 0;
                    for (int u = 0; u < voices; u++)
                    {
                        double fu = f * detune[u];
                        if (drift[u] is { } wander)
                        {
                            fu *= Math.Pow(2, layer.Drift!.Cents * wander.At(i) / 1200);
                        }

                        if (voice[u] is OscillatorVoice osc)
                        {
                            osc.Index = index;
                        }

                        s += voice[u].Next(fu);
                    }

                    s /= voices;
                }

                if (layer.Ring is { } ring)
                {
                    s *= 1 - ring.Amount + (ring.Amount * Math.Sin(2 * Math.PI * ringPhase));
                    ringPhase = OscillatorVoice.Wrap(ringPhase + (f * ring.Ratio / Rate));
                }

                if (layer.Filter is { } spec)
                {
                    // The cutoff moves evenly in octaves, which is how a sweep is heard.
                    double cutoff = Math.Exp(Shape.Sweep(
                        Math.Log(spec.Cutoff), Math.Log(spec.To ?? spec.Cutoff), t, (spec.Time ?? layer.Decay) / 1000 * v.Time, spec.Curve));
                    cutoff *= tone;
                    if (layer.Lfo is { Cutoff: > 0 } sweep)
                    {
                        cutoff *= Math.Pow(2, sweep.Cutoff * wobble);
                    }

                    if (ladder is not null)
                    {
                        ladder.Set(cutoff, spec.Q);
                        s = ladder.Process(s);
                    }
                    else
                    {
                        filter!.Set(cutoff, spec.Q);
                        s = filter.Process(s, spec.Type);
                    }
                }

                if (formant is not null)
                {
                    s = formant.Process(s, t);
                }

                s *= envelope * gain;
                if (body is not null)
                {
                    s = body.Process(s);
                }

                mix[start + i] += s;
            }
        }
    }

    /// <summary>
    /// The layer's level at time <paramref name="t"/>: a rise over the attack, the hold, then the fall. A wave that
    /// rings on by itself already falls, so it gets no fall: it stays at full level and fades out at the very end.
    /// </summary>
    private static double Envelope(Layer layer, double t, double attack, double hold, double decay, double sounding, double fade)
    {
        if (t >= sounding)
        {
            return 0;
        }

        if (t < attack)
        {
            return Shape.Rise(t / attack);
        }

        if (layer.Rings)
        {
            return t < sounding - fade ? 1 : Shape.Rise((sounding - t) / fade);
        }

        return t < attack + hold ? 1 : Shape.Fall((t - attack - hold) / decay, layer.Curve);
    }

    private static void ApplyMaster(double[] x, Master master)
    {
        if (master.Drive > 0)
        {
            double g = Db.ToGain(master.Drive);
            double norm = Math.Tanh(g);
            for (int i = 0; i < x.Length; i++)
            {
                x[i] = Math.Tanh(g * x[i]) / norm;
            }
        }

        var highpass = new Svf(Rate);
        highpass.Set(master.Highpass, 0.707);
        var lowpass = new Svf(Rate);
        lowpass.Set(master.Lowpass, 0.707);
        for (int i = 0; i < x.Length; i++)
        {
            x[i] = lowpass.Process(highpass.Process(x[i], FilterType.Highpass), FilterType.Lowpass);
        }

        if (master.Echo is { } echo)
        {
            EchoEffect.Apply(x, echo, Rate);
        }

        if (master.Reverb is { } reverb)
        {
            ReverbEffect.Apply(x, reverb, Rate);
        }
    }

    /// <summary>Makes the file start and end at exactly zero, so that it never clicks.</summary>
    private static void Fade(double[] x, double fadeOutMs)
    {
        int fadeIn = Math.Min(x.Length, (int)(FadeInMs / 1000 * OutputRate));
        for (int i = 0; i < fadeIn; i++)
        {
            x[i] *= Shape.Rise((double)i / fadeIn);
        }

        int fadeOut = Math.Min(x.Length, (int)(fadeOutMs / 1000 * OutputRate));
        for (int i = 0; i < fadeOut; i++)
        {
            x[x.Length - 1 - i] *= Shape.Rise((double)i / fadeOut);
        }
    }
}
