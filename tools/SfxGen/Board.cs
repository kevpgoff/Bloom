using System.Globalization;
using System.Text.Json;

namespace Bloom.SfxGen;

/// <summary>
/// Builds the sound board: one HTML page that holds every rendered sound, with a button to play each, so the
/// owner can judge them by ear on a phone. The page is <c>board.template.html</c> with the sounds filled in.
/// </summary>
internal static class Board
{
    private const string Placeholder = "/*SOUNDS*/null";

    /// <summary>The name the page gives the base set when there are sets or takes to compare it with.</summary>
    public const string NowTake = "now";

    /// <summary>
    /// The page holds, for each sound, the base version (under the name "now"), every other set's version of it
    /// and every take's, in that order. A sound that only a take has is listed too. The page treats a set like a
    /// take that is in the game: when it plays a fight in a set, the base set supplies the sounds the set lacks.
    /// </summary>
    public static string Build(IReadOnlyList<Recipe> recipes, IReadOnlyList<SoundSet> sets, IReadOnlyList<Take> takes)
    {
        var others = sets.Select(s => (s.Name, s.Title, s.Description, s.Recipes, InGame: true))
            .Concat(takes.Select(t => (t.Name, t.Title, t.Description, t.Recipes, InGame: false)))
            .ToList();

        string template = File.ReadAllText(Path.Combine(Paths.RepoRoot, "tools", "SfxGen", "board.template.html"));
        if (!template.Contains(Placeholder, StringComparison.Ordinal))
        {
            throw new RecipeException($"board.template.html has no {Placeholder} placeholder.");
        }

        // A short code that changes whenever any sound changes, so two rounds of the page can be told apart.
        ulong round = 14695981039346656037UL;
        object Version(Recipe recipe, string take)
        {
            var variants = new List<object>();
            double ms = 0;
            for (int v = 0; v < recipe.Variants; v++)
            {
                var samples = RenderCache.Of(recipe, v).Samples;
                byte[] wav = Wav.Encode(samples);
                foreach (byte b in wav)
                {
                    round = unchecked((round ^ b) * 1099511628211UL);
                }

                ms = Math.Max(ms, samples.Length * 1000.0 / Synth.OutputRate);
                // The page finds a sound by its name, so a set's or a take's files are named after it as well:
                // "fairy-ring/block-gain" and the base set's "block-gain" must never be taken for each other.
                string file = Path.GetFileNameWithoutExtension(Synth.FileName(recipe, v));
                variants.Add(new { name = take == NowTake ? file : $"{take}/{file}", wav = Convert.ToBase64String(wav) });
            }

            return new { take, description = recipe.Description, level = recipe.Level, ms = Math.Round(ms), variants };
        }

        var families = new List<object>();
        foreach (var (name, title, _) in Families.All)
        {
            var ids = recipes.Where(r => r.Family == name).Select(r => r.Id)
                .Concat(others.SelectMany(t => t.Recipes).Where(r => r.Family == name).Select(r => r.Id))
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList();
            var sounds = new List<object>();
            foreach (string id in ids)
            {
                var versions = new List<object>();
                if (recipes.FirstOrDefault(r => r.Id == id) is { } own)
                {
                    versions.Add(Version(own, NowTake));
                }

                foreach (var take in others)
                {
                    if (take.Recipes.FirstOrDefault(r => r.Id == id) is { } tried)
                    {
                        versions.Add(Version(tried, take.Name));
                    }
                }

                sounds.Add(new { id, versions });
            }

            if (sounds.Count > 0)
            {
                families.Add(new { name, title, sounds });
            }
        }

        var takeList = new List<object>();
        if (recipes.Count > 0 && others.Count > 0)
        {
            takeList.Add(new { name = NowTake, title = "Seedling: wood and water", description = "The base set: the Seedling's own sounds, and the sounds every ship shares. Things grown and made by hand, such as wooden bars, a thumb piano, plucked strings, seed pods, leaves and a glass bowl of water.", inGame = true });
        }

        takeList.AddRange(others.Select(t => new { name = t.Name, title = t.Title, description = t.Description, inGame = t.InGame }));

        string json = JsonSerializer.Serialize(new
        {
            round = (round & 0xFFFF).ToString("x4", CultureInfo.InvariantCulture),
            takes = takeList,
            families,
        });

        // "</" inside a script block would end it early; JSON allows the slash to be escaped.
        return template.Replace(Placeholder, json.Replace("</", "<\\/", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
