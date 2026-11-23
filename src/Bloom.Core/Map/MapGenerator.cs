using Bloom.Core.Rng;

namespace Bloom.Core.Map;

/// <summary>
/// Makes a sector map from the run's random numbers, so the same run always gets the same map.
/// </summary>
/// <remarks>
/// Defaults taken on 2026-11-22, all in <see cref="Rules"/>: <see cref="Rules.MapPaths"/> paths walk up a
/// grid of <see cref="Rules.MapRows"/> rows by <see cref="Rules.MapColumns"/> columns, each step going at
/// most one column left or right and never crossing an edge already made. The cells they visit are the
/// nodes, and every node on the last row leads to the boss. The opening rows are skirmishes, the row before
/// the boss is <see cref="Rules.MapRowBeforeBoss"/>, and the rest are rolled from
/// <see cref="Rules.MapNodeWeights"/>, with no type in <see cref="Rules.MapNoRepeatTypes"/> following itself
/// on a path. The types are rolled again until every type in <see cref="Rules.MapRequiredTypes"/> that has a
/// weight appears. Only arrays are walked, never hash sets, so the order never depends on hashing.
/// </remarks>
public static class MapGenerator
{
    private const int MaxTypeRolls = 100;

    public static SectorMap Generate(Pcg32 rng, int sector)
    {
        const int rows = Rules.MapRows;
        const int columns = Rules.MapColumns;
        var visited = new bool[rows, columns];

        // links[r, a, b]: an edge from column a on row r to column b on row r + 1.
        var links = new bool[rows - 1, columns, columns];
        int firstStart = -1;
        for (int p = 0; p < Rules.MapPaths; p++)
        {
            int column = rng.Next(columns);
            if (p == 1 && column == firstStart)
            {
                // At least two different starts, so the first choice is a real one.
                column = (column + 1 + rng.Next(columns - 1)) % columns;
            }

            if (p == 0)
            {
                firstStart = column;
            }

            visited[0, column] = true;
            for (int r = 1; r < rows; r++)
            {
                int next = Math.Clamp(column + rng.Next(3) - 1, 0, columns - 1);
                if (Crosses(links, r - 1, column, next))
                {
                    next = column;
                }

                links[r - 1, column, next] = true;
                visited[r, next] = true;
                column = next;
            }
        }

        var ids = new int[rows, columns];
        var cells = new List<(int Row, int Column)>();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                ids[r, c] = -1;
                if (visited[r, c])
                {
                    ids[r, c] = cells.Count;
                    cells.Add((r, c));
                }
            }
        }

        int bossId = cells.Count;
        var edges = new List<MapEdge>();
        foreach (var (r, a) in cells)
        {
            if (r == rows - 1)
            {
                edges.Add(new MapEdge(ids[r, a], bossId));
                continue;
            }

            for (int b = 0; b < columns; b++)
            {
                if (links[r, a, b])
                {
                    edges.Add(new MapEdge(ids[r, a], ids[r + 1, b]));
                }
            }
        }

        var types = RollTypes(rng, cells, edges);
        var map = new SectorMap { Sector = sector, Edges = edges };
        for (int id = 0; id < cells.Count; id++)
        {
            map.Nodes.Add(new MapNode(id, types[id], cells[id].Row, cells[id].Column));
        }

        map.Nodes.Add(new MapNode(bossId, NodeType.Boss, rows, (columns - 1) / 2));
        return map;
    }

    /// <summary>Whether an edge from column <paramref name="from"/> to <paramref name="to"/> after row <paramref name="row"/> would cross one already made.</summary>
    private static bool Crosses(bool[,,] links, int row, int from, int to)
    {
        int columns = links.GetLength(1);
        for (int a = 0; a < columns; a++)
        {
            for (int b = 0; b < columns; b++)
            {
                if (links[row, a, b] && ((a < from && b > to) || (a > from && b < to)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static NodeType[] RollTypes(Pcg32 rng, List<(int Row, int Column)> cells, List<MapEdge> edges)
    {
        var parents = new List<int>[cells.Count];
        for (int id = 0; id < cells.Count; id++)
        {
            parents[id] = [];
        }

        foreach (var edge in edges.Where(e => e.To < cells.Count))
        {
            parents[edge.To].Add(edge.From);
        }

        var required = Rules.MapRequiredTypes
            .Where(t => Rules.MapNodeWeights.Any(w => w.Type == t && w.Weight > 0))
            .ToList();
        var types = new NodeType[cells.Count];
        for (int attempt = 0; attempt < MaxTypeRolls; attempt++)
        {
            for (int id = 0; id < cells.Count; id++)
            {
                types[id] = RollType(rng, cells[id].Row, parents[id].Select(p => types[p]).ToList());
            }

            if (required.All(t => types.Contains(t)))
            {
                break;
            }
        }

        return types;
    }

    private static NodeType RollType(Pcg32 rng, int row, List<NodeType> parentTypes)
    {
        if (row < Rules.MapOpeningRows)
        {
            return NodeType.Skirmish;
        }

        if (row == Rules.MapRows - 1 && Rules.MapRowBeforeBoss is { } fixedType)
        {
            return fixedType;
        }

        // A type that may not follow itself also may not lead into a fixed row of that type.
        var nextRowType = row + 1 == Rules.MapRows - 1 ? Rules.MapRowBeforeBoss : null;
        bool Allowed(NodeType type) =>
            !Rules.MapNoRepeatTypes.Contains(type) || (!parentTypes.Contains(type) && nextRowType != type);

        var choices = Rules.MapNodeWeights.Where(w => w.Weight > 0 && Allowed(w.Type)).ToList();
        if (choices.Count == 0)
        {
            return NodeType.Skirmish;
        }

        int roll = rng.Next(choices.Sum(w => w.Weight));
        foreach (var (type, weight) in choices)
        {
            roll -= weight;
            if (roll < 0)
            {
                return type;
            }
        }

        return choices[^1].Type;
    }
}
