using System.Globalization;
using System.Text.Json;

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

            // That a render repeats exactly is checked by the tests, and for the game's sounds by Files below.
            var render = RenderCache.Of(recipe, v);
            renders.Add(render.Samples);
            var stats = Analysis.Measure(render.Samples);
            if (render.Clipped || stats.PeakDb > PeakCeilingDb)
            {
                Error($"it peaks at {Num(stats.PeakDb)} dBFS, above {Num(PeakCeilingDb)}. Lower the level, or make the sound less spiky.");
            }

            double target = Analysis.MusicOnPhone + recipe.Level;
            if (Math.Abs(stats.Loudness - target) > LevelToleranceDb)
            {
                Error($"its loudness is {Num(stats.Loudness)}, not the {Num(target)} its level asks for.");
            }

            if (render.Samples[0] != 0 || render.Samples[^1] != 0)
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

                var fresh = RenderCache.Of(recipe, v).Samples;
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

    /// <summary>
    /// Checks a ship's sound set. Every sound in it must pass <see cref="Sound"/>, must be one of the sounds that
    /// may change with the ship, and must keep the family and the level of the base sound it stands in for, so
    /// that a fight reads the same on any ship. A length far from the base sound's earns a warning, and so does a
    /// set that lacks some of a ship's sounds, for which the game plays the base ones.
    /// </summary>
    public static IReadOnlyList<Finding> Set(SoundSet set, IReadOnlyList<Recipe> baseRecipes)
    {
        var found = new List<Finding>();
        foreach (var recipe in set.Recipes)
        {
            string name = $"{set.Name}/{recipe.Id}";
            found.AddRange(Sound(recipe).Select(f => f with { Sound = $"{set.Name}/{f.Sound}" }));
            if (!SoundSets.ShipSounds.Contains(recipe.Id))
            {
                found.Add(new Finding(name, true, "every ship shares this sound, so a ship's set may not hold it. See the list in tools/SfxGen/README.md, \"Ships' sets\"."));
                continue;
            }

            if (baseRecipes.FirstOrDefault(r => r.Id == recipe.Id) is not { } basis)
            {
                found.Add(new Finding(name, true, "the base set in recipes/ has no sound of this name."));
                continue;
            }

            if (recipe.Family != basis.Family)
            {
                found.Add(new Finding(name, true, $"its family is {recipe.Family}, but the base sound's is {basis.Family}."));
            }

            if (recipe.Level != basis.Level)
            {
                found.Add(new Finding(name, true, $"its level is {Num(recipe.Level)}, but the base sound's is {Num(basis.Level)}. A sound keeps its level in every set."));
            }

            double own = LongestMs(recipe);
            double theirs = LongestMs(basis);
            if (own < theirs * SoundSets.ShortestShare || own > theirs * SoundSets.LongestShare)
            {
                found.Add(new Finding(name, false, $"it is {Num(own)} ms long, far from the base sound's {Num(theirs)} ms."));
            }
        }

        var missing = SoundSets.Missing(set);
        if (missing.Count > 0)
        {
            found.Add(new Finding(set.Name, false, $"the set has no {string.Join(", ", missing)}; the game plays the base sound instead."));
        }

        return found;
    }

    /// <summary>
    /// Checks the names of the sets and the takes. The sound board and the command line find a sound by
    /// <c>name/id</c>, so no set may share a name with a take, and none may take the name the board gives the
    /// base set.
    /// </summary>
    public static IReadOnlyList<Finding> Names(IReadOnlyList<SoundSet> sets, IReadOnlyList<Take> takes)
    {
        var found = new List<Finding>();
        foreach (var set in sets.Where(s => takes.Any(t => t.Name == s.Name)))
        {
            found.Add(new Finding(set.Name, true, "there is both a set and a take of this name. Rename the take, or delete it."));
        }

        foreach (string name in sets.Select(s => s.Name).Concat(takes.Select(t => t.Name)).Where(n => n == Board.NowTake))
        {
            found.Add(new Finding(name, true, "the sound board uses this name for the base set. Rename the folder."));
        }

        return found;
    }

    /// <summary>
    /// Checks the rendered files of every set, each in its folder inside the game's sound folder, as
    /// <see cref="Files"/> does for the base set, and that every folder there belongs to a set.
    /// </summary>
    public static IReadOnlyList<Finding> SetFiles(IReadOnlyList<SoundSet> sets, string outputDir)
    {
        var found = new List<Finding>();
        foreach (var set in sets)
        {
            found.AddRange(Files(set.Recipes, Path.Combine(outputDir, set.Name)).Select(f => f with { Sound = $"{set.Name}/{f.Sound}" }));
        }

        if (Directory.Exists(outputDir))
        {
            foreach (string folder in Directory.GetDirectories(outputDir))
            {
                string name = Path.GetFileName(folder);
                if (sets.All(s => s.Name != name))
                {
                    found.Add(new Finding(name + "/", true, "no set in tools/SfxGen/sets makes this folder. Delete it."));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Checks that every sound set a ship class names in <c>game/data/ships.json</c> has a folder here. A ship
    /// that names a set nobody made would play only the base sounds.
    /// </summary>
    public static IReadOnlyList<Finding> Ships(IReadOnlyList<SoundSet> sets, string shipsPath)
    {
        var found = new List<Finding>();
        using var ships = JsonDocument.Parse(File.ReadAllText(shipsPath));
        foreach (var ship in ships.RootElement.EnumerateArray())
        {
            if (ship.TryGetProperty("soundSet", out var named) && named.GetString() is { } name && sets.All(s => s.Name != name))
            {
                string id = ship.TryGetProperty("id", out var given) ? given.GetString() ?? "?" : "?";
                found.Add(new Finding("ships.json", true, $"the ship class '{id}' names the sound set '{name}', but tools/SfxGen/sets has no folder of that name."));
            }
        }

        return found;
    }

    /// <summary>The length of a recipe's longest variant.</summary>
    private static double LongestMs(Recipe recipe) =>
        Enumerable.Range(0, recipe.Variants).Max(v => RenderCache.Of(recipe, v).Samples.Length * 1000.0 / Synth.OutputRate);

    private static string Num(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
