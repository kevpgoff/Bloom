using System.Diagnostics;
using System.Reflection;

namespace Bloom.SfxGen;

/// <summary>Where the tool reads and writes, all inside the repo.</summary>
internal static class Paths
{
    public static string RepoRoot { get; } = typeof(Paths).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "BloomRepoRoot").Value!;

    /// <summary>The base set: the Seedling's sounds, and the ones every ship shares.</summary>
    public static string Recipes => Path.Combine(RepoRoot, "tools", "SfxGen", "recipes");

    /// <summary>The other ships' own sounds, one folder per set. See <see cref="SoundSet"/>.</summary>
    public static string Sets => Path.Combine(RepoRoot, "tools", "SfxGen", "sets");

    /// <summary>Trial versions of sounds, one folder per take, for the sound board only. See <see cref="TakeFiles"/>.</summary>
    public static string Takes => Path.Combine(RepoRoot, "tools", "SfxGen", "takes");

    /// <summary>The rendered sounds, which the game loads and the repo keeps. Each set has a folder of its name inside.</summary>
    public static string Output => Path.Combine(RepoRoot, "game", "audio", "sfx");

    /// <summary>The ship classes, each of which may name a set.</summary>
    public static string Ships => Path.Combine(RepoRoot, "game", "data", "ships.json");

    /// <summary>Previews, spectrograms and the sound board. Git ignores this folder.</summary>
    public static string Build => Path.Combine(RepoRoot, "build", "sfx");
}

internal static class Program
{
    private const string Usage = """
        Renders the game's sound effects from the recipes in tools/SfxGen/recipes and the ships' sets in tools/SfxGen/sets.

          render [id...]            write the sounds into game/audio/sfx and print their numbers
          stats [id...] [--spectra] print the numbers without writing; --spectra also draws spectrograms
          play <id> [--ab] [--times n]
                                    render one sound into build/sfx and play it; --ab plays the saved file first
          check                     run every check on every recipe, set and take, and on the files in game/audio/sfx
          board                     write build/sfx/board.html, one page with every sound, set and take to listen to

        With no id, render and stats cover the base sounds and every set. A sound of a set or of a take is named
        name/id, such as heliotrope/card-play; name/ means every sound of that set or take. render refuses a
        take, because takes never go into the game.

        The recipe format is described in tools/SfxGen/README.md.
        """;

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        var flags = args.Skip(1).Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToList();
        var names = new List<string>();
        int times = 1;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--times" && i + 1 < args.Length && int.TryParse(args[i + 1], out times))
            {
                i++;
            }
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                names.Add(args[i]);
            }
        }

        try
        {
            var all = RecipeFiles.LoadAll(Paths.Recipes);
            var sets = SoundSets.LoadAll(Paths.Sets);
            var takes = TakeFiles.LoadAll(Paths.Takes);
            switch (args[0])
            {
                case "render":
                    var chosen = Pick(all, sets, takes, names);
                    if (chosen.Any(sound => sound.OutputDir is null))
                    {
                        throw new RecipeException("render writes the game's sounds; a take is only for the sound board. Use stats or play.");
                    }

                    return Render(chosen, write: true, spectra: false);
                case "stats":
                    return Render(Pick(all, sets, takes, names), write: false, spectra: flags.Contains("--spectra"));
                case "play" when names.Count == 1:
                    return Play(Pick(all, sets, takes, names)[0], flags.Contains("--ab"), Math.Clamp(times, 1, 20));
                case "check":
                    return Check(all, sets, takes);
                case "board":
                    string path = Path.Combine(Paths.Build, "board.html");
                    Directory.CreateDirectory(Paths.Build);
                    File.WriteAllText(path, Board.Build(all, sets, takes));
                    int sounds = all.Sum(r => r.Variants) + sets.Sum(t => t.Recipes.Sum(r => r.Variants)) + takes.Sum(t => t.Recipes.Sum(r => r.Variants));
                    Console.WriteLine($"Wrote {path} ({new FileInfo(path).Length / 1024} kB, {sounds} sounds: the base set, {Count(sets.Count, "other set")} and {Count(takes.Count, "take")}).");
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (RecipeException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The sounds named on the command line. No names means every sound that goes into the game: the base recipes
    /// and every set's.
    /// </summary>
    private static IReadOnlyList<Named> Pick(IReadOnlyList<Recipe> all, IReadOnlyList<SoundSet> sets, IReadOnlyList<Take> takes, List<string> names)
    {
        if (names.Count == 0)
        {
            return all.Select(r => new Named("", r, InGame: true))
                .Concat(sets.SelectMany(s => s.Recipes.Select(r => new Named(s.Name, r, InGame: true))))
                .ToList();
        }

        var picked = new List<Named>();
        foreach (string name in names)
        {
            int slash = name.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
            {
                var recipe = all.FirstOrDefault(r => r.Id == name)
                    ?? throw new RecipeException($"there is no recipe named \"{name}\" in {Paths.Recipes}.");
                picked.Add(new Named("", recipe, InGame: true));
                continue;
            }

            string group = name[..slash];
            string id = name[(slash + 1)..];
            bool inGame = sets.Any(s => s.Name == group);
            var recipes = sets.FirstOrDefault(s => s.Name == group)?.Recipes
                ?? takes.FirstOrDefault(t => t.Name == group)?.Recipes
                ?? throw new RecipeException($"there is no set named \"{group}\" in {Paths.Sets} and no take of that name in {Paths.Takes}.");
            var chosen = id.Length == 0 ? recipes : recipes.Where(r => r.Id == id).ToList();
            if (chosen.Count == 0)
            {
                throw new RecipeException($"\"{group}\" has no sound named \"{id}\".");
            }

            picked.AddRange(chosen.Select(r => new Named(group, r, inGame)));
        }

        return picked;
    }

    /// <summary>
    /// A recipe and where it belongs: an empty group is the base set, and any other group is the name of a set
    /// (which goes into the game) or of a take (which does not).
    /// </summary>
    private sealed record Named(string Group, Recipe Recipe, bool InGame)
    {
        /// <summary>The folder its rendered files are kept in, or null for a take, which has none.</summary>
        public string? OutputDir => !InGame ? null : Group.Length == 0 ? Paths.Output : Path.Combine(Paths.Output, Group);

        public string Label(int variant) =>
            (Group.Length == 0 ? "" : Group + "/") + Path.GetFileNameWithoutExtension(Synth.FileName(Recipe, variant));
    }

    private static int Render(IReadOnlyList<Named> sounds, bool write, bool spectra)
    {
        Console.WriteLine(Stats.Header);
        var found = new List<Finding>();
        int changed = 0;
        foreach (var sound in sounds)
        {
            var recipe = sound.Recipe;
            found.AddRange(Checks.Sound(recipe).Select(f => sound.Group.Length == 0 ? f : f with { Sound = sound.Group + "/" + f.Sound }));
            for (int v = 0; v < recipe.Variants; v++)
            {
                string name = sound.Label(v);
                var samples = Synth.Render(recipe, v).Samples;
                byte[] bytes = Wav.Encode(samples);
                Console.WriteLine(Analysis.Measure(samples).Line(name));

                // A take has no saved file to compare with or to write.
                if (sound.OutputDir is { } dir)
                {
                    string saved = Path.Combine(dir, Synth.FileName(recipe, v));
                    if (File.Exists(saved) && !File.ReadAllBytes(saved).AsSpan().SequenceEqual(bytes))
                    {
                        changed++;
                        Console.WriteLine(TryMeasure(saved)?.Line("  was") ?? "  was: unreadable");
                    }

                    if (write)
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllBytes(saved, bytes);
                    }
                }

                if (spectra)
                {
                    DrawSpectrum(name.Replace('/', '.'), bytes);
                }
            }
        }

        foreach (var finding in found)
        {
            Console.WriteLine(finding);
        }

        Console.WriteLine($"{sounds.Sum(s => s.Recipe.Variants)} sounds, {changed} different from the saved files{(write ? ", written to " + Paths.Output : "")}.");
        return found.Any(f => f.IsError) ? 1 : 0;
    }

    private static Stats? TryMeasure(string path)
    {
        try
        {
            return Analysis.Measure(Wav.Decode(File.ReadAllBytes(path)));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static int Check(IReadOnlyList<Recipe> recipes, IReadOnlyList<SoundSet> sets, IReadOnlyList<Take> takes)
    {
        var found = recipes.SelectMany(Checks.Sound)
            .Concat(sets.SelectMany(s => Checks.Set(s, recipes)))
            .Concat(takes.SelectMany(t => t.Recipes.SelectMany(Checks.Sound).Select(f => f with { Sound = t.Name + "/" + f.Sound })))
            .Concat(Checks.Names(sets, takes))
            .Concat(Checks.Files(recipes, Paths.Output))
            .Concat(Checks.SetFiles(sets, Paths.Output))
            .Concat(Checks.Ships(sets, Paths.Ships))
            .ToList();
        foreach (var finding in found)
        {
            Console.WriteLine(finding);
        }

        int errors = found.Count(f => f.IsError);
        Console.WriteLine(
            $"{recipes.Count} base recipes, {sets.Sum(s => s.Recipes.Count)} in {Count(sets.Count, "set")} and {takes.Sum(t => t.Recipes.Count)} in {Count(takes.Count, "take")}: " +
            $"{errors} errors, {found.Count - errors} warnings.");
        return errors > 0 ? 1 : 0;
    }

    /// <summary>"1 set" or "2 sets".</summary>
    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private static int Play(Named sound, bool ab, int times)
    {
        var recipe = sound.Recipe;
        string dir = Path.Combine(Paths.Build, "preview", sound.Group);
        Directory.CreateDirectory(dir);
        Console.WriteLine(Stats.Header);
        for (int v = 0; v < recipe.Variants; v++)
        {
            string file = Synth.FileName(recipe, v);
            var samples = Synth.Render(recipe, v).Samples;
            string preview = Path.Combine(dir, file);
            File.WriteAllBytes(preview, Wav.Encode(samples));
            Console.WriteLine(Analysis.Measure(samples).Line(sound.Label(v)));

            // A set's sound is compared with its own saved file. A take, or a set's sound that was never rendered,
            // is compared with the base sound of the same name.
            string saved = sound.OutputDir is { } own && File.Exists(Path.Combine(own, file))
                ? Path.Combine(own, file)
                : Path.Combine(Paths.Output, file);
            for (int t = 0; t < times; t++)
            {
                if (ab && File.Exists(saved))
                {
                    PlayFile(saved);
                    Thread.Sleep(350);
                }

                if (!PlayFile(preview))
                {
                    Console.Error.WriteLine("error: none of pw-play, paplay or aplay could play the file.");
                    return 1;
                }

                Thread.Sleep(250);
            }
        }

        return 0;
    }

    private static bool PlayFile(string path) =>
        new[] { "pw-play", "paplay", "aplay" }.Any(player => Run(player, path) == 0);

    /// <summary>Draws build/sfx/spectra/NAME.png with ffmpeg: time across, frequency up (100 Hz to 16 kHz, log scale).</summary>
    private static void DrawSpectrum(string name, byte[] wav)
    {
        string dir = Path.Combine(Paths.Build, "spectra");
        Directory.CreateDirectory(dir);
        string input = Path.Combine(dir, name + ".wav");
        File.WriteAllBytes(input, wav);
        int exit = Run(
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", input,
            "-lavfi", "showspectrumpic=s=640x320:legend=1:fscale=log:start=100:stop=16000",
            Path.Combine(dir, name + ".png"));
        File.Delete(input);
        if (exit != 0)
        {
            Console.Error.WriteLine($"warning: ffmpeg could not draw the spectrogram of {name}.");
        }
    }

    private static int Run(string program, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(program) { UseShellExecute = false };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return -1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The program is not installed.
            return -1;
        }
    }
}
