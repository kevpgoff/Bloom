using Bloom.Core;
using Bloom.Core.Grid;
using Bloom.Game.Visuals;
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

    /// <summary>A corner of the grid (cell coordinates, so (x, y) is the top-left corner of cell (x, y)) on screen.</summary>
    public Vector2 CornerAt(Vector2I corner) => Origin + new Vector2(corner.X * CellSize, (corner.Y - MinRow) * CellSize);

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
/// Option A "Holo Grove" grid drawing, shared by the combat grid, the fitting board and the piece previews.
/// The ship reads as a lit schematic: root traces across an open deck, and pieces drawn as glowing
/// outlines with scanlines and corner brackets, in their tag's color.
/// </summary>
internal static class GridPainter
{
    /// <summary>Scanlines keep a fixed pitch, so they read as a projection at any cell size.</summary>
    private const float ScanPitch = 5f;

    private static readonly Vector2I[] Sides = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    /// <summary>
    /// Dashed guides on both sides of every lane, and a mark ("L1" to "L6") with a tick above each one, so
    /// all lanes can be counted. Lanes in <paramref name="markedLanes"/> get the heavy-attack marker instead.
    /// Inside the hull, <see cref="DrawHull"/> and <see cref="DrawPiece"/> carry the lane lines on.
    /// </summary>
    public static void DrawLanes(CanvasItem canvas, GridLayout g, IReadOnlyCollection<int>? markedLanes = null)
    {
        var bounds = g.Bounds;
        var guide = Palette.HoloLine with { A = 0.16f };
        for (int x = 0; x <= Rules.GridColumns; x++)
        {
            float lx = bounds.Position.X + (x * g.CellSize);
            canvas.DrawDashedLine(new Vector2(lx, bounds.Position.Y), new Vector2(lx, bounds.End.Y), guide, width: 1f, dash: 4f);
        }

        var font = BloomTheme.Hud;
        int size = LaneMarkSize(g);
        for (int lane = 0; lane < Rules.GridColumns; lane++)
        {
            if (markedLanes?.Contains(lane) == true)
            {
                continue;
            }

            var rect = g.LaneRect(lane);
            float cx = rect.GetCenter().X;
            float top = rect.Position.Y;
            canvas.DrawString(font, new Vector2(rect.Position.X, top - 13), $"L{lane + 1}",
                HorizontalAlignment.Center, g.CellSize, size, Palette.HoloLine with { A = 0.8f });
            canvas.DrawLine(new Vector2(cx, top - 8), new Vector2(cx, top - 2), Palette.HoloLine with { A = 0.6f }, 1.5f);
        }
    }

    /// <summary>A lane that a heavy attack names: an amber dashed column, and a "HEAVY" tag pointing into it.</summary>
    public static void DrawHeavyLane(CanvasItem canvas, GridLayout g, int lane)
    {
        var laneRect = g.LaneRect(lane);
        canvas.DrawRect(laneRect, Palette.Heavy with { A = 0.06f });
        DrawDashedLoop(canvas, Corners(laneRect.Grow(-1f)), Palette.Heavy with { A = 0.75f }, 1.6f, 6f);

        var font = BloomTheme.Hud;
        int size = (int)Mathf.Clamp(g.CellSize * 0.17f, 13, 20);
        float cx = laneRect.GetCenter().X;
        float top = laneRect.Position.Y;
        float half = (font.GetStringSize("HEAVY", HorizontalAlignment.Left, -1, size).X / 2f) + 8f;
        canvas.DrawString(font, new Vector2(cx - half, top - 17), "HEAVY", HorizontalAlignment.Center, half * 2f, size, Palette.Heavy);
        float yTop = top - 17 - font.GetAscent(size) - 2;
        float yBottom = top - 13;
        canvas.DrawPolyline([new Vector2(cx - half + 4, yTop), new Vector2(cx - half, yTop), new Vector2(cx - half, yBottom), new Vector2(cx - half + 4, yBottom)], Palette.Heavy, 1.4f, antialiased: true);
        canvas.DrawPolyline([new Vector2(cx + half - 4, yTop), new Vector2(cx + half, yTop), new Vector2(cx + half, yBottom), new Vector2(cx + half - 4, yBottom)], Palette.Heavy, 1.4f, antialiased: true);
        canvas.DrawColoredPolygon([new Vector2(cx - 7, top - 9), new Vector2(cx + 7, top - 9), new Vector2(cx, top - 2)], Palette.Heavy);
    }

    /// <summary>
    /// The hull as an open deck: each cell a dark pane, root traces from cell to cell, a mark at every
    /// grid point, a thin hologram rim and brackets at the corners of its bounds.
    /// </summary>
    public static void DrawHull(CanvasItem canvas, GridLayout g, IEnumerable<Cell> cells)
    {
        var hull = cells.ToHashSet();
        if (hull.Count == 0)
        {
            return;
        }

        float c = g.CellSize;
        foreach (var cell in hull)
        {
            var r = g.CellRect(cell);
            canvas.DrawRect(r, Palette.Deck);
            if (cell.X % 2 == 1)
            {
                canvas.DrawRect(r, Palette.HoloLine with { A = 0.035f });
            }

            // The lane line between two hull cells side by side; the rim draws the outer ones.
            if (hull.Contains(new Cell(cell.X - 1, cell.Y)))
            {
                canvas.DrawDashedLine(r.Position, new Vector2(r.Position.X, r.End.Y), Palette.HoloLine with { A = 0.2f }, 1f, 4f);
            }
        }

        float jog = c * 0.1f;
        float pad = c * 0.07f;
        float node = Mathf.Max(1.6f, c * 0.03f);
        foreach (var cell in hull)
        {
            var center = g.CellRect(cell).GetCenter();
            if (hull.Contains(new Cell(cell.X + 1, cell.Y)))
            {
                float j = (cell.X + cell.Y) % 2 == 0 ? jog : -jog;
                canvas.DrawPolyline(
                    [center + new Vector2(pad, 0), center + new Vector2((c / 2) - jog, 0), center + new Vector2(c / 2, j),
                     center + new Vector2(c - pad, j), center + new Vector2(c - pad, 0)],
                    Palette.Trace with { A = 0.7f }, 1.2f, antialiased: true);
            }

            if (hull.Contains(new Cell(cell.X, cell.Y + 1)))
            {
                canvas.DrawLine(center + new Vector2(0, pad), center + new Vector2(0, c - pad), Palette.Trace with { A = 0.5f }, 1.2f, antialiased: true);
            }

            canvas.DrawCircle(center, node, Palette.Trace);
        }

        var points = new HashSet<Vector2I>();
        foreach (var cell in hull)
        {
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dy = 0; dy <= 1; dy++)
                {
                    points.Add(new Vector2I(cell.X + dx, cell.Y + dy));
                }
            }
        }

        float arm = Mathf.Clamp(c * 0.06f, 3f, 6f);
        var mark = Palette.HoloLine with { A = 0.38f };
        foreach (var p in points)
        {
            var at = g.CornerAt(p);
            canvas.DrawLine(at - new Vector2(arm, 0), at + new Vector2(arm, 0), mark, 1f);
            canvas.DrawLine(at - new Vector2(0, arm), at + new Vector2(0, arm), mark, 1f);
        }

        foreach (var loop in OutlineLoops(hull))
        {
            canvas.DrawPolyline(Closed(Inset(loop, g, -1f)), Palette.HoloLine with { A = 0.55f }, 1.2f, antialiased: true);
        }

        var tl = g.CornerAt(new Vector2I(hull.Min(h => h.X), hull.Min(h => h.Y))) - new Vector2(8, 8);
        var br = g.CornerAt(new Vector2I(hull.Max(h => h.X) + 1, hull.Max(h => h.Y) + 1)) + new Vector2(8, 8);
        DrawBrackets(canvas, new Rect2(tl, br - tl), c * 0.27f, Palette.HoloLine with { A = 0.9f }, 2f);
    }

    /// <summary>
    /// A piece as a hologram in <paramref name="color"/>: a faint fill, scanlines, an inner glow, a lit edge and
    /// brackets on its outer corners. Every alpha is scaled by the color's, so a faded color draws a faded piece.
    /// An offline piece is drawn as a dashed red outline with a ghost of its edge beside it.
    /// </summary>
    public static void DrawPiece(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells, Color color, bool offline = false)
    {
        var set = cells.ToHashSet();
        if (set.Count == 0)
        {
            return;
        }

        float c = g.CellSize;
        float a = color.A;
        var tint = offline ? Palette.Refused with { A = a } : color;
        float inset = Mathf.Max(2f, c * 0.05f);
        var loops = OutlineLoops(set).Select(loop => (Loop: loop, Body: Inset(loop, g, inset))).ToList();

        foreach (var (_, body) in loops)
        {
            canvas.DrawColoredPolygon(body, tint with { A = a * (offline ? 0.04f : 0.09f) });
        }

        var scan = tint with { A = a * (offline ? 0.06f : 0.12f) };
        foreach (var cell in set)
        {
            var r = g.CellRect(cell);
            for (float y = r.Position.Y + 6f; y < r.End.Y - 4f; y += ScanPitch)
            {
                canvas.DrawLine(new Vector2(r.Position.X + 5f, y), new Vector2(r.End.X - 5f, y), scan, 1f);
            }
        }

        DrawSeams(canvas, g, set, tint, a * (offline ? 0.25f : 0.45f), inset);

        if (offline)
        {
            foreach (var (_, body) in loops)
            {
                var ghost = Closed(body);
                for (int i = 0; i < ghost.Length; i++)
                {
                    ghost[i] += new Vector2(2f, 0f);
                }

                canvas.DrawPolyline(ghost, Palette.HoloLine with { A = a * 0.28f }, 1f, antialiased: true);
                DrawDashedLoop(canvas, body, tint with { A = a * 0.85f }, 1.4f, 4f);
            }

            return;
        }

        float glowInset = inset + Mathf.Max(2.5f, c * 0.06f);
        var bracket = tint.Lerp(Colors.White, 0.3f) with { A = a };
        float bracketLength = Mathf.Max(5f, c * 0.15f);
        float bracketWidth = Mathf.Max(1.8f, c * 0.036f);
        foreach (var (loop, body) in loops)
        {
            canvas.DrawPolyline(Closed(Inset(loop, g, glowInset)), tint with { A = a * 0.1f }, Mathf.Max(4f, c * 0.1f), antialiased: true);
            canvas.DrawPolyline(Closed(body), tint, Mathf.Max(1.5f, c * 0.027f), antialiased: true);
            for (int i = 0; i < body.Length; i++)
            {
                var (din, dout) = Turn(body, i);
                if (din.Cross(dout) <= 0)
                {
                    continue;
                }

                var at = body[i] + ((din - dout) * 2.5f);
                canvas.DrawPolyline([at - (din * bracketLength), at, at + (dout * bracketLength)], bracket, bracketWidth, antialiased: true);
            }
        }
    }

    /// <summary>
    /// Draws a piece's glyph upright at the visual center of its shape, over a soft glow in the same color,
    /// so L-, T-, S- and U-shapes stay recognizable at every rotation.
    /// </summary>
    public static void DrawPieceIcon(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells, Texture2D? icon, Color tint, int labelLines = 1)
    {
        if (icon is null || cells.Count == 0)
        {
            return;
        }

        // Room for the name under the glyph (NameLines): none, one line, or two.
        var (center, span) = IconAnchor(g, cells);
        float maxSide = labelLines switch { <= 0 => span * 0.70f, 1 => span * 0.56f, _ => span * 0.46f };
        float iconSize = Mathf.Clamp(maxSide, 14f, g.CellSize * 1.15f);
        var drawCenter = labelLines > 0 && g.CellSize >= 46f
            ? center + new Vector2(0, -g.CellSize * (labelLines > 1 ? 0.2f : 0.11f))
            : center;

        if (ArtLibrary.Glow is { } glow)
        {
            float glowSize = iconSize * 1.7f;
            canvas.DrawTextureRect(glow, new Rect2(drawCenter - new Vector2(glowSize / 2f, glowSize / 2f), new Vector2(glowSize, glowSize)), tile: false, modulate: tint with { A = tint.A * 0.32f });
        }

        var rect = new Rect2(drawCenter - new Vector2(iconSize / 2f, iconSize / 2f), new Vector2(iconSize, iconSize));
        canvas.DrawTextureRect(icon, rect, tile: false, modulate: tint);
    }

    /// <summary>Draws the outer edge of a piece, with a soft glow around it: selection, targets, refusals.</summary>
    public static void DrawOutline(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells, Color color, float width)
    {
        foreach (var loop in OutlineLoops(cells.ToHashSet()))
        {
            var edge = Closed(Inset(loop, g, 0f));
            canvas.DrawPolyline(edge, color with { A = color.A * 0.22f }, width * 2.4f, antialiased: true);
            canvas.DrawPolyline(edge, color, Mathf.Max(2f, width * 0.55f), antialiased: true);
        }
    }

    /// <summary>
    /// Writes the name in tracked capitals along the piece's bottom row, under the glyph. See
    /// <see cref="LayOutName"/> for how it fits.
    /// </summary>
    public static void DrawLabel(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells, string text, Color color)
    {
        var name = LayOutName(g, cells, text);

        // Capitals only, so two lines can sit closer than the font's own line height.
        float step = name.FontSize * 1.15f;
        for (int i = 0; i < name.Lines.Length; i++)
        {
            var baseline = name.Baseline - new Vector2(0, step * (name.Lines.Length - 1 - i));
            canvas.DrawString(BloomTheme.Hud, baseline, name.Lines[i], HorizontalAlignment.Center, name.Width, name.FontSize, color);
        }
    }

    /// <summary>How many lines a piece's name takes on the grid, so <see cref="DrawPieceIcon"/> can make room.</summary>
    public static int NameLines(GridLayout g, IReadOnlyCollection<Cell> cells, string text) => LayOutName(g, cells, text).Lines.Length;

    /// <summary>
    /// Fits a piece's name to the run of cells at the left of its bottom row. It takes one line at 19 px when
    /// the cells are full size, shrinking to 16 px at most. A name still too long splits at the space nearest
    /// its middle, onto two lines of at most 18 px.
    /// </summary>
    private static NameLayout LayOutName(GridLayout g, IReadOnlyCollection<Cell> cells, string text)
    {
        var font = BloomTheme.Hud;
        text = text.ToUpperInvariant();
        var set = cells.ToHashSet();
        int maxY = cells.Max(c => c.Y);
        var anchorRow = Cell.RowMajor(cells.Where(c => c.Y == maxY)).First();
        int width = 1;
        while (set.Contains(new Cell(anchorRow.X + width, anchorRow.Y)))
        {
            width++;
        }

        float maxWidth = (width * g.CellSize) - 10f;
        var rect = g.CellRect(anchorRow);
        var baseline = new Vector2(rect.Position.X + 5f, rect.End.Y - Mathf.Max(7f, g.CellSize * 0.11f));
        bool Fits(string line, int size) => font.GetStringSize(line, HorizontalAlignment.Left, -1, size).X <= maxWidth;

        int largest = (int)Mathf.Clamp(g.CellSize * 0.17f, 10, 19);
        int smallest = Math.Min(largest, 16);
        for (int size = largest; size >= smallest; size--)
        {
            if (Fits(text, size))
            {
                return new NameLayout([text], size, baseline, maxWidth);
            }
        }

        int split = SplitNearMiddle(text);
        string[] lines = split < 0 ? [text] : [text[..split], text[(split + 1)..]];
        int lineSize = Math.Min(largest, 18);
        while (lineSize > 10 && !lines.All(line => Fits(line, lineSize)))
        {
            lineSize--;
        }

        return new NameLayout(lines, lineSize, baseline, maxWidth);
    }

    /// <summary>The index of the space nearest the middle of the text, or -1 if it has none.</summary>
    private static int SplitNearMiddle(string text)
    {
        int best = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ' && (best < 0 || Math.Abs(i - (text.Length / 2)) < Math.Abs(best - (text.Length / 2))))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>A piece's name laid out on the grid: its lines, their font size, the last line's baseline and the width they're centered in.</summary>
    private readonly record struct NameLayout(string[] Lines, int FontSize, Vector2 Baseline, float Width);

    /// <summary>"OFFLINE" in the top-left corner of a disabled piece's first cell; the name stays readable for repair picks.</summary>
    public static void DrawOffTag(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells)
    {
        var font = BloomTheme.Hud;
        int fontSize = (int)Mathf.Clamp(g.CellSize * 0.17f, 10, 19);
        var corner = g.CellRect(Cell.RowMajor(cells).First());
        var textSize = font.GetStringSize("OFFLINE", HorizontalAlignment.Left, -1, fontSize);
        var tag = new Rect2(corner.Position + new Vector2(7, 7), textSize + new Vector2(8, 2));
        canvas.DrawRect(tag, Palette.SurfaceDeep with { A = 0.75f });
        canvas.DrawLine(tag.Position, new Vector2(tag.Position.X, tag.End.Y), Palette.Refused, 2f);
        canvas.DrawString(font, tag.Position + new Vector2(5, 1 + font.GetAscent(fontSize)), "OFFLINE", HorizontalAlignment.Left, -1, fontSize, Palette.Refused);
    }

    /// <summary>The "+" on a piece that receives an adjacency bonus, bracketed in the top-right of its first cell.</summary>
    public static void DrawBonusBadge(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> cells, Color color)
    {
        var corner = g.CellRect(Cell.RowMajor(cells).First());
        float h = Mathf.Clamp(g.CellSize * 0.2f, 14f, 22f);
        var box = new Rect2(new Vector2(corner.End.X - 7 - h, corner.Position.Y + 7), new Vector2(h, h));
        canvas.DrawRect(box, Palette.SurfaceDeep with { A = 0.7f });
        DrawBrackets(canvas, box, h * 0.3f, color, 1.5f);
        var mid = box.GetCenter();
        float arm = h * 0.24f;
        canvas.DrawLine(mid - new Vector2(arm, 0), mid + new Vector2(arm, 0), color, 2f);
        canvas.DrawLine(mid - new Vector2(0, arm), mid + new Vector2(0, arm), color, 2f);
    }

    /// <summary>
    /// A short dotted arrow of light bridging the seam where a piece that gives a bonus touches the piece that
    /// gets it, at the middle of their shared edge, over a soft glow. It stays close to the seam, clear of the
    /// names along the bottom of each piece. Draws nothing if the two don't touch.
    /// </summary>
    public static void DrawBonusLink(CanvasItem canvas, GridLayout g, IReadOnlyCollection<Cell> giver, IReadOnlyCollection<Cell> receiver, Color color)
    {
        var to = receiver.ToHashSet();
        var shared = Cell.RowMajor(giver)
            .SelectMany(cell => Sides.Where(s => to.Contains(new Cell(cell.X + s.X, cell.Y + s.Y))).Select(s => (Cell: cell, Side: s)))
            .ToList();
        if (shared.Count == 0)
        {
            return;
        }

        var (from, side) = shared[shared.Count / 2];
        float c = g.CellSize;
        var dir = new Vector2(side.X, side.Y);
        var across = new Vector2(-dir.Y, dir.X);

        // Below the glyph across a side-by-side seam; across a stacked seam, the middle is clear of the glyphs.
        var offset = side.X != 0 ? new Vector2(0, c * 0.14f) : Vector2.Zero;
        var seam = g.CellRect(from).GetCenter() + (dir * (c / 2f)) + offset;
        var start = seam - (dir * (c * 0.1f));
        var tip = seam + (dir * (c * 0.07f));

        if (ArtLibrary.Glow is { } glow)
        {
            float glowSize = c * 0.55f;
            canvas.DrawTextureRect(glow, new Rect2(seam - new Vector2(glowSize / 2f, glowSize / 2f), new Vector2(glowSize, glowSize)), tile: false, modulate: color with { A = 0.35f });
        }

        canvas.DrawCircle(start, 3f, Palette.SurfaceDeep);
        canvas.DrawArc(start, 3f, 0, Mathf.Tau, 16, color, 1.4f, antialiased: true);
        float length = (tip - start).Length();
        for (float d = 6f; d < length - 2f; d += 4.5f)
        {
            canvas.DrawCircle(start + (dir * d), 1.5f, color);
        }

        canvas.DrawColoredPolygon([tip + (dir * 6f), tip + (across * 4.5f), tip - (across * 4.5f)], color);
    }

    /// <summary>
    /// Dashed seams where a piece's cells meet. The ones that cross into the next lane are the stronger, so a
    /// piece shows which lanes it sits in. They stop short of the name along the piece's bottom row, and
    /// break around the glyph.
    /// </summary>
    private static void DrawSeams(CanvasItem canvas, GridLayout g, HashSet<Cell> set, Color tint, float alpha, float inset)
    {
        float c = g.CellSize;
        int labelRow = set.Max(cell => cell.Y);

        // One circle that covers the glyph wherever DrawPieceIcon puts it, with or without room for a label.
        var (anchor, span) = IconAnchor(g, set);
        var glyph = anchor - new Vector2(0, c * 0.05f);
        float clear = span * 0.4f;
        var lane = tint with { A = alpha };
        var row = tint with { A = alpha * 0.45f };
        foreach (var cell in set)
        {
            var r = g.CellRect(cell);
            if (set.Contains(new Cell(cell.X + 1, cell.Y)))
            {
                float top = set.Contains(new Cell(cell.X, cell.Y - 1)) && set.Contains(new Cell(cell.X + 1, cell.Y - 1)) ? r.Position.Y : r.Position.Y + inset + 4f;
                float bottom = cell.Y == labelRow
                    ? r.End.Y - (c * 0.3f)
                    : set.Contains(new Cell(cell.X, cell.Y + 1)) && set.Contains(new Cell(cell.X + 1, cell.Y + 1))
                        ? r.End.Y
                        : r.End.Y - inset - 4f;
                DrawDashedAround(canvas, new Vector2(r.End.X, top), new Vector2(r.End.X, bottom), glyph, clear, lane, 1.2f);
            }

            if (set.Contains(new Cell(cell.X, cell.Y + 1)))
            {
                DrawDashedAround(canvas, new Vector2(r.Position.X + inset + 4f, r.End.Y), new Vector2(r.End.X - inset - 4f, r.End.Y), glyph, clear, row, 1f);
            }
        }
    }

    /// <summary>A dashed horizontal or vertical line, left out where it passes within <paramref name="radius"/> of <paramref name="center"/>.</summary>
    private static void DrawDashedAround(CanvasItem canvas, Vector2 from, Vector2 to, Vector2 center, float radius, Color color, float width)
    {
        bool vertical = Mathf.IsEqualApprox(from.X, to.X);
        float across = vertical ? Mathf.Abs(from.X - center.X) : Mathf.Abs(from.Y - center.Y);
        if (across >= radius)
        {
            canvas.DrawDashedLine(from, to, color, width, 4f);
            return;
        }

        float half = Mathf.Sqrt((radius * radius) - (across * across));
        var axis = vertical ? Vector2.Down : Vector2.Right;
        float start = vertical ? from.Y : from.X;
        float end = vertical ? to.Y : to.X;
        float mid = vertical ? center.Y : center.X;
        if (mid - half > start)
        {
            canvas.DrawDashedLine(from, from + (axis * (Mathf.Min(end, mid - half) - start)), color, width, 4f);
        }

        if (mid + half < end)
        {
            canvas.DrawDashedLine(from + (axis * (Mathf.Max(start, mid + half) - start)), to, color, width, 4f);
        }
    }

    /// <summary>The visual center for a piece's glyph: solid rectangles center on the block, other shapes on their hub cell.</summary>
    internal static (Vector2 Center, float Span) IconAnchor(GridLayout g, IReadOnlyCollection<Cell> cells)
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
            .ThenBy(c => c.Y)
            .ThenBy(c => c.X)
            .First();
        return (g.CellRect(hub).GetCenter(), g.CellSize);
    }

    private static int NeighborCount(HashSet<Cell> set, Cell c) =>
        Sides.Count(s => set.Contains(new Cell(c.X + s.X, c.Y + s.Y)));

    private static int LaneMarkSize(GridLayout g) => (int)Mathf.Clamp(g.CellSize * 0.17f, 12, 20);

    /// <summary>
    /// The outline of a set of cells: one loop of grid corners per separate part, running clockwise on
    /// screen (the inside is on the right), with no points in the middle of straight edges. Where two
    /// cells touch only at a corner, the loop turns right there, so each part gets its own loop.
    /// </summary>
    private static List<List<Vector2I>> OutlineLoops(HashSet<Cell> set)
    {
        var edges = new Dictionary<Vector2I, List<Vector2I>>();
        void Add(Vector2I from, Vector2I to)
        {
            if (!edges.TryGetValue(from, out var list))
            {
                edges[from] = list = [];
            }

            list.Add(to);
        }

        foreach (var c in Cell.RowMajor(set))
        {
            if (!set.Contains(new Cell(c.X, c.Y - 1)))
            {
                Add(new Vector2I(c.X, c.Y), new Vector2I(c.X + 1, c.Y));
            }

            if (!set.Contains(new Cell(c.X + 1, c.Y)))
            {
                Add(new Vector2I(c.X + 1, c.Y), new Vector2I(c.X + 1, c.Y + 1));
            }

            if (!set.Contains(new Cell(c.X, c.Y + 1)))
            {
                Add(new Vector2I(c.X + 1, c.Y + 1), new Vector2I(c.X, c.Y + 1));
            }

            if (!set.Contains(new Cell(c.X - 1, c.Y)))
            {
                Add(new Vector2I(c.X, c.Y + 1), new Vector2I(c.X, c.Y));
            }
        }

        var loops = new List<List<Vector2I>>();
        while (edges.Count > 0)
        {
            var start = edges.Keys.OrderBy(p => p.Y).ThenBy(p => p.X).First();
            var corners = new List<Vector2I>();
            var at = start;
            Vector2I? cameFrom = null;
            do
            {
                corners.Add(at);
                var outgoing = edges[at];
                var next = outgoing[0];
                if (outgoing.Count > 1 && cameFrom is { } prev)
                {
                    var din = at - prev;
                    next = outgoing.FirstOrDefault(o => Cross(din, o - at) > 0, outgoing[0]);
                }

                outgoing.Remove(next);
                if (outgoing.Count == 0)
                {
                    edges.Remove(at);
                }

                cameFrom = at;
                at = next;
            }
            while (at != start && edges.ContainsKey(at));

            loops.Add(DropStraightPoints(corners));
        }

        return loops;
    }

    private static List<Vector2I> DropStraightPoints(List<Vector2I> loop)
    {
        int n = loop.Count;
        var kept = new List<Vector2I>(n);
        for (int i = 0; i < n; i++)
        {
            var a = loop[(i - 1 + n) % n];
            var b = loop[i];
            var c = loop[(i + 1) % n];
            if (Cross(b - a, c - b) != 0)
            {
                kept.Add(b);
            }
        }

        return kept;
    }

    private static int Cross(Vector2I a, Vector2I b) => (a.X * b.Y) - (a.Y * b.X);

    /// <summary>An outline loop on screen, moved inward by <paramref name="inset"/> (outward when negative).</summary>
    private static Vector2[] Inset(List<Vector2I> loop, GridLayout g, float inset)
    {
        int n = loop.Count;
        var screen = loop.Select(g.CornerAt).ToArray();
        var result = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            var (din, dout) = Turn(screen, i);
            var inward = new Vector2(-din.Y, din.X) + new Vector2(-dout.Y, dout.X);
            result[i] = screen[i] + (inward * inset);
        }

        return result;
    }

    /// <summary>The directions into and out of point <paramref name="i"/> of a closed loop.</summary>
    private static (Vector2 In, Vector2 Out) Turn(Vector2[] loop, int i)
    {
        int n = loop.Length;
        var din = (loop[i] - loop[(i - 1 + n) % n]).Normalized();
        var dout = (loop[(i + 1) % n] - loop[i]).Normalized();
        return (din, dout);
    }

    private static Vector2[] Closed(Vector2[] loop) => [.. loop, loop[0]];

    private static Vector2[] Corners(Rect2 r) => [r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y)];

    private static void DrawDashedLoop(CanvasItem canvas, Vector2[] loop, Color color, float width, float dash)
    {
        for (int i = 0; i < loop.Length; i++)
        {
            canvas.DrawDashedLine(loop[i], loop[(i + 1) % loop.Length], color, width, dash);
        }
    }

    /// <summary>L-shaped brackets on the four corners of a rectangle.</summary>
    private static void DrawBrackets(CanvasItem canvas, Rect2 r, float length, Color color, float width)
    {
        var tl = r.Position;
        var br = r.End;
        var tr = new Vector2(br.X, tl.Y);
        var bl = new Vector2(tl.X, br.Y);
        canvas.DrawPolyline([tl + new Vector2(0, length), tl, tl + new Vector2(length, 0)], color, width, antialiased: true);
        canvas.DrawPolyline([tr - new Vector2(length, 0), tr, tr + new Vector2(0, length)], color, width, antialiased: true);
        canvas.DrawPolyline([br - new Vector2(0, length), br, br - new Vector2(length, 0)], color, width, antialiased: true);
        canvas.DrawPolyline([bl + new Vector2(length, 0), bl, bl - new Vector2(0, length)], color, width, antialiased: true);
    }
}
