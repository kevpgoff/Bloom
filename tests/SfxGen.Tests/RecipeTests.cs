using Xunit;

namespace Bloom.SfxGen.Tests;

public class RecipeTests
{
    private const string Good = """
        {
          "id": "good", "family": "cards", "description": "A test pluck.", "level": -6,
          "layers": [ { "wave": "triangle", "note": "D5", "decay": 100 } ]
        }
        """;

    [Theory]
    [InlineData("A4", 440.0)]
    [InlineData("D5", 587.33)]
    [InlineData("C4", 261.63)]
    [InlineData("Bb3", 233.08)]
    [InlineData("F#6", 1479.98)]
    public void NoteNamesAreTunedToA440(string name, double hz) => Assert.Equal(hz, Note.Parse(name).Hz, 0.01);

    [Theory]
    [InlineData("H4")]
    [InlineData("d5")]
    [InlineData("D")]
    [InlineData("D55")]
    public void ABadNoteNameIsRefused(string name) => Assert.Throws<FormatException>(() => Note.Parse(name));

    [Fact]
    public void AGoodRecipeLoadsWithItsDefaults()
    {
        var recipe = RecipeFiles.Parse(Good, "good.json");
        Assert.Equal(1, recipe.Variants);
        Assert.Equal(2, recipe.Layers[0].Attack);
        Assert.Equal(150, recipe.Master.Highpass);
        Assert.Equal(587.33, recipe.Layers[0].Note!.Value.Hz, 0.01);
    }

    [Fact]
    public void ANoteMayBeWrittenInHz()
    {
        var recipe = RecipeFiles.Parse(Good.Replace("\"D5\"", "1000"), "good.json");
        Assert.Equal(1000, recipe.Layers[0].Note!.Value.Hz);
    }

    [Theory]
    [InlineData("\"level\": -6", "\"level\": -6, \"loudness\": 3", "loudness")]
    [InlineData("\"decay\": 100", "\"decay\": 100, \"sustain\": 0.5", "sustain")]
    [InlineData("\"level\": -6,", "", "level")]
    [InlineData("\"decay\": 100", "\"attack\": 5", "decay")]
    [InlineData("\"triangle\"", "\"sawtooth\"", "wave")]
    [InlineData("\"D5\"", "\"D#b5\"", "note name")]
    [InlineData("\"id\": \"good\"", "\"id\": \"Good\"", "lowercase")]
    [InlineData("\"id\": \"good\"", "\"id\": \"other\"", "file name")]
    [InlineData("\"cards\"", "\"music\"", "family")]
    [InlineData("\"level\": -6", "\"level\": 20", "level")]
    [InlineData("\"decay\": 100", "\"decay\": 100, \"fm\": { \"ratio\": 2, \"index\": 1 }", "sine")]
    [InlineData("\"level\": -6", "\"level\": -6, \"variants\": 3", "vary")]
    [InlineData("\"note\": \"D5\", ", "", "note")]
    [InlineData("\"level\": -6", "\"level\": -6, \"level\": -7", "level")]
    public void AMistakeIsRefusedAndTheErrorNamesTheFileAndTheField(string find, string replace, string names)
    {
        Assert.Contains(find, Good, StringComparison.Ordinal);
        var error = Assert.Throws<RecipeException>(() => RecipeFiles.Parse(Good.Replace(find, replace), "good.json"));
        Assert.StartsWith("good.json:", error.Message, StringComparison.Ordinal);
        Assert.Contains(names, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentsAreNotAllowed() =>
        Assert.Throws<RecipeException>(() => RecipeFiles.Parse("// a pluck\n" + Good, "good.json"));
}
