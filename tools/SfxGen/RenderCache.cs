using System.Collections.Concurrent;
using System.Text.Json;

namespace Bloom.SfxGen;

/// <summary>
/// Renders each variant of a recipe once per run of the tool or of the tests. The checks on a sound, the check of
/// the saved files and the sound board all need the same renders, and on CI's single slow core one render can take
/// a tenth of a second, so rendering each sound once and not four times keeps <c>dotnet test</c> short.
/// </summary>
/// <remarks>
/// The key is the recipe's whole content, so two loads of one file share a render and an edited recipe gets a new
/// one. The samples are shared by every caller, so nothing may change them.
/// </remarks>
internal static class RenderCache
{
    private static readonly ConcurrentDictionary<string, Rendered> Renders = new(StringComparer.Ordinal);

    public static Rendered Of(Recipe recipe, int variant) =>
        Renders.GetOrAdd($"{variant}:{JsonSerializer.Serialize(recipe)}", _ => Synth.Render(recipe, variant));
}
