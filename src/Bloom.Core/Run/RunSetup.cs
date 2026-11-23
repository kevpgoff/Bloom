namespace Bloom.Core.Run;

/// <summary>
/// What the player chooses in the hangar before a run: the ship class, one of its loadouts and the seed.
/// Later choices for a run, such as a voyage level, belong here too.
/// </summary>
/// <param name="Seed">Where all of the run's randomness starts. The same setup always gives the same run.</param>
/// <param name="SeedText">The seed as the player sees and types it (<see cref="RunSeed"/>), or null for a seed that is only a number.</param>
/// <param name="LoadoutId">One of the ship class's loadouts, or null for its first.</param>
public sealed record RunSetup(string ShipClassId, ulong Seed, string? SeedText = null, string? LoadoutId = null)
{
    /// <summary>A setup whose seed comes from a code or from text that the player typed.</summary>
    public static RunSetup FromSeedText(string shipClassId, string seedText, string? loadoutId = null)
    {
        string text = RunSeed.Normalize(seedText);
        return new RunSetup(shipClassId, RunSeed.FromText(text), text, loadoutId);
    }
}
