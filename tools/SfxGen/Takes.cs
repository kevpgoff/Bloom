using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bloom.SfxGen;

/// <summary>
/// A take: a trial version of some of the sounds, in one character, for the owner to compare with the game's own
/// sounds on the sound board. A take is a folder in <c>tools/SfxGen/takes/</c> that holds <c>about.json</c> and
/// recipes named like the game's. When a take is chosen, its recipes move into <c>recipes/</c> and the folder goes.
/// </summary>
internal sealed record Take(string Name, string Title, string Description, IReadOnlyList<Recipe> Recipes);

internal static partial class TakeFiles
{
    public const string AboutFile = "about.json";

    private sealed record About
    {
        public required string Title { get; init; }

        public required string Description { get; init; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Every take, in the order of their folder names. No folder means no takes.</summary>
    /// <param name="dir">The folder that holds one folder per take.</param>
    /// <param name="kind">
    /// What the folders are called in an error: <c>takes</c>, or <c>sets</c> when <see cref="SoundSets"/> reads
    /// the ships' sets, which are kept in folders of the same form.
    /// </param>
    public static IReadOnlyList<Take> LoadAll(string dir, string kind = "takes")
    {
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var folders = Directory.GetDirectories(dir);
        Array.Sort(folders, StringComparer.Ordinal);
        return folders.Select(folder => Load(folder, kind)).ToList();
    }

    public static Take Load(string folder, string kind = "takes")
    {
        string name = Path.GetFileName(folder);
        if (!NamePattern().IsMatch(name))
        {
            throw new RecipeException($"{kind}/{name}: the folder's name must be lowercase words joined by hyphens.");
        }

        string aboutPath = Path.Combine(folder, AboutFile);
        if (!File.Exists(aboutPath))
        {
            throw new RecipeException($"{kind}/{name}: the folder needs an {AboutFile} with a title and a description.");
        }

        About about;
        try
        {
            about = JsonSerializer.Deserialize<About>(File.ReadAllText(aboutPath), Options)
                ?? throw new RecipeException($"{kind}/{name}/{AboutFile} is empty.");
        }
        catch (JsonException e)
        {
            throw new RecipeException($"{kind}/{name}/{AboutFile}: {e.Message}");
        }

        var files = Directory.GetFiles(folder, "*.json").Where(f => Path.GetFileName(f) != AboutFile).ToArray();
        Array.Sort(files, StringComparer.Ordinal);
        var recipes = new List<Recipe>();
        foreach (string file in files)
        {
            try
            {
                recipes.Add(RecipeFiles.Load(file));
            }
            catch (RecipeException e)
            {
                throw new RecipeException($"{kind}/{name}/{e.Message}");
            }
        }

        return new Take(name, about.Title, about.Description, recipes);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex NamePattern();
}
