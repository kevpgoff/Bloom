using System.Text.Json.Serialization;

namespace Bloom.Core.Grid;

/// <summary>
/// An immutable set of cells, normalized so that its top-left corner is (0,0). In data files a shape is
/// written as rows, where <c>#</c> is a cell and <c>.</c> is empty: <c>["###", ".#."]</c> is a T.
/// </summary>
[JsonConverter(typeof(ShapeJsonConverter))]
public sealed class Shape : IEquatable<Shape>
{
    private readonly Cell[] _cells;

    private Shape(Cell[] normalizedCells)
    {
        _cells = normalizedCells;
        Width = _cells.Length == 0 ? 0 : _cells.Max(c => c.X) + 1;
        Height = _cells.Length == 0 ? 0 : _cells.Max(c => c.Y) + 1;
    }

    /// <summary>Cells front row first, then left to right.</summary>
    public IReadOnlyList<Cell> Cells => _cells;

    public int Count => _cells.Length;

    public int Width { get; }

    public int Height { get; }

    public static Shape FromCells(IEnumerable<Cell> cells)
    {
        var distinct = cells.Distinct().ToArray();
        if (distinct.Length == 0)
        {
            return new Shape([]);
        }

        int minX = distinct.Min(c => c.X);
        int minY = distinct.Min(c => c.Y);
        var offset = new Cell(minX, minY);
        return new Shape(Cell.RowMajor(distinct.Select(c => c - offset)).ToArray());
    }

    public static Shape Parse(IReadOnlyList<string> rows)
    {
        var cells = new List<Cell>();
        for (int y = 0; y < rows.Count; y++)
        {
            string row = rows[y];
            for (int x = 0; x < row.Length; x++)
            {
                switch (row[x])
                {
                    case '#':
                        cells.Add(new Cell(x, y));
                        break;
                    case '.':
                    case ' ':
                        break;
                    default:
                        throw new FormatException($"Unexpected character '{row[x]}' in shape row \"{row}\". Use '#' for a cell and '.' for empty.");
                }
            }
        }

        return FromCells(cells);
    }

    public Shape Rotated(Rotation rotation) =>
        rotation == Rotation.R0 ? this : FromCells(_cells.Select(c => rotation.Apply(c)));

    /// <summary>
    /// The cells this shape covers on the grid after rotation. <paramref name="origin"/> is the top-left
    /// corner of the rotated shape's bounding box.
    /// </summary>
    public IReadOnlyList<Cell> CellsAt(Cell origin, Rotation rotation) =>
        Rotated(rotation)._cells.Select(c => c + origin).ToArray();

    /// <summary>True when every cell can reach every other cell through shared edges.</summary>
    public bool IsConnected()
    {
        if (_cells.Length == 0)
        {
            return false;
        }

        var remaining = new HashSet<Cell>(_cells);
        var frontier = new Queue<Cell>();
        frontier.Enqueue(_cells[0]);
        remaining.Remove(_cells[0]);
        while (frontier.Count > 0)
        {
            foreach (var neighbor in frontier.Dequeue().EdgeNeighbors())
            {
                if (remaining.Remove(neighbor))
                {
                    frontier.Enqueue(neighbor);
                }
            }
        }

        return remaining.Count == 0;
    }

    public IReadOnlyList<string> ToRows()
    {
        var filled = new HashSet<Cell>(_cells);
        var rows = new string[Height];
        for (int y = 0; y < Height; y++)
        {
            var chars = new char[Width];
            for (int x = 0; x < Width; x++)
            {
                chars[x] = filled.Contains(new Cell(x, y)) ? '#' : '.';
            }

            rows[y] = new string(chars);
        }

        return rows;
    }

    public bool Equals(Shape? other) => other is not null && _cells.AsSpan().SequenceEqual(other._cells);

    public override bool Equals(object? obj) => Equals(obj as Shape);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var cell in _cells)
        {
            hash.Add(cell);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Join("/", ToRows());
}
