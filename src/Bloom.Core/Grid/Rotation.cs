namespace Bloom.Core.Grid;

/// <summary>Quarter turns, clockwise on screen. Pieces can be rotated but never mirrored.</summary>
public enum Rotation
{
    R0 = 0,
    R90 = 1,
    R180 = 2,
    R270 = 3,
}

public static class RotationExtensions
{
    public static Rotation Clockwise(this Rotation rotation) => (Rotation)(((int)rotation + 1) % 4);

    /// <summary>Rotates a cell around the origin, clockwise in screen coordinates (Y grows downward).</summary>
    public static Cell Apply(this Rotation rotation, Cell cell) => rotation switch
    {
        Rotation.R0 => cell,
        Rotation.R90 => new Cell(-cell.Y, cell.X),
        Rotation.R180 => new Cell(-cell.X, -cell.Y),
        Rotation.R270 => new Cell(cell.Y, -cell.X),
        _ => throw new ArgumentOutOfRangeException(nameof(rotation), rotation, null),
    };
}
