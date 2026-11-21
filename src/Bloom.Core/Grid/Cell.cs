namespace Bloom.Core.Grid;

/// <summary>
/// A square on the ship grid. <see cref="X"/> is the column, which is also the lane.
/// <see cref="Y"/> is the row; row 0 is the front of the ship, which faces the enemy at the top of the screen.
/// </summary>
public readonly record struct Cell(int X, int Y)
{
    public static Cell operator +(Cell a, Cell b) => new(a.X + b.X, a.Y + b.Y);

    public static Cell operator -(Cell a, Cell b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>The four cells that share an edge with this one. Corners do not count as adjacent.</summary>
    public IEnumerable<Cell> EdgeNeighbors()
    {
        yield return new Cell(X, Y - 1);
        yield return new Cell(X + 1, Y);
        yield return new Cell(X, Y + 1);
        yield return new Cell(X - 1, Y);
    }

    public override string ToString() => $"({X},{Y})";

    /// <summary>Sorts cells front row first, then left to right.</summary>
    public static IOrderedEnumerable<Cell> RowMajor(IEnumerable<Cell> cells) =>
        cells.OrderBy(c => c.Y).ThenBy(c => c.X);
}
