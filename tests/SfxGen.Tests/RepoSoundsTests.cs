using Xunit;

namespace Bloom.SfxGen.Tests;

/// <summary>
/// The real recipes in <c>tools/SfxGen/recipes</c>, the ships' sets in <c>tools/SfxGen/sets</c> and the files
/// rendered from both in <c>game/audio/sfx</c>.
/// </summary>
public class RepoSoundsTests
{
    public static TheoryData<string> RecipeIds()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.GetFiles(Paths.Recipes, "*.json"))
        {
            data.Add(Path.GetFileNameWithoutExtension(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RecipeIds))]
    public void EveryRecipePassesTheChecks(string id)
    {
        var recipe = RecipeFiles.Load(Path.Combine(Paths.Recipes, id + ".json"));
        var errors = Checks.Sound(recipe).Where(f => f.IsError).Select(f => f.ToString());
        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void EverySoundOnTheBoardHasANameOfItsOwn()
    {
        // The page caches each sound by its name; two versions with one name would both play the first one decoded.
        string page = Board.Build(RecipeFiles.LoadAll(Paths.Recipes), SoundSets.LoadAll(Paths.Sets), TakeFiles.LoadAll(Paths.Takes));
        var names = System.Text.RegularExpressions.Regex.Matches(page, "\"name\":\"([^\"]+)\",\"wav\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.NotEmpty(names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryTakePassesTheChecks()
    {
        var errors = TakeFiles.LoadAll(Paths.Takes)
            .SelectMany(t => t.Recipes.SelectMany(Checks.Sound).Where(f => f.IsError).Select(f => $"{t.Name}/{f}"));
        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void EverySetPassesTheChecks()
    {
        var recipes = RecipeFiles.LoadAll(Paths.Recipes);
        var sets = SoundSets.LoadAll(Paths.Sets);
        var errors = sets.SelectMany(s => Checks.Set(s, recipes))
            .Concat(Checks.Names(sets, TakeFiles.LoadAll(Paths.Takes)))
            .Where(f => f.IsError)
            .Select(f => f.ToString());
        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void EverySetAShipNamesExists()
    {
        var errors = Checks.Ships(SoundSets.LoadAll(Paths.Sets), Paths.Ships).Select(f => f.ToString());
        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void TheRenderedFilesMatchTheRecipes()
    {
        var errors = Checks.Files(RecipeFiles.LoadAll(Paths.Recipes), Paths.Output)
            .Concat(Checks.SetFiles(SoundSets.LoadAll(Paths.Sets), Paths.Output))
            .Where(f => f.IsError)
            .Select(f => f.ToString());
        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void TheSoundBoardHoldsEverySound()
    {
        var recipes = RecipeFiles.LoadAll(Paths.Recipes);
        var sets = SoundSets.LoadAll(Paths.Sets);
        string page = Board.Build(recipes, sets, TakeFiles.LoadAll(Paths.Takes));
        Assert.DoesNotContain("/*SOUNDS*/", page, StringComparison.Ordinal);
        foreach (var recipe in recipes)
        {
            Assert.Contains($"\"id\":\"{recipe.Id}\"", page, StringComparison.Ordinal);
        }

        foreach (var set in sets)
        {
            Assert.Contains($"\"name\":\"{set.Name}\"", page, StringComparison.Ordinal);
            foreach (var recipe in set.Recipes)
            {
                Assert.Contains($"\"name\":\"{set.Name}/{Path.GetFileNameWithoutExtension(Synth.FileName(recipe, 0))}\"", page, StringComparison.Ordinal);
            }
        }
    }
}
