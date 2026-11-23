using Bloom.Core.Content;

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
