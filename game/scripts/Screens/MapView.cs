using Bloom.Core;
using Bloom.Core.Map;
using Bloom.Core.Run;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Draws the sector map as a hologram: the first row at the bottom and the boss alone at the top, the
/// edges between rows, the path taken so far, and the nodes that can be chosen next, which pulse. Reports
/// taps on nodes through <see cref="NodeTapped"/>. Everything is read from the run; nothing is decided here.
/// </summary>
public partial class MapView : Control
{
    private const float PulsePeriod = 1.6f;

    /// <summary>Room below the first row for the ship's start marker, in rows.</summary>
    private const float StartRoom = 0.7f;

    private RunSession? _session;
    private int? _selected;
    private float _time;
    private HashSet<int> _reachable = [];

    /// <summary>A node was tapped; the argument is its id.</summary>
    public event Action<int>? NodeTapped;

    public void SetState(RunSession session, int? selected)
    {
        _session = session;
        _selected = selected;
        _reachable = session.Map.Reachable(session.State).Select(n => n.Id).ToHashSet();
        SetProcess(_reachable.Count > 0 && !OS.LowProcessorUsageMode);
        QueueRedraw();
    }

    public override void _Ready()
    {
        Resized += QueueRedraw;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        _time = (_time + (float)delta) % PulsePeriod;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click
            || _session?.State.Map is not { } map)
        {
            return;
        }

        // The nearest node within half a row or half a column, so every node has a generous target.
        float reach = Mathf.Min(RowStep, Size.X / Rules.MapColumns) * 0.55f;
        var hit = map.Nodes
            .Select(n => (Node: n, Distance: click.Position.DistanceTo(Center(n))))
            .Where(h => h.Distance <= Mathf.Max(reach, RadiusOf(h.Node)))
            .OrderBy(h => h.Distance)
            .Select(h => (MapNode?)h.Node)
            .FirstOrDefault();
        if (hit is not null)
        {
            NodeTapped?.Invoke(hit.Id);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        if (_session?.State is not { Map: { } map } run)
        {
            return;
        }

        float pulse = 0.5f + (0.5f * Mathf.Sin(_time / PulsePeriod * Mathf.Tau));
        DrawStart(run, map, pulse);
        foreach (var edge in map.Edges)
        {
            DrawEdge(run, map.Node(edge.From), map.Node(edge.To), pulse);
        }

        foreach (var node in map.Nodes)
        {
            DrawNode(run, node, pulse);
        }
    }

    private float RowStep => Size.Y / (Rules.MapRows + 1 + StartRoom);

    private float Radius => Mathf.Min(Size.X / Rules.MapColumns, RowStep) * 0.4f;

    private float RadiusOf(MapNode node) => node.Type == NodeType.Boss ? Radius * 1.35f : Radius;

    private Vector2 Center(MapNode node)
    {
        float y = Size.Y - (RowStep * (node.Row + StartRoom + 0.5f));
        float x = node.Type == NodeType.Boss ? Size.X / 2f : Size.X * (node.Column + 0.5f) / Rules.MapColumns;
        return new Vector2(x, y);
    }

    private Vector2 StartPoint => new(Size.X / 2f, Size.Y - (RowStep * StartRoom * 0.4f));

    private static bool Walked(RunState run, int from, int to)
    {
        for (int i = 0; i + 1 < run.Path.Count; i++)
        {
            if (run.Path[i] == from && run.Path[i + 1] == to)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The ship's marker below the first row, with lines to the first row's nodes until one is chosen.</summary>
    private void DrawStart(RunState run, SectorMap map, float pulse)
    {
        var start = StartPoint;
        foreach (var node in map.StartNodes())
        {
            var end = Center(node) - ((Center(node) - start).Normalized() * RadiusOf(node));
            if (run.Path.Count == 0)
            {
                DrawDashedLine(start, end, Palette.Selectable with { A = 0.45f + (0.4f * pulse) }, 2f, 6f, antialiased: true);
            }
            else if (run.Path[0] == node.Id)
            {
                DrawWalked(start, end);
            }
            else
            {
                DrawLine(start, end, Palette.LineMuted with { A = 0.35f }, 1.2f, antialiased: true);
            }
        }

        float s = Radius * 0.32f;
        var hull = new[] { start + new Vector2(0, -s), start + new Vector2(s * 0.85f, s * 0.6f), start + new Vector2(0, s * 0.25f), start + new Vector2(-s * 0.85f, s * 0.6f) };
        if (ArtLibrary.Glow is { } glow)
        {
            float g = s * 4f;
            DrawTextureRect(glow, new Rect2(start - new Vector2(g / 2f, g / 2f), new Vector2(g, g)), tile: false, modulate: Palette.HoloLine with { A = 0.3f });
        }

        DrawColoredPolygon(hull, Palette.SurfaceDeep);
        DrawPolyline([.. hull, hull[0]], Palette.HoloBright, 2f, antialiased: true);
    }

    private void DrawEdge(RunState run, MapNode from, MapNode to, float pulse)
    {
        var a = Center(from);
        var b = Center(to);
        var dir = (b - a).Normalized();
        a += dir * RadiusOf(from);
        b -= dir * RadiusOf(to);

        if (Walked(run, from.Id, to.Id))
        {
            DrawWalked(a, b);
        }
        else if (run.CurrentNode?.Id == from.Id && _reachable.Contains(to.Id))
        {
            DrawDashedLine(a, b, Palette.Selectable with { A = 0.45f + (0.4f * pulse) }, 2f, 6f, antialiased: true);
        }
        else
        {
            bool past = to.Row <= (run.CurrentNode?.Row ?? -1);
            DrawLine(a, b, past ? Palette.LineMuted with { A = 0.3f } : Palette.HoloLine with { A = 0.3f }, 1.3f, antialiased: true);
        }
    }

    private void DrawWalked(Vector2 a, Vector2 b)
    {
        DrawLine(a, b, Palette.HoloBright with { A = 0.18f }, 7f, antialiased: true);
        DrawLine(a, b, Palette.HoloBright with { A = 0.9f }, 2.4f, antialiased: true);
    }

    private void DrawNode(RunState run, MapNode node, float pulse)
    {
        var center = Center(node);
        float r = RadiusOf(node);
        bool visited = run.Path.Contains(node.Id);
        bool current = run.CurrentNode?.Id == node.Id;
        bool reachable = _reachable.Contains(node.Id);
        bool selected = _selected == node.Id;
        bool passed = !visited && node.Row <= (run.CurrentNode?.Row ?? -1);
        var color = Palette.ForNode(node.Type);
        float alpha = passed ? 0.28f : visited || reachable || selected ? 1f : 0.62f;

        if (ArtLibrary.Glow is { } glow && (reachable || selected || current))
        {
            float g = r * 3.2f;
            var glowColor = reachable ? Palette.Selectable : color;
            float glowAlpha = reachable && !selected ? 0.18f + (0.22f * pulse) : 0.32f;
            DrawTextureRect(glow, new Rect2(center - new Vector2(g / 2f, g / 2f), new Vector2(g, g)), tile: false, modulate: glowColor with { A = glowAlpha });
        }

        DrawCircle(center, r, Palette.SurfaceDeep with { A = 0.94f });
        DrawCircle(center, r, color with { A = 0.07f * alpha });

        if (selected)
        {
            DrawArc(center, r, 0, Mathf.Tau, 64, Palette.Selectable, 3.4f, antialiased: true);
            DrawDashedRing(center, r + 7f, Palette.Selectable with { A = 0.8f });
        }
        else if (reachable)
        {
            DrawArc(center, r, 0, Mathf.Tau, 64, Palette.Selectable with { A = 0.55f + (0.45f * pulse) }, 2.6f, antialiased: true);
        }
        else if (visited)
        {
            DrawArc(center, r, 0, Mathf.Tau, 64, Palette.HoloBright with { A = 0.85f }, 2.2f, antialiased: true);
        }
        else
        {
            DrawArc(center, r, 0, Mathf.Tau, 64, color with { A = 0.5f * alpha }, 1.5f, antialiased: true);
        }

        if (node.Type == NodeType.Boss)
        {
            DrawDashedRing(center, r - 5f, color with { A = 0.5f * alpha });
        }

        float iconSize = r * 1.3f;
        var rect = new Rect2(center - new Vector2(iconSize / 2f, iconSize / 2f), new Vector2(iconSize, iconSize));
        if (ArtLibrary.NodeIcon(node.Type) is { } icon)
        {
            DrawTextureRect(icon, rect, tile: false, modulate: Palette.Glyph(color) with { A = alpha });
        }
        else
        {
            var font = BloomTheme.DisplayBold;
            int size = Mathf.RoundToInt(r * 0.9f);
            string letter = node.Type.ToString()[..1];
            var textSize = font.GetStringSize(letter, fontSize: size);
            DrawString(font, center + new Vector2(-textSize.X / 2f, size * 0.36f), letter, fontSize: size, modulate: color with { A = alpha });
        }

        if (current)
        {
            DrawBrackets(center, r + 9f, Palette.HoloBright);
        }
    }

    private void DrawDashedRing(Vector2 center, float radius, Color color)
    {
        const int dashes = 24;
        for (int i = 0; i < dashes; i++)
        {
            float start = i * Mathf.Tau / dashes;
            DrawArc(center, radius, start, start + (Mathf.Tau / dashes * 0.5f), 4, color, 1.4f, antialiased: true);
        }
    }

    /// <summary>Corner brackets around the node the ship is at, like the target lock on the combat screen.</summary>
    private void DrawBrackets(Vector2 center, float half, Color color)
    {
        float arm = half * 0.4f;
        foreach (var (sx, sy) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var corner = center + new Vector2(sx * half, sy * half);
            DrawPolyline([corner - new Vector2(0, sy * arm), corner, corner - new Vector2(sx * arm, 0)], color, 2f, antialiased: true);
        }
    }
}
