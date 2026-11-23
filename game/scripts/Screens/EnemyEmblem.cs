using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// The enemy, drawn large inside a target-lock reticle: a glow, the enemy's icon, corner brackets, and two
/// dashed rings that turn in opposite directions. The rings stand still in low-processor mode, and when
/// <see cref="Active"/> is off (the fight is over). While the player aims at one of a boss's parts instead
/// (<see cref="Aimed"/> off), the lock and the brackets are gone and only the outer ring is left.
/// </summary>
/// <remarks>
/// Combat feedback can <see cref="Kick"/> it (a recoil that springs back), <see cref="Flash"/> it, and hold
/// back its next tint change (<see cref="DelayTintChange"/>). Those play in low-processor mode too, since they end.
/// </remarks>
public partial class EnemyEmblem : Control
{
    private const float OuterTurnSpeed = 0.22f;
    private const float InnerTurnSpeed = -0.45f;
    private const int OuterDashes = 28;
    private const float KickLife = 0.5f;
    private const float TintFade = 0.3f;

    private Texture2D? _icon;
    private Color _tint = Palette.Threat;
    private bool _active;
    private bool _aimed = true;
    private float _elapsed;

    private Vector2 _kick;
    private float _kickAge = KickLife;
    private Color _flashColor = Colors.White;
    private float _flashAge;
    private float _flashLife;
    private bool _tintChanging;
    private Color _fromTint;
    private float _tintDelay;
    private float _tintFadeAge;

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
            UpdateProcessing();
            QueueRedraw();
        }
    }

    /// <summary>Whether the player's attacks hit the body: true unless a boss's part is aimed at.</summary>
    public bool Aimed
    {
        get => _aimed;
        set
        {
            if (_aimed != value)
            {
                _aimed = value;
                QueueRedraw();
            }
        }
    }

    private bool Turning => _active && !OS.LowProcessorUsageMode;

    private bool Animating => _kickAge < KickLife || _flashAge < _flashLife || _tintChanging;

    /// <summary>A recoil of <paramref name="pixels"/> toward <paramref name="direction"/> that springs back, with a slight squash.</summary>
    public void Kick(Vector2 direction, float pixels)
    {
        _kick = direction.Normalized() * pixels;
        _kickAge = 0f;
        UpdateProcessing();
    }

    /// <summary>The icon and glow light up in a color and fade back over <paramref name="seconds"/>.</summary>
    public void Flash(Color color, float seconds)
    {
        _flashColor = color;
        _flashAge = 0f;
        _flashLife = seconds;
        UpdateProcessing();
    }

    /// <summary>
    /// Keeps showing the current tint for <paramref name="seconds"/>, then fades to whatever <see cref="Tint"/> is by
    /// then. On a killing blow the emblem turns grey only when it shatters, not when the screen redraws.
    /// </summary>
    public void DelayTintChange(float seconds)
    {
        _fromTint = ShownTint();
        _tintChanging = true;
        _tintDelay = seconds;
        _tintFadeAge = 0f;
        UpdateProcessing();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        UpdateProcessing();
        Resized += QueueRedraw;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Turning)
        {
            // Wrap well past a full turn of both rings so the float stays precise on a long fight.
            _elapsed = (_elapsed + dt) % 1000f;
        }

        _kickAge += dt;
        _flashAge += dt;
        if (_tintChanging)
        {
            if (_tintDelay > 0f)
            {
                _tintDelay -= dt;
            }
            else if ((_tintFadeAge += dt) >= TintFade)
            {
                _tintChanging = false;
            }
        }

        QueueRedraw();
        UpdateProcessing();
    }

    private void UpdateProcessing() => SetProcess(Turning || Animating);

    private Color ShownTint()
    {
        if (!_tintChanging)
        {
            return _tint;
        }

        return _tintDelay > 0f ? _fromTint : _fromTint.Lerp(_tint, Mathf.Clamp(_tintFadeAge / TintFade, 0f, 1f));
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
        var line = ShownTint();

        // The recoil and squash, around the center.
        float flash = _flashAge < _flashLife ? 1f - (_flashAge / _flashLife) : 0f;
        if (_kickAge < KickLife)
        {
            float spring = Mathf.Exp(-_kickAge * 9f);
            float scale = 1f - (0.06f * Mathf.Exp(-_kickAge * 14f));
            DrawSetTransform((_kick * spring * Mathf.Cos(_kickAge * 26f)) + (c * (1f - scale)), 0f, new Vector2(scale, scale));
        }

        if (ArtLibrary.Glow is { } glow)
        {
            float g = side * 1.25f;
            var glowColor = line.Lerp(_flashColor, flash * 0.6f) with { A = 0.26f + (0.4f * flash) };
            DrawTextureRect(glow, new Rect2(c - new Vector2(g, g) / 2f, new Vector2(g, g)), tile: false, modulate: glowColor);
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
        for (int i = 0; _aimed && i < 2; i++)
        {
            float a = spin + (i * Mathf.Pi) + 0.35f;
            DrawArc(c, inner, a, a + 0.9f, 16, line with { A = 0.85f }, 2.5f, antialiased: true);
        }

        // Corner brackets on the bounding square, like the ship grid's.
        float half = r * 0.98f;
        float arm = r * 0.22f;
        foreach (var (sx, sy) in _aimed ? new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) } : [])
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
            DrawTextureRect(icon, new Rect2(c - new Vector2(s, s) / 2f, new Vector2(s, s)), tile: false, modulate: Palette.Glyph(line).Lerp(_flashColor, flash * 0.8f));
        }
    }
}
