using System.Text.Json.Serialization;
using Bloom.Core.Grid;

namespace Bloom.Core.Content;

/// <summary>
/// A ship class's special rule, declared in ships.json as an object whose first property is <c>"type"</c>
/// (e.g. <c>{ "type": "keepEnergy" }</c>). Each derived record holds whatever parameters its rule needs.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeepEnergyRule), "keepEnergy")]
public abstract record ShipRule;

/// <summary>Unspent energy stays for the next turn instead of resetting to <see cref="Rules.EnergyPerTurn"/>.</summary>
public sealed record KeepEnergyRule : ShipRule;

/// <summary>
/// What opens a ship class that is locked at first, declared in ships.json like a <see cref="ShipRule"/>
/// (e.g. <c>{ "type": "winWith", "ship": "seedling" }</c>). Whether it is met is worked out from the profile
/// (<see cref="Profile.ProfileEngine.IsUnlocked"/>).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WinWithUnlock), "winWith")]
public abstract record UnlockRule;

/// <summary>Open once a recorded voyage was won with the ship class <see cref="Ship"/>, with any of its loadouts.</summary>
public sealed record WinWithUnlock(string Ship) : UnlockRule;

/// <summary>A piece the ship class starts with, at an absolute grid position.</summary>
public sealed record StartingPiece(string Piece, Cell Origin, Rotation Rotation = Rotation.R0);

/// <summary>
/// One set of starting pieces for a ship class. A class has one or more; the player picks one in the hangar.
/// A loadout changes only the pieces: the hull, the hull points and the rule belong to the class.
/// <see cref="Placeholder"/> marks loadouts invented for testing.
/// </summary>
public sealed record ShipLoadout(string Id, string Name, IReadOnlyList<StartingPiece> StartingPieces, bool Placeholder = false);

/// <summary>
/// A ship class as defined in ships.json: its starting hull, hull points, loadouts and optional special rule.
/// <see cref="HullOrigin"/> places the hull shape inside the 6x8 grid, leaving room for hull sections.
/// </summary>
/// <param name="Loadouts">The sets of starting pieces to choose from. The first is the one a run uses when none is named.</param>
/// <param name="SoundSet">
/// The name of the ship's own set of sound effects, or null for a ship that uses only the base set. The rules
/// never read it; the game plays the set's version of a sound where the set has one.
/// </param>
/// <param name="Commune">The commune that grows the ship, written as on pieces (e.g. <c>sunwrights</c>). The rules never read it.</param>
/// <param name="GrownFrom">One line on what the ship is grown from, for the hangar. The rules never read it.</param>
/// <param name="Unlock">What opens the ship class, or null for one that is open from the start.</param>
public sealed record ShipClassDef(
    string Id,
    string Name,
    int Hull,
    Shape HullShape,
    Cell HullOrigin,
    IReadOnlyList<ShipLoadout> Loadouts,
    ShipRule? Rule = null,
    string? SoundSet = null,
    string? Commune = null,
    string? GrownFrom = null,
    UnlockRule? Unlock = null,
    bool Placeholder = false)
{
    /// <summary>The loadout with this id, or the first one when <paramref name="id"/> is null.</summary>
    public ShipLoadout Loadout(string? id) =>
        id is null
            ? Loadouts[0]
            : Loadouts.FirstOrDefault(l => l.Id == id) ?? throw new KeyNotFoundException($"Ship class '{Id}' has no loadout '{id}'.");
}
