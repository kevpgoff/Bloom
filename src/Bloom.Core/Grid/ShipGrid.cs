using System.Text.Json.Serialization;

namespace Bloom.Core.Grid;

public enum AttachError
{
    None,
    Empty,
    OutOfBounds,
    Overlaps,
    NotTouching,
}

/// <summary>
/// The hull: the cells of the ship, inside a box of <see cref="Rules.GridColumns"/> by <see cref="Rules.GridRows"/>.
/// Each column is a lane. Hull sections attach to its edges during a run.
/// </summary>
public sealed class ShipGrid
{
    private readonly HashSet<Cell> _cells;

    [JsonConstructor]
    public ShipGrid(IReadOnlyList<Cell> cells)
    {
        _cells = new HashSet<Cell>(cells);
        foreach (var cell in _cells)
        {
            if (!InBounds(cell))
            {
                throw new ArgumentException($"Hull cell {cell} is outside the {Rules.GridColumns}x{Rules.GridRows} grid.", nameof(cells));
            }
        }
    }

    /// <summary>Hull cells, front row first, then left to right.</summary>
    public IReadOnlyList<Cell> Cells => Cell.RowMajor(_cells).ToArray();

    [JsonIgnore]
    public int CellCount => _cells.Count;

    public static ShipGrid FromShape(Shape hull, Cell origin) => new(hull.CellsAt(origin, Rotation.R0));

    public static bool InBounds(Cell cell) =>
        cell.X >= 0 && cell.X < Rules.GridColumns && cell.Y >= 0 && cell.Y < Rules.GridRows;

    public bool Contains(Cell cell) => _cells.Contains(cell);

    /// <summary>
    /// The lane a heavy attack aimed at <paramref name="aimed"/> hits: that lane when the hull reaches it, otherwise
    /// the nearest lane it does reach (the lower one on a tie). So an attack aimed at an edge lane the hull has not
    /// grown into yet hits the hull's own edge lane (owner, 2026-11-23).
    /// </summary>
    public int LaneHit(int aimed)
    {
        int best = aimed;
        int bestDistance = int.MaxValue;
        foreach (int lane in _cells.Select(c => c.X).Distinct().Order())
        {
            int distance = Math.Abs(lane - aimed);
            if (distance < bestDistance)
            {
                (best, bestDistance) = (lane, distance);
            }
        }

        return best;
    }

    /// <summary>A hull section must stay in bounds, must not overlap the hull and must share an edge with it.</summary>
    public AttachError CanAttach(IReadOnlyCollection<Cell> section)
    {
        if (section.Count == 0)
        {
            return AttachError.Empty;
        }

        if (section.Any(c => !InBounds(c)))
        {
            return AttachError.OutOfBounds;
        }

        if (section.Any(_cells.Contains))
        {
            return AttachError.Overlaps;
        }

        bool touches = section.Any(c => c.EdgeNeighbors().Any(_cells.Contains));
        return touches ? AttachError.None : AttachError.NotTouching;
    }

    public void Attach(IReadOnlyCollection<Cell> section)
    {
        var error = CanAttach(section);
        if (error != AttachError.None)
        {
            throw new InvalidOperationException($"Cannot attach hull section: {error}.");
        }

        _cells.UnionWith(section);
    }
}
