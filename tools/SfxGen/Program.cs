using System.Diagnostics;
using System.Reflection;

namespace Bloom.SfxGen;

/// <summary>Where the tool reads and writes, all inside the repo.</summary>
internal static class Paths
{
    public static string RepoRoot { get; } = typeof(Paths).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "BloomRepoRoot").Value!;

    public static string Recipes => Path.Combine(RepoRoot, "tools", "SfxGen", "recipes");

    /// <summary>Trial versions of sounds, one folder per take, for the sound board only. See <see cref="TakeFiles"/>.</summary>
    public static string Takes => Path.Combine(RepoRoot, "tools", "SfxGen", "takes");

    /// <summary>The rendered sounds, which the game loads and the repo keeps.</summary>
    public static string Output => Path.Combine(RepoRoot, "game", "audio", "sfx");

    /// <summary>Previews, spectrograms and the sound board. Git ignores this folder.</summary>
    public static string Build => Path.Combine(RepoRoot, "build", "sfx");
}

internal static class Program
{
    private const string Usage = """
        Renders the game's sound effects from the recipes in tools/SfxGen/recipes.

          render [id...]            write the sounds into game/audio/sfx and print their numbers
          stats [id...] [--spectra] print the numbers without writing; --spectra also draws spectrograms
          play <id> [--ab] [--times n]
                                    render one sound into build/sfx and play it; --ab plays the saved file first
          check                     run every check on every recipe, every take and the files in game/audio/sfx
          board                     write build/sfx/board.html, one page with every sound and take to listen to

        A take's sound is named take/id, such as heliotrope/card-play; take/ names every sound of the take.
        stats, play and check accept them; render does not, because takes never go into the game.

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
            var takes = TakeFiles.LoadAll(Paths.Takes);
            switch (args[0])
            {
                case "render" when names.Any(n => n.Contains('/', StringComparison.Ordinal)):
                    throw new RecipeException("render writes the game's sounds; a take is only for the sound board. Use stats or play.");
                case "render":
                    return Render(Pick(all, takes, names), write: true, spectra: false);
                case "stats":
                    return Render(Pick(all, takes, names), write: false, spectra: flags.Contains("--spectra"));
                case "play" when names.Count == 1:
                    return Play(Pick(all, takes, names)[0], flags.Contains("--ab"), Math.Clamp(times, 1, 20));
                case "check":
                    return Check(all, takes);
                case "board":
                    string path = Path.Combine(Paths.Build, "board.html");
                    Directory.CreateDirectory(Paths.Build);
                    File.WriteAllText(path, Board.Build(all, takes));
                    int sounds = all.Sum(r => r.Variants) + takes.Sum(t => t.Recipes.Sum(r => r.Variants));
                    Console.WriteLine($"Wrote {path} ({new FileInfo(path).Length / 1024} kB, {sounds} sounds in {takes.Count + 1} takes).");
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

    /// <summary>The sounds named on the command line, as (where it is shown, recipe) pairs. No names means every recipe.</summary>
    private static IReadOnlyList<Named> Pick(IReadOnlyList<Recipe> all, IReadOnlyList<Take> takes, List<string> names)
    {
        if (names.Count == 0)
        {
            return all.Select(r => new Named("", r)).ToList();
        }

        var picked = new List<Named>();
        foreach (string name in names)
        {
            int slash = name.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
            {
                var recipe = all.FirstOrDefault(r => r.Id == name)
                    ?? throw new RecipeException($"there is no recipe named \"{name}\" in {Paths.Recipes}.");
                picked.Add(new Named("", recipe));
                continue;
            }

            string takeName = name[..slash];
            string id = name[(slash + 1)..];
            var take = takes.FirstOrDefault(t => t.Name == takeName)
                ?? throw new RecipeException($"there is no take named \"{takeName}\" in {Paths.Takes}.");
            var chosen = id.Length == 0 ? take.Recipes : take.Recipes.Where(r => r.Id == id).ToList();
            if (chosen.Count == 0)
            {
                throw new RecipeException($"the take \"{takeName}\" has no sound named \"{id}\".");
            }

            picked.AddRange(chosen.Select(r => new Named(take.Name, r)));
        }

        return picked;
    }

    /// <summary>A recipe and the take it belongs to; an empty take is the game's own recipes.</summary>
    private sealed record Named(string Take, Recipe Recipe)
    {
        public string Label(int variant) =>
            (Take.Length == 0 ? "" : Take + "/") + Path.GetFileNameWithoutExtension(Synth.FileName(Recipe, variant));
    }

    private static int Render(IReadOnlyList<Named> sounds, bool write, bool spectra)
    {
        if (write)
        {
            Directory.CreateDirectory(Paths.Output);
        }

        Console.WriteLine(Stats.Header);
        var found = new List<Finding>();
        int changed = 0;
        foreach (var (take, recipe) in sounds)
        {
            found.AddRange(Checks.Sound(recipe).Select(f => take.Length == 0 ? f : f with { Sound = take + "/" + f.Sound }));
            for (int v = 0; v < recipe.Variants; v++)
            {
                string file = Synth.FileName(recipe, v);
                string name = new Named(take, recipe).Label(v);
                var samples = Synth.Render(recipe, v).Samples;
                byte[] bytes = Wav.Encode(samples);
                Console.WriteLine(Analysis.Measure(samples).Line(name));

                string saved = Path.Combine(Paths.Output, file);
                if (take.Length == 0 && File.Exists(saved) && !File.ReadAllBytes(saved).AsSpan().SequenceEqual(bytes))
                {
                    changed++;
                    Console.WriteLine(TryMeasure(saved)?.Line("  was") ?? "  was: unreadable");
                }

                if (write)
                {
                    File.WriteAllBytes(saved, bytes);
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

    private static int Check(IReadOnlyList<Recipe> recipes, IReadOnlyList<Take> takes)
    {
        var found = recipes.SelectMany(Checks.Sound)
            .Concat(takes.SelectMany(t => t.Recipes.SelectMany(Checks.Sound).Select(f => f with { Sound = t.Name + "/" + f.Sound })))
            .Concat(Checks.Files(recipes, Paths.Output))
            .ToList();
        foreach (var finding in found)
        {
            Console.WriteLine(finding);
        }

        int errors = found.Count(f => f.IsError);
        Console.WriteLine($"{recipes.Count} recipes and {takes.Sum(t => t.Recipes.Count)} takes, {errors} errors, {found.Count - errors} warnings.");
        return errors > 0 ? 1 : 0;
    }

    private static int Play(Named sound, bool ab, int times)
    {
        var recipe = sound.Recipe;
        string dir = Path.Combine(Paths.Build, "preview", sound.Take);
        Directory.CreateDirectory(dir);
        Console.WriteLine(Stats.Header);
        for (int v = 0; v < recipe.Variants; v++)
        {
            string file = Synth.FileName(recipe, v);
            var samples = Synth.Render(recipe, v).Samples;
            string preview = Path.Combine(dir, file);
            File.WriteAllBytes(preview, Wav.Encode(samples));
            Console.WriteLine(Analysis.Measure(samples).Line(sound.Label(v)));

            // A take is compared with the game's sound of the same name.
            string saved = Path.Combine(Paths.Output, file);
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
