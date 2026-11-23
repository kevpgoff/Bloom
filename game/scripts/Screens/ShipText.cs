using Bloom.Core;
using Bloom.Core.Content;
using Bloom.Core.Profile;

namespace Bloom.Game.Screens;

/// <summary>Words for a ship class, as <see cref="CardText"/> has them for cards and pieces.</summary>
internal static class ShipText
{
    /// <summary>A ship's special rule as one sentence, or empty for a ship without one.</summary>
    public static string Rule(ShipRule? rule) => rule switch
    {
        null => "",
        KeepEnergyRule => "Unspent energy stays for the next turn.",
        _ => rule.GetType().Name,
    };

    /// <summary>What opens a locked ship class, as a fact: "a won voyage with the Seedling". Empty for a ship without a lock.</summary>
    public static string Unlock(UnlockRule? unlock, ContentDb content) => unlock switch
    {
        null => "",
        WinWithUnlock win => $"a won voyage with the {content.Ship(win.Ship).Name}",
        _ => unlock.GetType().Name,
    };

    /// <summary>How far a voyage came: "row 4 of 7", "the boss", or with its sector in front when a run has several.</summary>
    public static string Progress(VoyageProgress progress)
    {
        string row = progress.Rows > Rules.MapRows ? "the boss" : progress.Rows == 0 ? "the start" : $"row {progress.Rows} of {Rules.MapRows}";
        return Rules.SectorsPerRun > 1 ? $"sector {progress.Sector}, {row}" : row;
    }

    /// <summary>A commune's name from its id in the data: "tide_orchard" gives "Tide Orchard".</summary>
    public static string Commune(string? id) =>
        string.IsNullOrEmpty(id)
            ? ""
            : string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    /// <summary>"A · Sprout": a loadout's place among its ship's loadouts, as a letter, and its name.</summary>
    public static string Loadout(ShipClassDef ship, ShipLoadout loadout)
    {
        int index = Math.Max(0, ship.Loadouts.ToList().IndexOf(loadout));
        return $"{(char)('A' + index)} · {loadout.Name}";
    }
}
