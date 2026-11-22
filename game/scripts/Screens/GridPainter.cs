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

/// <summary>
/// Option A geometric vector grid drawing shared by the combat grid, fitting board, and reward previews.
/// Draws crisp solar hull deck plates, connected polyomino pieces with bevels, and upright centered icons.
/// </summary>
internal static class GridPainter
{
    public static void DrawLanes(CanvasItem canvas, GridLayout g)
    {
        var bounds = g.Bounds;
        var guide = Palette.BrassLine with { A = 0.14f };
        var tick = Palette.BrassLine with { A = 0.32f };

        for (int x = 0; x < Rules.GridColumns; x++)
        {
            var lane = g.LaneRect(x);
            var wash = x % 2 == 0 ? Palette.LaneEven : Palette.LaneOdd;
            if (wash.A > 0f)
            {
                canvas.DrawRect(lane, wash);
            }

            // Small navigation tick at the top of each lane so columns remain scannable in open space.
            float cx = lane.GetCenter().X;
            canvas.DrawLine(new Vector2(cx - 5f, bounds.Position.Y - 4f), new Vector2(cx + 5f, bounds.Position.Y - 4f), tick, 1.5f);
        }

        // Faint dashed guides on both sides of every lane, with no top or bottom edge, so empty lanes
        // read as open space but all 6 lanes can still be counted.
        for (int x = 0; x <= Rules.GridColumns; x++)
        {
            float lx = bounds.Position.X + (x * g.CellSize);
            canvas.DrawDashedLine(
                new Vector2(lx, bounds.Position.Y),
                new Vector2(lx, bounds.End.Y),
                guide,
                width: 1.2f,
                dash: 6f);
        }
    }

    /// <summary>Tints a lane that a heavy attack names and draws a vector warning chevron above it.</summary>
    public static void DrawHeavyLane(CanvasItem canvas, GridLayout g, int lane)
    {
        var laneRect = g.LaneRect(lane);
        canvas.DrawRect(laneRect, Palette.HeavyLane);
        canvas.DrawDashedLine(laneRect.Position, new Vector2(laneRect.Position.X, laneRect.End.Y), Palette.Heavy with { A = 0.55f }, width: 2f, dash: 8f);
        canvas.DrawDashedLine(new Vector2(laneRect.End.X, laneRect.Position.Y), laneRect.End, Palette.Heavy with { A = 0.55f }, width: 2f, dash: 8f);

        float cx = laneRect.GetCenter().X;
        float top = laneRect.Position.Y;
        canvas.DrawColoredPolygon([new Vector2(cx - 15, top - 30), new Vector2(cx + 15, top - 30), new Vector2(cx, top - 7)], Palette.Heavy);
        canvas.DrawPolyline([new Vector2(cx - 19, top - 22), new Vector2(cx, top - 3), new Vector2(cx + 19, top - 22)], Palette.Heavy with { A = 0.65f }, 2.2f, antialiased: true);
    }

    public static void DrawHull(CanvasItem canvas, GridLayout g, IEnumerable<Cell> cells)
    {
        var hullSet = cells.ToHashSet();

        // Grounded drop shadow + solid hull backing so the playable ship silhouette pops from open space.
        foreach (var cell in hullSet)
        {
            var r = g.CellRect(cell);
            canvas.DrawRect(new Rect2(r.Position + new Vector2(0, 5f), r.Size), Palette.HullShadow);
        }

        foreach (var cell in hullSet)
        {
            var r = g.CellRect(cell);
            canvas.DrawRect(r, Palette.SurfaceDeep);
            var plate = r.Grow(-2.5f);
            canvas.DrawRect(plate, Palette.Hull);
            canvas.DrawRect(plate.Grow(-1f), Palette.HullBorder with { A = 0.6f }, filled: false, width: 1.2f);

            // Subtle geometric rivet dot at deck-plate center.
            canvas.DrawCircle(plate.GetCenter(), Mathf.Clamp(g.CellSize * 0.04f, 1.5f, 3f), Palette.BrassLine with { A = 0.3f });
        }

        DrawOutline(canvas, g, hullSet, Palette.BrassBright with { A = 0.78f }, 2.5f);
    }

    /// <summary>Fills each cell and the gaps between neighboring cells, adding a clean vector bevel and dark seam.</summary>
    public static void DrawPiece(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells, Color color)
    {
        float inset = Mathf.Max(2f, g.CellSize * 0.045f);
        var set = cells.ToHashSet();

        // Dark botanical-slate interior base with luminous commune-tinted fill.
        var body = color.Darkened(0.08f);
        foreach (var cell in set)
        {
            var r = g.CellRect(cell);
            canvas.DrawRect(r.Grow(-inset), body);
            if (set.Contains(new Cell(cell.X + 1, cell.Y)))
            {
                canvas.DrawRect(new Rect2(r.End.X - inset, r.Position.Y + inset, inset * 2, g.CellSize - (inset * 2)), body);
            }

            if (set.Contains(new Cell(cell.X, cell.Y + 1)))
            {
                canvas.DrawRect(new Rect2(r.Position.X + inset, r.End.Y - inset, g.CellSize - (inset * 2), inset * 2), body);
            }
        }

        // Subtle internal cell seam marks so multi-cell geometry stays legible.
        var seam = color.Darkened(0.32f) with { A = color.A * 0.45f };
        foreach (var cell in set)
        {
            var r = g.CellRect(cell);
            if (set.Contains(new Cell(cell.X + 1, cell.Y)))
            {
                canvas.DrawLine(new Vector2(r.End.X, r.Position.Y + inset + 6), new Vector2(r.End.X, r.End.Y - inset - 6), seam, 1.2f);
            }

            if (set.Contains(new Cell(cell.X, cell.Y + 1)))
            {
                canvas.DrawLine(new Vector2(r.Position.X + inset + 6, r.End.Y), new Vector2(r.End.X - inset - 6, r.End.Y), seam, 1.2f);
            }
        }

        // Top-left solar sheen highlight & crisp outer dark contour.
        var highlight = color.Lightened(0.32f) with { A = color.A * 0.7f };
        var edge = color.Darkened(0.55f) with { A = color.A * 0.85f };
        foreach (var cell in set)
        {
            var r = g.CellRect(cell).Grow(-inset);
            if (!set.Contains(new Cell(cell.X, cell.Y - 1)))
            {
                canvas.DrawLine(new Vector2(r.Position.X + 2, r.Position.Y + 1.5f), new Vector2(r.End.X - 2, r.Position.Y + 1.5f), highlight, 2f);
            }

            if (!set.Contains(new Cell(cell.X - 1, cell.Y)))
            {
                canvas.DrawLine(new Vector2(r.Position.X + 1.5f, r.Position.Y + 2), new Vector2(r.Position.X + 1.5f, r.End.Y - 2), highlight, 1.5f);
            }
        }

        DrawInsetOutline(canvas, g, set, inset, edge, 2f);
    }

    /// <summary>
    /// Draws a piece's monochrome vector icon upright at the visual center of its polyomino, so
    /// L-, T-, S-, and U-shapes remain instantly recognizable at every rotation.
    /// </summary>
    public static void DrawPieceIcon(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells, Texture2D? icon, Color tint, bool reserveLabelSpace = true)
    {
        if (icon is null || cells.Count == 0)
        {
            return;
        }

        var (center, span) = IconAnchor(g, cells);
        float maxSide = reserveLabelSpace ? span * 0.56f : span * 0.70f;
        float iconSize = Mathf.Clamp(maxSide, 14f, g.CellSize * 1.15f);
        var drawCenter = reserveLabelSpace && g.CellSize >= 46f
            ? center + new Vector2(0, -g.CellSize * 0.11f)
            : center;
        var rect = new Rect2(drawCenter - new Vector2(iconSize / 2f, iconSize / 2f), new Vector2(iconSize, iconSize));

        // Subtle dark medallion halo behind the vector glyph for contrast on bright commune colors.
        canvas.DrawCircle(drawCenter, iconSize * 0.54f, new Color(0.06f, 0.10f, 0.12f, 0.24f * tint.A));
        canvas.DrawTextureRect(icon, rect, tile: false, modulate: tint);
    }

    /// <summary>Draws diagonal offline striping across a disabled piece.</summary>
    public static void DrawDisabledOverlay(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells)
    {
        float inset = Mathf.Max(2f, g.CellSize * 0.045f);
        var lineColor = Palette.DisabledText with { A = 0.22f };
        foreach (var cell in cells)
        {
            var r = g.CellRect(cell).Grow(-inset - 2f);
            canvas.DrawLine(r.Position, r.End, lineColor, 1.5f);
            canvas.DrawLine(new Vector2(r.Position.X, r.End.Y), new Vector2(r.End.X, r.Position.Y), lineColor, 1.5f);
        }
    }

    /// <summary>Draws the piece's outer edges only.</summary>
    public static void DrawOutline(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells, Color color, float width) =>
        DrawInsetOutline(canvas, g, cells.ToHashSet(), 0f, color, width);

    /// <summary>
    /// Writes the name along the piece's bottom row, under the icon. The font shrinks to fit that row,
    /// down to 12 px; a name still too long there wraps onto two lines.
    /// </summary>
    public static void DrawLabel(CanvasItem canvas, Font font, GridLayout g, IReadOnlyCollection<Cell> cells, string text, Color color)
    {
        var set = cells.ToHashSet();
        int maxY = cells.Max(c => c.Y);
        var anchorRow = Cell.RowMajor(cells.Where(c => c.Y == maxY)).First();
        int width = 1;
        while (set.Contains(new Cell(anchorRow.X + width, anchorRow.Y)))
        {
            width++;
        }

        const int smallest = 12;
        float maxWidth = (width * g.CellSize) - 8f;
        int fontSize = (int)Mathf.Clamp(g.CellSize * 0.18f, 14, 20);
        while (fontSize > smallest && font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X > maxWidth)
        {
            fontSize--;
        }

        var rect = g.CellRect(anchorRow);
        var baseline = new Vector2(rect.Position.X + 4f, rect.End.Y - Mathf.Max(6f, g.CellSize * 0.10f));
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X <= maxWidth)
        {
            canvas.DrawString(font, baseline, text, HorizontalAlignment.Center, maxWidth, fontSize, color);
            return;
        }

        // Two lines whose last baseline sits where the single line would.
        canvas.DrawMultilineString(
            font,
            baseline - new Vector2(0, font.GetHeight(fontSize)),
            text,
            HorizontalAlignment.Center,
            maxWidth,
            fontSize,
            maxLines: 2,
            modulate: color);
    }

    /// <summary>An "OFF" tag in the top-left corner of a disabled piece's first cell; the name stays readable for repair picks.</summary>
    public static void DrawOffTag(CanvasItem canvas, Font font, GridLayout g, IReadOnlyCollection<Cell> cells)
    {
        const int fontSize = 14;
        var corner = g.CellRect(Cell.RowMajor(cells).First());
        var textSize = font.GetStringSize("OFF", HorizontalAlignment.Left, -1, fontSize);
        var tag = new Rect2(corner.Position + new Vector2(6, 6), textSize + new Vector2(10, 4));
        canvas.DrawRect(tag, Palette.Refused);
        canvas.DrawString(font, tag.Position + new Vector2(5, 2 + font.GetAscent(fontSize)), "OFF", HorizontalAlignment.Left, -1, fontSize, Colors.White);
    }

    /// <summary>The "+" badge on a piece that receives an adjacency bonus, in the corner of its first cell.</summary>
    public static void DrawBonusBadge(CanvasItem canvas, Font font, GridLayout g, IReadOnlyCollection<Cell> cells, Color color)
    {
        var corner = g.CellRect(Cell.RowMajor(cells).First());
        var center = new Vector2(corner.End.X - 14, corner.Position.Y + 14);
        canvas.DrawCircle(center, 11, Palette.SurfaceDeep);
        canvas.DrawCircle(center, 9.5f, color);
        canvas.DrawString(font, center + new Vector2(-7, 7), "+", HorizontalAlignment.Center, 14, 20, Palette.PieceText);
    }

    private static (Vector2 Center, float Span) IconAnchor(GridLayout g, IReadOnlyCollection<Cell> cells)
    {
        var set = cells.ToHashSet();
        int minX = set.Min(c => c.X);
        int maxX = set.Max(c => c.X);
        int minY = set.Min(c => c.Y);
        int maxY = set.Max(c => c.Y);
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;

        // Solid rectangles (1x1, 1x2, 1x3, 2x2) center cleanly across the whole block.
        if (set.Count == width * height)
        {
            var tl = g.CellRect(new Cell(minX, minY)).Position;
            var br = g.CellRect(new Cell(maxX, maxY)).End;
            float span = Mathf.Min(width, height) * g.CellSize;
            return ((tl + br) * 0.5f, span);
        }

        // Non-rectangular polyominoes (L, T, S, U) anchor on their hub cell (most neighbors, closest to centroid).
        var centroid = new Vector2((float)set.Average(c => c.X), (float)set.Average(c => c.Y));
        var hub = set
            .OrderByDescending(c => NeighborCount(set, c))
            .ThenBy(c => new Vector2(c.X, c.Y).DistanceSquaredTo(centroid))
            .First();
        return (g.CellRect(hub).GetCenter(), g.CellSize);
    }

    private static int NeighborCount(HashSet<Cell> set, Cell c)
    {
        int n = 0;
        if (set.Contains(new Cell(c.X + 1, c.Y))) n++;
        if (set.Contains(new Cell(c.X - 1, c.Y))) n++;
        if (set.Contains(new Cell(c.X, c.Y + 1))) n++;
        if (set.Contains(new Cell(c.X, c.Y - 1))) n++;
        return n;
    }

    private static void DrawInsetOutline(CanvasItem canvas, GridLayout g, HashSet<Cell> set, float inset, Color color, float width)
    {
        foreach (var cell in set)
        {
            var r = g.CellRect(cell);
            float left = set.Contains(new Cell(cell.X - 1, cell.Y)) ? r.Position.X : r.Position.X + inset;
            float right = set.Contains(new Cell(cell.X + 1, cell.Y)) ? r.End.X : r.End.X - inset;
            float top = set.Contains(new Cell(cell.X, cell.Y - 1)) ? r.Position.Y : r.Position.Y + inset;
            float bottom = set.Contains(new Cell(cell.X, cell.Y + 1)) ? r.End.Y : r.End.Y - inset;

            if (!set.Contains(new Cell(cell.X, cell.Y - 1)))
            {
                canvas.DrawLine(new Vector2(left, top), new Vector2(right, top), color, width);
            }

            if (!set.Contains(new Cell(cell.X, cell.Y + 1)))
            {
                canvas.DrawLine(new Vector2(left, bottom), new Vector2(right, bottom), color, width);
            }

            if (!set.Contains(new Cell(cell.X - 1, cell.Y)))
            {
                canvas.DrawLine(new Vector2(left, top), new Vector2(left, bottom), color, width);
            }

            if (!set.Contains(new Cell(cell.X + 1, cell.Y)))
            {
                canvas.DrawLine(new Vector2(right, top), new Vector2(right, bottom), color, width);
            }
        }
    }
}
