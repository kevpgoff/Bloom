using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Bloom.SfxGen;

/// <summary>
/// What a layer is made of. The first four are plain oscillators. <see cref="Noise"/> and <see cref="Crackle"/> have
/// no pitch. <see cref="String"/> is a plucked string, and the last five are struck objects (see
/// <see cref="Materials"/>): these ring on by themselves, so their <c>decay</c> is how long they ring.
/// </summary>
internal enum Wave
{
    Sine,
    Triangle,
    Saw,
    Square,
    Noise,
    Crackle,
    String,
    Wood,
    Tine,
    Metal,
    Glass,
    Skin,
}

/// <summary>The materials of the struck waves and of a <see cref="Body"/>. <see cref="Materials"/> describes each.</summary>
internal enum Material
{
    Wood,
    Tine,
    Metal,
    Glass,
    Skin,
}

internal enum FilterType
{
    Lowpass,
    Highpass,
    Bandpass,

    /// <summary>A 24 dB per octave low-pass with a soft saturation, after the filter of the old analog synths.</summary>
    Ladder,
}

/// <summary>The five vowels that a <see cref="Formant"/> filter can shape a sound into.</summary>
internal enum Vowel
{
    A,
    E,
    I,
    O,
    U,
}

/// <summary>A pitch in Hz. A recipe writes it as a note name ("D5", "F#4", "Bb3"; A4 is 440 Hz) or as a number of Hz.</summary>
[JsonConverter(typeof(NoteJsonConverter))]
internal readonly partial record struct Note(double Hz)
{
    public static Note Parse(string text)
    {
        var match = NamePattern().Match(text);
        if (!match.Success)
        {
            throw new FormatException($"\"{text}\" is not a note name such as D5, F#4 or Bb3.");
        }

        int semitone = match.Groups[1].Value[0] switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            _ => 11,
        };
        semitone += match.Groups[2].Value switch
        {
            "#" => 1,
            "b" => -1,
            _ => 0,
        };
        int octave = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

        // MIDI numbering: C4 is 60 and A4 is 69.
        int midi = ((octave + 1) * 12) + semitone;
        return new Note(440.0 * Math.Pow(2.0, (midi - 69) / 12.0));
    }

    [GeneratedRegex("^([A-G])([#b]?)(-?[0-9])$")]
    private static partial Regex NamePattern();
}

internal sealed class NoteJsonConverter : JsonConverter<Note>
{
    public override Note Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return new Note(reader.GetDouble());
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            try
            {
                return Note.Parse(reader.GetString()!);
            }
            catch (FormatException e)
            {
                throw new JsonException(e.Message);
            }
        }

        throw new JsonException("A note is a name such as \"D5\" or a number of Hz.");
    }

    public override void Write(Utf8JsonWriter writer, Note value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Hz);
}

/// <summary>
/// One sound. Up to six <see cref="Layers"/> are added together and then pass through <see cref="Master"/>.
/// The format is documented in <c>tools/SfxGen/README.md</c>. Times are milliseconds, levels are dB.
/// </summary>
internal sealed record Recipe
{
    /// <summary>The file name without ".json", and the name of the rendered file.</summary>
    public required string Id { get; init; }

    /// <summary>One of <see cref="Families.All"/>. It groups the sound board and sets the longest allowed length.</summary>
    public required string Family { get; init; }

    /// <summary>One line that says what the sound is and when it plays. The sound board shows it.</summary>
    public required string Description { get; init; }

    /// <summary>How loud the sound is on a phone, in dB relative to the music. See <see cref="Analysis.MusicOnPhone"/>.</summary>
    public required double Level { get; init; }

    public required IReadOnlyList<Layer> Layers { get; init; }

    public Master Master { get; init; } = new();

    /// <summary>How many files to render, each a little different. See <see cref="Vary"/>.</summary>
    public int Variants { get; init; } = 1;

    public Vary? Vary { get; init; }

    /// <summary>Changes the noise and the order of the variants without changing anything else.</summary>
    public int Seed { get; init; }
}

internal sealed record Layer
{
    public required Wave Wave { get; init; }

    /// <summary>The pitch. Required unless the layer is noise or has <see cref="Notes"/>.</summary>
    public Note? Note { get; init; }

    /// <summary>A run of notes. Each one plays the whole layer, <see cref="Step"/> after the one before.</summary>
    public IReadOnlyList<Note>? Notes { get; init; }

    /// <summary>The time between the notes of a run. 0 plays them together, as a chord.</summary>
    public double Step { get; init; } = 80;

    public double Gain { get; init; }

    /// <summary>When the layer starts.</summary>
    public double At { get; init; }

    /// <summary>The rise from silence, at least 1 ms.</summary>
    public double Attack { get; init; } = 2;

    /// <summary>How long the layer stays at full level after the attack.</summary>
    public double Hold { get; init; }

    /// <summary>The fall back to silence.</summary>
    public required double Decay { get; init; }

    /// <summary>The shape of the decay. See <see cref="Shape.Fall"/>.</summary>
    public double Curve { get; init; } = Shape.DefaultCurve;

    public Glide? Pitch { get; init; }

    public Fm? Fm { get; init; }

    public Filter? Filter { get; init; }

    public Unison? Unison { get; init; }

    /// <summary>
    /// For a plucked string or a struck material: how hard the pluck or the strike is, from 0 (a felt mallet, a
    /// fingertip) to 1 (a hard mallet, a pick). A harder strike is brighter and has more of a click.
    /// </summary>
    public double Hardness { get; init; } = 0.5;

    /// <summary>For a crackle: how many clicks it makes per second.</summary>
    public Density? Density { get; init; }

    public Drift? Drift { get; init; }

    public Lfo? Lfo { get; init; }

    public Ring? Ring { get; init; }

    public Formant? Formant { get; init; }

    public Body? Body { get; init; }

    /// <summary>Whether the wave rings on by itself, so that its <see cref="Decay"/> is how long it rings.</summary>
    [JsonIgnore]
    public bool Rings => Wave is Wave.String || Materials.IsMaterial(Wave);

    /// <summary>Whether the wave has a pitch.</summary>
    [JsonIgnore]
    public bool Tonal => Wave is not (Wave.Noise or Wave.Crackle);
}

/// <summary>How many clicks a crackle makes per second, moving from <see cref="From"/> to <see cref="To"/>.</summary>
internal sealed record Density
{
    public required double From { get; init; }

    /// <summary>Defaults to <see cref="From"/>: no change.</summary>
    public double? To { get; init; }

    /// <summary>Defaults to the layer's decay.</summary>
    public double? Time { get; init; }

    public double Curve { get; init; } = Shape.DefaultCurve;
}

/// <summary>
/// The pitch wanders slowly and at random by up to <see cref="Cents"/>, like the oscillators of an old analog
/// synth. Each voice of a unison wanders on its own.
/// </summary>
internal sealed record Drift
{
    public required double Cents { get; init; }

    /// <summary>How many times a second the pitch changes direction, roughly.</summary>
    public double Rate { get; init; } = 4;
}

/// <summary>
/// A slow wobble at <see cref="Rate"/> times a second (moving to <see cref="To"/>): of the pitch by
/// <see cref="Pitch"/> cents (vibrato), of the volume by <see cref="Amp"/> (tremolo), and of the filter's cutoff by
/// <see cref="Cutoff"/> octaves.
/// </summary>
internal sealed record Lfo
{
    public required double Rate { get; init; }

    /// <summary>Defaults to <see cref="Rate"/>: no change.</summary>
    public double? To { get; init; }

    /// <summary>Defaults to the layer's decay.</summary>
    public double? Time { get; init; }

    public double Curve { get; init; }

    public double Pitch { get; init; }

    /// <summary>From 0 (no tremolo) to 1 (the volume falls to silence on every wobble).</summary>
    public double Amp { get; init; }

    public double Cutoff { get; init; }
}

/// <summary>
/// Multiplies the sound by a sine at <see cref="Ratio"/> times its pitch. This makes new partials that are not in
/// tune with the note: a hollow, metallic, alien tone.
/// </summary>
internal sealed record Ring
{
    public required double Ratio { get; init; }

    /// <summary>From 0 (none) to 1 (only the ring-modulated sound).</summary>
    public double Amount { get; init; } = 1;
}

/// <summary>
/// Shapes the sound with the resonances of a voice singing a vowel, moving from <see cref="From"/> to
/// <see cref="To"/>. <see cref="Shift"/> moves every resonance up, in semitones: a smaller throat.
/// </summary>
internal sealed record Formant
{
    public required Vowel From { get; init; }

    /// <summary>Defaults to <see cref="From"/>: no change.</summary>
    public Vowel? To { get; init; }

    /// <summary>Defaults to the layer's decay.</summary>
    public double? Time { get; init; }

    public double Curve { get; init; } = Shape.DefaultCurve;

    public double Shift { get; init; }
}

/// <summary>
/// Puts the layer through the resonances of an object of <see cref="Material"/> tuned to <see cref="Note"/>, as if
/// the sound were played on it: noise becomes a bowed glass, a crackle becomes seeds in a gourd. The object rings
/// for <see cref="Decay"/> after the layer ends. <see cref="Dry"/> mixes some of the sound without the object back in.
/// </summary>
internal sealed record Body
{
    public required Material Material { get; init; }

    public required Note Note { get; init; }

    public required double Decay { get; init; }

    /// <summary>The level of the sound without the object, in dB. Unset, none of it is heard.</summary>
    public double? Dry { get; init; }
}

/// <summary>The pitch moves from <see cref="From"/> to <see cref="To"/> semitones away from the note, over <see cref="Time"/>.</summary>
internal sealed record Glide
{
    public double From { get; init; }

    public double To { get; init; }

    public required double Time { get; init; }

    public double Curve { get; init; } = Shape.DefaultCurve;
}

/// <summary>
/// Makes a sine layer bell-like: a second sine at <see cref="Ratio"/> times the pitch bends the first one's phase.
/// The depth falls from <see cref="Index"/> to <see cref="To"/> (in radians) over <see cref="Time"/>, so the tone
/// starts bright and ends pure. Ratios that are not whole numbers, such as 3.5 or 1.41, sound like glass or metal.
/// </summary>
internal sealed record Fm
{
    public required double Ratio { get; init; }

    public required double Index { get; init; }

    public double To { get; init; }

    /// <summary>Defaults to the layer's decay.</summary>
    public double? Time { get; init; }

    public double Curve { get; init; } = Shape.DefaultCurve;
}

/// <summary>A 12 dB per octave filter whose cutoff moves from <see cref="Cutoff"/> to <see cref="To"/> Hz over <see cref="Time"/>.</summary>
internal sealed record Filter
{
    public FilterType Type { get; init; } = FilterType.Lowpass;

    public required double Cutoff { get; init; }

    /// <summary>Defaults to <see cref="Cutoff"/>: no sweep.</summary>
    public double? To { get; init; }

    /// <summary>Defaults to the layer's decay.</summary>
    public double? Time { get; init; }

    public double Curve { get; init; } = Shape.DefaultCurve;

    /// <summary>Resonance. 0.707 is flat; higher values ring at the cutoff.</summary>
    public double Q { get; init; } = 0.707;
}

/// <summary>Several copies of the oscillator, spread across <see cref="Detune"/> cents, for a warmer tone.</summary>
internal sealed record Unison
{
    public required int Voices { get; init; }

    public required double Detune { get; init; }
}

/// <summary>What happens to the sum of the layers, in this order.</summary>
internal sealed record Master
{
    /// <summary>Gain in dB into a soft saturation. 0 turns it off.</summary>
    public double Drive { get; init; }

    public double Highpass { get; init; } = 150;

    public double Lowpass { get; init; } = 10000;

    public Echo? Echo { get; init; }

    public Reverb? Reverb { get; init; }

    /// <summary>The fade at the very end of the file.</summary>
    public double FadeOut { get; init; } = 10;
}

internal sealed record Echo
{
    public required double Time { get; init; }

    /// <summary>How much of each repeat feeds the next, from 0 to 0.9.</summary>
    public double Feedback { get; init; } = 0.3;

    /// <summary>The level of the repeats, in dB below the sound.</summary>
    public double Mix { get; init; } = -12;

    /// <summary>Each repeat is duller than the last: a low-pass at this many Hz.</summary>
    public double Damp { get; init; } = 3000;
}

internal sealed record Reverb
{
    /// <summary>How long the tail takes to fall by 60 dB.</summary>
    public required double Decay { get; init; }

    /// <summary>The level of the tail, in dB below the sound.</summary>
    public double Mix { get; init; } = -14;

    /// <summary>Scales the size of the space. Below 1 is a smaller, denser room.</summary>
    public double Size { get; init; } = 1;

    public double Damp { get; init; } = 5000;

    /// <summary>How long after the sound the room answers, in milliseconds. A longer wait sounds like a bigger space.</summary>
    public double Predelay { get; init; }
}

/// <summary>
/// How far the variants differ. Each range is the distance from the recipe's own value to the farthest variant:
/// <see cref="Pitch"/> in semitones, <see cref="Time"/> as a fraction of the decays and sweep times,
/// <see cref="Tone"/> in octaves of filter cutoff (and the same factor on the bell depth).
/// </summary>
internal sealed record Vary
{
    public double Pitch { get; init; }

    public double Time { get; init; }

    public double Tone { get; init; }
}

internal static class Families
{
    /// <summary>Each family with the longest sound it allows, in milliseconds. The sound board lists them in this order.</summary>
    public static readonly IReadOnlyList<(string Name, string Title, double MaxMs)> All =
    [
        ("ui", "Interface", 300),
        ("cards", "Cards", 700),
        ("attack", "Your attacks", 1000),
        ("ship", "Your ship", 1200),
        ("flow", "Turns and the end of a fight", 1500),
        ("fitting", "Fitting", 800),
        ("reward", "Rewards and trade", 1500),
    ];

    public const double MinMs = 30;

    public static double MaxMs(string family) => All.First(f => f.Name == family).MaxMs;
}

internal sealed class RecipeException(string message) : Exception(message);

internal static partial class RecipeFiles
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static Recipe Parse(string json, string fileName)
    {
        Recipe? recipe;
        try
        {
            recipe = JsonSerializer.Deserialize<Recipe>(json, Options);
        }
        catch (JsonException e)
        {
            throw new RecipeException($"{fileName}: {e.Message}");
        }

        if (recipe is null)
        {
            throw new RecipeException($"{fileName}: the file holds no recipe.");
        }

        Validate(recipe, fileName);
        return recipe;
    }

    public static Recipe Load(string path) => Parse(File.ReadAllText(path), Path.GetFileName(path));

    /// <summary>Every recipe in a folder, in the order of their ids.</summary>
    public static IReadOnlyList<Recipe> LoadAll(string dir)
    {
        var files = Directory.GetFiles(dir, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        return files.Select(Load).ToList();
    }

    private static void Validate(Recipe r, string file)
    {
        void Require(bool ok, string what)
        {
            if (!ok)
            {
                throw new RecipeException($"{file}: {what}");
            }
        }

        void Range(double value, double min, double max, string field) =>
            Require(value >= min && value <= max, $"{field} is {value.ToString(CultureInfo.InvariantCulture)}; it must be from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}.");

        Require(IdPattern().IsMatch(r.Id), $"id \"{r.Id}\" must be lowercase words joined by hyphens.");
        Require(r.Id + ".json" == file, $"id \"{r.Id}\" must equal the file name.");
        Require(Families.All.Any(f => f.Name == r.Family), $"family \"{r.Family}\" is not one of {string.Join(", ", Families.All.Select(f => f.Name))}.");
        Require(r.Description.Trim().Length >= 10, "description must say what the sound is.");
        Range(r.Level, -30, 6, "level");
        Range(r.Layers.Count, 1, 6, "the number of layers");
        Range(r.Variants, 1, 5, "variants");
        Require(r.Variants == 1 || r.Vary is not null, "variants above 1 needs a vary block.");
        if (r.Vary is { } vary)
        {
            Range(vary.Pitch, 0, 3, "vary.pitch");
            Range(vary.Time, 0, 0.5, "vary.time");
            Range(vary.Tone, 0, 1, "vary.tone");
        }

        for (int i = 0; i < r.Layers.Count; i++)
        {
            var l = r.Layers[i];
            string at = $"layers[{i}].";
            bool tonal = l.Tonal;
            Require(!(l.Note is not null && l.Notes is not null), at + "note and notes cannot both be set.");
            Require(!tonal || l.Note is not null || l.Notes is { Count: > 0 }, at + "note (or notes) is required unless the wave is noise or crackle.");
            Require(tonal || (l.Note is null && l.Notes is null), at + $"a {l.Wave.ToString().ToLowerInvariant()} layer has no note.");
            foreach (var note in l.Notes ?? (l.Note is { } one ? [one] : []))
            {
                Range(note.Hz, 20, 12000, at + "note");
            }

            Range(l.Notes?.Count ?? 1, 1, 12, at + "the number of notes");
            Range(l.Step, 0, 500, at + "step");
            Range(l.Gain, -60, 12, at + "gain");
            Range(l.At, 0, 1500, at + "at");
            Range(l.Attack, 1, 1000, at + "attack");
            Range(l.Hold, 0, 1500, at + "hold");
            Range(l.Decay, 1, 1500, at + "decay");
            Range(l.Curve, -12, 12, at + "curve");
            if (l.Pitch is { } glide)
            {
                Require(tonal, at + "pitch needs a wave with a note.");
                Range(glide.From, -48, 48, at + "pitch.from");
                Range(glide.To, -48, 48, at + "pitch.to");
                Range(glide.Time, 1, 1500, at + "pitch.time");
                Range(glide.Curve, -12, 12, at + "pitch.curve");
            }

            if (l.Fm is { } fm)
            {
                Require(l.Wave == Wave.Sine, at + "fm works on a sine layer only.");
                Range(fm.Ratio, 0.1, 16, at + "fm.ratio");
                Range(fm.Index, 0, 12, at + "fm.index");
                Range(fm.To, 0, 12, at + "fm.to");
                Range(fm.Time ?? 1, 1, 1500, at + "fm.time");
                Range(fm.Curve, -12, 12, at + "fm.curve");
            }

            if (l.Filter is { } filter)
            {
                Range(filter.Cutoff, 20, 18000, at + "filter.cutoff");
                Range(filter.To ?? filter.Cutoff, 20, 18000, at + "filter.to");
                Range(filter.Time ?? 1, 1, 1500, at + "filter.time");
                Range(filter.Curve, -12, 12, at + "filter.curve");
                Range(filter.Q, 0.3, 12, at + "filter.q");
            }

            if (l.Unison is { } unison)
            {
                Require(tonal, at + "unison needs a wave with a note.");
                Range(unison.Voices, 2, 7, at + "unison.voices");
                Range(unison.Detune, 0, 100, at + "unison.detune");
            }

            Range(l.Hardness, 0, 1, at + "hardness");
            Require(l.Hardness == 0.5 || l.Rings, at + "hardness works on string and the struck materials only.");
            if (l.Density is { } density)
            {
                Require(l.Wave == Wave.Crackle, at + "density works on a crackle layer only.");
                Range(density.From, 1, 20000, at + "density.from");
                Range(density.To ?? density.From, 1, 20000, at + "density.to");
                Range(density.Time ?? 1, 1, 1500, at + "density.time");
                Range(density.Curve, -12, 12, at + "density.curve");
            }

            if (l.Drift is { } drift)
            {
                Require(tonal, at + "drift needs a wave with a note.");
                Range(drift.Cents, 0, 100, at + "drift.cents");
                Range(drift.Rate, 0.1, 30, at + "drift.rate");
            }

            if (l.Lfo is { } lfo)
            {
                Range(lfo.Rate, 0.1, 200, at + "lfo.rate");
                Range(lfo.To ?? lfo.Rate, 0.1, 200, at + "lfo.to");
                Range(lfo.Time ?? 1, 1, 1500, at + "lfo.time");
                Range(lfo.Curve, -12, 12, at + "lfo.curve");
                Range(lfo.Pitch, 0, 1200, at + "lfo.pitch");
                Range(lfo.Amp, 0, 1, at + "lfo.amp");
                Range(lfo.Cutoff, 0, 4, at + "lfo.cutoff");
                Require(lfo.Pitch == 0 || tonal, at + "lfo.pitch needs a wave with a note.");
                Require(lfo.Cutoff == 0 || l.Filter is not null, at + "lfo.cutoff needs a filter.");
                Require(lfo.Pitch > 0 || lfo.Amp > 0 || lfo.Cutoff > 0, at + "lfo needs pitch, amp or cutoff.");
            }

            if (l.Ring is { } ring)
            {
                Require(tonal, at + "ring needs a wave with a note.");
                Range(ring.Ratio, 0.05, 16, at + "ring.ratio");
                Range(ring.Amount, 0, 1, at + "ring.amount");
            }

            if (l.Formant is { } formant)
            {
                Range(formant.Time ?? 1, 1, 1500, at + "formant.time");
                Range(formant.Curve, -12, 12, at + "formant.curve");
                Range(formant.Shift, -12, 24, at + "formant.shift");
            }

            if (l.Body is { } body)
            {
                Range(body.Note.Hz, 20, 12000, at + "body.note");
                Range(body.Decay, 10, 3000, at + "body.decay");
                Range(body.Dry ?? 0, -60, 12, at + "body.dry");
            }

            if (l.Filter is { Type: FilterType.Ladder } ladder)
            {
                Range(ladder.Cutoff, 20, 16000, at + "filter.cutoff");
                Range(ladder.To ?? ladder.Cutoff, 20, 16000, at + "filter.to");
            }
        }

        var m = r.Master;
        Range(m.Drive, 0, 30, "master.drive");
        Range(m.Highpass, 20, 2000, "master.highpass");
        Range(m.Lowpass, 500, 20000, "master.lowpass");
        Range(m.FadeOut, 1, 200, "master.fadeOut");
        if (m.Echo is { } echo)
        {
            Range(echo.Time, 10, 500, "master.echo.time");
            Range(echo.Feedback, 0, 0.9, "master.echo.feedback");
            Range(echo.Mix, -60, 0, "master.echo.mix");
            Range(echo.Damp, 200, 18000, "master.echo.damp");
        }

        if (m.Reverb is { } reverb)
        {
            Range(reverb.Decay, 50, 1200, "master.reverb.decay");
            Range(reverb.Mix, -60, 0, "master.reverb.mix");
            Range(reverb.Size, 0.25, 2, "master.reverb.size");
            Range(reverb.Damp, 200, 18000, "master.reverb.damp");
            Range(reverb.Predelay, 0, 200, "master.reverb.predelay");
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();
}
