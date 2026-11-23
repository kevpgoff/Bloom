namespace Bloom.SfxGen;

/// <summary>
/// A ship class's sound set: that ship's own version of some of the game's sounds, in the character of what the
/// ship is grown from. A set is a folder in <c>tools/SfxGen/sets/</c> that holds <c>about.json</c> and recipes
/// named like the base recipes in <c>recipes/</c>. Its sounds are rendered into a folder of the same name inside
/// <c>game/audio/sfx/</c>. The game plays them for a ship class whose <c>soundSet</c> in
/// <c>game/data/ships.json</c> names the set, and plays the base sound wherever the set has none.
/// </summary>
internal sealed record SoundSet(string Name, string Title, string Description, IReadOnlyList<Recipe> Recipes);

internal static class SoundSets
{
    /// <summary>
    /// The sounds that may change with the ship: the cards it plays and draws, its hits on the enemy, what it
    /// gains and the hits it takes. Every other sound (the turns, the enemy's sounds, the end of a fight, the
    /// other screens) is the same on every ship, so a set may not hold it.
    /// </summary>
    public static readonly IReadOnlyList<string> ShipSounds =
    [
        "card-play", "card-play-core", "card-play-weapon", "card-play-shield", "card-play-solar", "card-play-garden",
        "card-play-drone", "card-draw",
        "hit-light", "hit-medium", "hit-heavy", "hit-blocked", "focus-spend",
        "block-gain", "energy-gain", "focus-gain", "grow", "repair",
        "ship-hit", "ship-hit-heavy", "ship-blocked", "piece-offline",
    ];

    /// <summary>
    /// A set's sound earns a warning when it is shorter than this share of the base sound's length, or longer
    /// than <see cref="LongestShare"/> of it: a fight should take the same time to hear on any ship.
    /// </summary>
    public const double ShortestShare = 0.5;

    public const double LongestShare = 1.5;

    /// <summary>Every set, in the order of their folder names. No folder means no sets.</summary>
    public static IReadOnlyList<SoundSet> LoadAll(string dir) =>
        TakeFiles.LoadAll(dir, "sets").Select(t => new SoundSet(t.Name, t.Title, t.Description, t.Recipes)).ToList();

    /// <summary>The ids of the ship's own sounds that the set has no version of, for which the game plays the base sound.</summary>
    public static IReadOnlyList<string> Missing(SoundSet set) =>
        ShipSounds.Where(id => set.Recipes.All(r => r.Id != id)).ToList();
}
