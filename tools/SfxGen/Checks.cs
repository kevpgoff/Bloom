using System.Globalization;

namespace Bloom.SfxGen;

/// <summary>Something a check found. An error fails <c>check</c> and the tests; a warning is only printed.</summary>
internal sealed record Finding(string Sound, bool IsError, string Message)
{
    public override string ToString() => $"{(IsError ? "error" : "warning")}: {Sound}: {Message}";
}

/// <summary>
/// The rules every sound must pass. They catch what the person tuning a recipe would otherwise need ears for:
/// clipping, clicks, a sound that a phone speaker would swallow, a file that no longer matches its recipe.
/// </summary>
internal static class Checks
{
    /// <summary>No sample may come closer to full scale than this, in dB.</summary>
    public const double PeakCeilingDb = -1;

    /// <summary>How far the measured loudness may be from the recipe's level, in dB.</summary>
    public const double LevelToleranceDb = 0.5;

    /// <summary>A sound may lose at most this much loudness on a phone speaker, in dB; half of it earns a warning.</summary>
    public const double MaxPhoneLossDb = 6;

    /// <summary>
    /// How far a committed file may be from a new render, in steps of the 16-bit scale. Another machine's math
    /// library can differ in the last bit of a sine, which moves a sample by a step at most; a changed recipe
    /// moves samples by hundreds.
    /// </summary>
    public const int FileToleranceSteps = 3;

    /// <summary>Renders every variant of a recipe and checks the result.</summary>
    public static IReadOnlyList<Finding> Sound(Recipe recipe)
    {
        var found = new List<Finding>();
        var renders = new List<short[]>();
        for (int v = 0; v < recipe.Variants; v++)
        {
            string name = Path.GetFileNameWithoutExtension(Synth.FileName(recipe, v));
            void Error(string message) => found.Add(new Finding(name, true, message));

            var first = Synth.Render(recipe, v);
            if (!first.Samples.AsSpan().SequenceEqual(Synth.Render(recipe, v).Samples))
            {
                Error("two renders of the same recipe differ.");
            }

            renders.Add(first.Samples);
            var stats = Analysis.Measure(first.Samples);
            if (first.Clipped || stats.PeakDb > PeakCeilingDb)
            {
                Error($"it peaks at {Num(stats.PeakDb)} dBFS, above {Num(PeakCeilingDb)}. Lower the level, or make the sound less spiky.");
            }

            double target = Analysis.MusicOnPhone + recipe.Level;
            if (Math.Abs(stats.Loudness - target) > LevelToleranceDb)
            {
                Error($"its loudness is {Num(stats.Loudness)}, not the {Num(target)} its level asks for.");
            }

            if (first.Samples[0] != 0 || first.Samples[^1] != 0)
            {
                Error("it does not start and end at zero, so it would click.");
            }

            if (stats.TailDb > -60)
            {
                Error($"its last 2 ms are at {Num(stats.TailDb)} dBFS; the end is cut off.");
            }

            if (stats.DcDb > -60)
            {
                Error($"it has a constant offset of {Num(stats.DcDb)} dBFS.");
            }

            double max = Families.MaxMs(recipe.Family);
            if (stats.Ms < Families.MinMs || stats.Ms > max)
            {
                Error($"it is {Num(stats.Ms)} ms long; the {recipe.Family} family allows {Num(Families.MinMs)} to {Num(max)} ms.");
            }

            if (stats.PhoneLoss > MaxPhoneLossDb)
            {
                Error($"it loses {Num(stats.PhoneLoss)} dB on a phone speaker (limit {Num(MaxPhoneLossDb)}). Give it more above {Num(Analysis.PhoneCutoff)} Hz.");
            }
            else if (stats.PhoneLoss > MaxPhoneLossDb / 2)
            {
                found.Add(new Finding(name, false, $"it loses {Num(stats.PhoneLoss)} dB on a phone speaker."));
            }
        }

        for (int a = 0; a < renders.Count; a++)
        {
            for (int b = a + 1; b < renders.Count; b++)
            {
                if (renders[a].AsSpan().SequenceEqual(renders[b]))
                {
                    found.Add(new Finding(recipe.Id, true, $"variants {a + 1} and {b + 1} are the same."));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Checks the rendered files in the game against the recipes: every recipe has its files, every file has a
    /// recipe and still matches it, and Godot is told to keep each file as plain PCM.
    /// </summary>
    public static IReadOnlyList<Finding> Files(IReadOnlyList<Recipe> recipes, string outputDir)
    {
        var found = new List<Finding>();
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in recipes)
        {
            for (int v = 0; v < recipe.Variants; v++)
            {
                string file = Synth.FileName(recipe, v);
                expected.Add(file);
                string path = Path.Combine(outputDir, file);
                if (!File.Exists(path))
                {
                    found.Add(new Finding(file, true, "the file is missing. Run tools/sfx.sh render."));
                    continue;
                }

                short[] committed;
                try
                {
                    committed = Wav.Decode(File.ReadAllBytes(path));
                }
                catch (InvalidDataException e)
                {
                    found.Add(new Finding(file, true, e.Message));
                    continue;
                }

                var fresh = Synth.Render(recipe, v).Samples;
                if (committed.Length != fresh.Length)
                {
                    found.Add(new Finding(file, true, "the file is older than its recipe (the length differs). Run tools/sfx.sh render."));
                }
                else
                {
                    int worst = 0;
                    for (int i = 0; i < fresh.Length; i++)
                    {
                        worst = Math.Max(worst, Math.Abs(committed[i] - fresh[i]));
                    }

                    if (worst > FileToleranceSteps)
                    {
                        found.Add(new Finding(file, true, "the file is older than its recipe. Run tools/sfx.sh render."));
                    }
                }

                string import = path + ".import";
                if (!File.Exists(import))
                {
                    found.Add(new Finding(file, true, "Godot has not imported the file. Run the editor's import (see tools/SfxGen/README.md)."));
                }
                else if (!File.ReadLines(import).Any(line => line.Trim() == "compress/mode=0"))
                {
                    found.Add(new Finding(file, true, "its .import file does not keep it as plain PCM (compress/mode=0)."));
                }
            }
        }

        if (Directory.Exists(outputDir))
        {
            foreach (string path in Directory.GetFiles(outputDir, "*.wav"))
            {
                string file = Path.GetFileName(path);
                if (!expected.Contains(file))
                {
                    found.Add(new Finding(file, true, "no recipe makes this file. Delete it and its .import file."));
                }
            }
        }

        return found;
    }

    private static string Num(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
