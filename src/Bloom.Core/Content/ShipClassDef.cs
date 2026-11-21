using Bloom.Core.Grid;

namespace Bloom.Core.Content;

/// <summary>A piece the ship class starts with, at an absolute grid position.</summary>
public sealed record StartingPiece(string Piece, Cell Origin, Rotation Rotation = Rotation.R0);

/// <summary>
/// A ship class as defined in ships.json: its starting hull, hull points and pieces.
/// <see cref="HullOrigin"/> places the hull shape inside the 6x8 grid, leaving room for hull sections.
/// </summary>
public sealed record ShipClassDef(
    string Id,
    string Name,
    int Hull,
    Shape HullShape,
    Cell HullOrigin,
    IReadOnlyList<StartingPiece> StartingPieces,
    bool Placeholder = false);
