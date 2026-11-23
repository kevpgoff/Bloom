using Xunit;

namespace Bloom.SfxGen.Tests;

/// <summary>The real recipes in <c>tools/SfxGen/recipes</c> and the files rendered from them in <c>game/audio/sfx</c>.</summary>
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
        string page = Board.Build(RecipeFiles.LoadAll(Paths.Recipes), TakeFiles.LoadAll(Paths.Takes));
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
    public void TheRenderedFilesMatchTheRecipes()
    {
        var errors = Checks.Files(RecipeFiles.LoadAll(Paths.Recipes), Paths.Output).Where(f => f.IsError).Select(f => f.ToString());
        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void TheSoundBoardHoldsEverySound()
    {
        var recipes = RecipeFiles.LoadAll(Paths.Recipes);
        string page = Board.Build(recipes, TakeFiles.LoadAll(Paths.Takes));
        Assert.DoesNotContain("/*SOUNDS*/", page, StringComparison.Ordinal);
        foreach (var recipe in recipes)
        {
            Assert.Contains($"\"id\":\"{recipe.Id}\"", page, StringComparison.Ordinal);
        }
    }
}
