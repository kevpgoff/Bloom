using Bloom.Core;
using Bloom.Core.Grid;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Maps grid cells to screen rectangles. <see cref="MinRow"/> is the grid row drawn at the top, so a
/// ship can show only the rows its hull covers; a lone piece uses 0.
/// </summary>
internal readonly record struct GridLayout(float CellSize, Vector2 Origin, int MinRow, int Rows)
{
    public Rect2 CellRect(Cell c) => new(Origin + new Vector2(c.X * CellSize, (c.Y - MinRow) * CellSize), new Vector2(CellSize, CellSize));

    public Rect2 LaneRect(int lane) => new(Origin + new Vector2(lane * CellSize, 0), new Vector2(CellSize, Rows * CellSize));

    public Rect2 Bounds => new(Origin, new Vector2(Rules.GridColumns * CellSize, Rows * CellSize));

    /// <summary>The cell under a point, which may lie outside the grid.</summary>
    public Cell CellAt(Vector2 point)
    {
        var local = (point - Origin) / CellSize;
        return new Cell(Mathf.FloorToInt(local.X), Mathf.FloorToInt(local.Y) + MinRow);
    }

    /// <summary>
    /// Fits all lanes and the hull's rows into <paramref name="area"/>, leaving <paramref name="markerHeight"/>
    /// above the grid for the heavy-attack marker. Null when there is no room or no hull.
    /// </summary>
    public static GridLayout? ForHull(Rect2 area, IReadOnlyCollection<Cell> hull, float markerHeight)
    {
        if (hull.Count == 0 || area.Size.X <= 0 || area.Size.Y <= markerHeight)
        {
            return null;
        }

        int minRow = hull.Min(c => c.Y);
        int rows = hull.Max(c => c.Y) - minRow + 1;
        float cell = Mathf.Floor(Mathf.Min(area.Size.X / Rules.GridColumns, (area.Size.Y - markerHeight) / rows));
        var size = new Vector2(cell * Rules.GridColumns, cell * rows);
        var origin = area.Position + new Vector2((area.Size.X - size.X) / 2, markerHeight + ((area.Size.Y - markerHeight - size.Y) / 2));
        return new GridLayout(cell, origin, minRow, rows);
    }
}

/// <summary>Drawing shared by the combat grid and the fitting board. Placeholder shapes and colors until there is art.</summary>
internal static class GridPainter
{
    public static void DrawLanes(CanvasItem canvas, GridLayout g)
    {
        for (int x = 0; x < Rules.GridColumns; x++)
        {
            canvas.DrawRect(g.LaneRect(x), x % 2 == 0 ? Palette.LaneEven : Palette.LaneOdd);
        }
    }

    /// <summary>Tints a lane that a heavy attack names and puts a marker above it.</summary>
    public static void DrawHeavyLane(CanvasItem canvas, GridLayout g, int lane)
    {
        var laneRect = g.LaneRect(lane);
        canvas.DrawRect(laneRect, Palette.HeavyLane);
        float cx = laneRect.GetCenter().X;
        float top = laneRect.Position.Y;
        canvas.DrawColoredPolygon([new Vector2(cx - 16, top - 32), new Vector2(cx + 16, top - 32), new Vector2(cx, top - 6)], Palette.Heavy);
    }

    public static void DrawHull(CanvasItem canvas, GridLayout g, IEnumerable<Cell> cells)
    {
        foreach (var cell in cells)
        {
            canvas.DrawRect(g.CellRect(cell).Grow(-2), Palette.Hull);
        }
    }

    /// <summary>Fills each cell and the gaps between neighboring cells, so the piece reads as one shape.</summary>
    public static void DrawPiece(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells, Color color)
    {
        float inset = Mathf.Max(2, g.CellSize * 0.04f);
        var set = cells.ToHashSet();
        foreach (var cell in cells)
        {
            var r = g.CellRect(cell);
            canvas.DrawRect(r.Grow(-inset), color);
            if (set.Contains(new Cell(cell.X + 1, cell.Y)))
            {
                canvas.DrawRect(new Rect2(r.End.X - inset, r.Position.Y + inset, inset * 2, g.CellSize - (inset * 2)), color);
            }

            if (set.Contains(new Cell(cell.X, cell.Y + 1)))
            {
                canvas.DrawRect(new Rect2(r.Position.X + inset, r.End.Y - inset, g.CellSize - (inset * 2), inset * 2), color);
            }
        }
    }

    /// <summary>Draws the piece's outer edges only.</summary>
    public static void DrawOutline(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells, Color color, float width)
    {
        var set = cells.ToHashSet();
        foreach (var cell in cells)
        {
            var r = g.CellRect(cell);
            var tl = r.Position;
            var tr = new Vector2(r.End.X, r.Position.Y);
            var bl = new Vector2(r.Position.X, r.End.Y);
            var br = r.End;
            if (!set.Contains(new Cell(cell.X, cell.Y - 1)))
            {
                canvas.DrawLine(tl, tr, color, width);
            }

            if (!set.Contains(new Cell(cell.X, cell.Y + 1)))
            {
                canvas.DrawLine(bl, br, color, width);
            }

            if (!set.Contains(new Cell(cell.X - 1, cell.Y)))
            {
                canvas.DrawLine(tl, bl, color, width);
            }

            if (!set.Contains(new Cell(cell.X + 1, cell.Y)))
            {
                canvas.DrawLine(tr, br, color, width);
            }
        }
    }

    /// <summary>Writes the text on the piece's first cell and across the cells to its right in the same row.</summary>
    public static void DrawLabel(CanvasItem canvas, Font font, GridLayout g, IReadOnlyCollection<Cell> cells, string text, Color color)
    {
        var set = cells.ToHashSet();
        var first = Cell.RowMajor(cells).First();
        int width = 1;
        while (set.Contains(new Cell(first.X + width, first.Y)))
        {
            width++;
        }

        int fontSize = (int)Mathf.Clamp(g.CellSize * 0.17f, 14, 24);
        var rect = g.CellRect(first).Grow(-8);
        canvas.DrawMultilineString(
            font,
            rect.Position + new Vector2(0, font.GetAscent(fontSize)),
            text,
            HorizontalAlignment.Center,
            (width * g.CellSize) - 16,
            fontSize,
            maxLines: 3,
            modulate: color);
    }

    /// <summary>The "+" badge on a piece that receives an adjacency bonus, in the corner of its first cell.</summary>
    public static void DrawBonusBadge(CanvasItem canvas, Font font, GridLayout g, IReadOnlyCollection<Cell> cells, Color color)
    {
        var corner = g.CellRect(Cell.RowMajor(cells).First());
        var center = new Vector2(corner.End.X - 14, corner.Position.Y + 14);
        canvas.DrawCircle(center, 11, color);
        canvas.DrawString(font, center + new Vector2(-7, 8), "+", HorizontalAlignment.Center, 14, 22, Palette.PieceText);
    }
}
