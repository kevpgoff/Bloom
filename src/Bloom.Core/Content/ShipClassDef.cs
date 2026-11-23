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

/// <summary>A piece the ship class starts with, at an absolute grid position.</summary>
public sealed record StartingPiece(string Piece, Cell Origin, Rotation Rotation = Rotation.R0);

/// <summary>
/// A ship class as defined in ships.json: its starting hull, hull points, pieces and optional special rule.
/// <see cref="HullOrigin"/> places the hull shape inside the 6x8 grid, leaving room for hull sections.
/// </summary>
public sealed record ShipClassDef(
    string Id,
    string Name,
    int Hull,
    Shape HullShape,
    Cell HullOrigin,
    IReadOnlyList<StartingPiece> StartingPieces,
    ShipRule? Rule = null,
    bool Placeholder = false);
