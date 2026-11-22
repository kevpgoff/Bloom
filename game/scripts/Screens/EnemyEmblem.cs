using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// The enemy, drawn large inside a target-lock reticle: a glow, the enemy's icon, corner brackets, and two
/// dashed rings that turn in opposite directions. The rings stand still in low-processor mode, and when
/// <see cref="Active"/> is off (the fight is over).
/// </summary>
public partial class EnemyEmblem : Control
{
    private const float OuterTurnSpeed = 0.22f;
    private const float InnerTurnSpeed = -0.45f;
    private const int OuterDashes = 28;

    private Texture2D? _icon;
    private Color _tint = Palette.Threat;
    private bool _active;
    private float _elapsed;

    public Texture2D? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            QueueRedraw();
        }
    }

    public Color Tint
    {
        get => _tint;
        set
        {
            _tint = value;
            QueueRedraw();
        }
    }

    /// <summary>Whether the reticle turns. Off after the fight, and always off in low-processor mode.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            SetProcess(value && !OS.LowProcessorUsageMode);
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(_active && !OS.LowProcessorUsageMode);
        Resized += QueueRedraw;
    }

    public override void _Process(double delta)
    {
        // Wrap well past a full turn of both rings so the float stays precise on a long fight.
        _elapsed = (_elapsed + (float)delta) % 1000f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float side = Mathf.Min(Size.X, Size.Y);
        if (side <= 0)
        {
            return;
        }

        var c = Size / 2f;
        float r = side / 2f;
        var line = _tint;

        if (ArtLibrary.Glow is { } glow)
        {
            float g = side * 1.25f;
            DrawTextureRect(glow, new Rect2(c - new Vector2(g, g) / 2f, new Vector2(g, g)), tile: false, modulate: line with { A = 0.26f });
        }

        // A dark disc behind the icon, so the backdrop's lines don't run through the enemy.
        DrawCircle(c, r * 0.8f, Palette.SurfaceDeep with { A = 0.55f });
        DrawArc(c, r * 0.8f, 0f, Mathf.Tau, 72, line with { A = 0.22f }, 1f, antialiased: true);

        // The outer ring: short dashes that turn slowly, crossed by four fixed ticks.
        float outer = r * 0.93f;
        float turn = _elapsed * OuterTurnSpeed;
        float step = Mathf.Tau / OuterDashes;
        for (int i = 0; i < OuterDashes; i++)
        {
            float a = turn + (i * step);
            DrawArc(c, outer, a, a + (step * 0.55f), 6, line with { A = 0.5f }, 1.5f, antialiased: true);
        }

        for (int i = 0; i < 4; i++)
        {
            var dir = Vector2.FromAngle(i * Mathf.Pi / 2f);
            DrawLine(c + (dir * r * 0.84f), c + (dir * r * 1.0f), line with { A = 0.9f }, 2f, antialiased: true);
        }

        // The inner lock: two arcs that turn the other way.
        float inner = r * 0.86f;
        float spin = _elapsed * InnerTurnSpeed;
        for (int i = 0; i < 2; i++)
        {
            float a = spin + (i * Mathf.Pi) + 0.35f;
            DrawArc(c, inner, a, a + 0.9f, 16, line with { A = 0.85f }, 2.5f, antialiased: true);
        }

        // Corner brackets on the bounding square, like the ship grid's.
        float half = r * 0.98f;
        float arm = r * 0.22f;
        foreach (var (sx, sy) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            var corner = c + new Vector2(sx * half, sy * half);
            DrawPolyline(
                [corner - new Vector2(0, sy * arm), corner, corner - new Vector2(sx * arm, 0)],
                line with { A = 0.95f },
                2.5f,
                antialiased: true);
        }

        if (_icon is { } icon)
        {
            float s = side * 0.62f;
            DrawTextureRect(icon, new Rect2(c - new Vector2(s, s) / 2f, new Vector2(s, s)), tile: false, modulate: Palette.Glyph(line));
        }
    }
}
