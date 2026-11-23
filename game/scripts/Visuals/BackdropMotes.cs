using Bloom.Game.Screens;
using Godot;

namespace Bloom.Game.Visuals;

/// <summary>
/// The backdrop's moving layer: pollen and spore motes drifting upward with a soft glow, and a radar
/// sweep turning along the dial ring. It redraws every frame, so in low-processor mode (which saves
/// battery on phones) it draws once and stands still, and the engine can idle.
/// </summary>
/// <remarks>
/// It also shows the <see cref="HullWarning"/>: as the hull wears, the horizon warms, the screen's edges glow
/// and breathe in the warning's color, and the sweep and the green motes warm with them. All of it is drawn
/// behind the screens, so it never covers text.
/// </remarks>
public partial class BackdropMotes : Control
{
    private const int MoteCount = 30;
    private const float SweepSpeed = 0.12f;
    private const float SweepLength = 0.7f;

    /// <summary>
    /// The edges' glow at full warning, in the corners, where it is strongest: between breaths, and how much a
    /// breath adds to it. The middle of each edge gets about half of it.
    /// </summary>
    private const float EdgeAlpha = 0.09f;
    private const float BreathAlpha = 0.05f;

    /// <summary>The warm glow on the horizon at full warning, laid over the backdrop's green one.</summary>
    private const float HorizonAlpha = 0.035f;

    /// <summary>Seconds per breath: slow as the warning starts, a little quicker at full strength.</summary>
    private const float BreathSlow = 5.5f;
    private const float BreathQuick = 3.4f;

    private readonly Mote[] _motes = new Mote[MoteCount];
    private readonly GradientTexture2D _vignette = Vignette();
    private float _elapsed;
    private float _warning;
    private float _warningTarget;
    private bool _warningSet;
    private float _breath;

    /// <summary>
    /// The hull warning to show, from 0 to 1 (<see cref="HullWarning.Level(int, int)"/>). The first value shows at
    /// once; later ones ease in over <see cref="HullWarning.EaseTime"/>.
    /// </summary>
    public float Warning
    {
        get => _warningTarget;
        set
        {
            _warningTarget = Mathf.Clamp(value, 0f, 1f);

            // Without processing (low-processor mode) nothing eases, so the new level shows at once.
            if (!_warningSet || !IsProcessing())
            {
                _warningSet = true;
                _warning = _warningTarget;
                QueueRedraw();
            }
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
        SetProcess(!OS.LowProcessorUsageMode);
        for (int i = 0; i < MoteCount; i++)
        {
            // A golden-ratio scatter, so the motes look evenly spread from the first frame.
            float u = ((i * 0.61803399f) + 0.13f) % 1f;
            float v = ((i * 0.38196601f) + 0.29f) % 1f;
            float radius = 1.2f + ((i % 4) * 0.7f);
            float speed = 0.006f + ((i % 5) * 0.0025f);
            _motes[i] = new Mote(u, v, radius, speed, i * 1.17f, i % 3 != 0);
        }
    }

    public override void _Process(double delta)
    {
        // Wrap so the float stays precise however long the game stays open.
        float dt = (float)delta;
        _elapsed = (_elapsed + dt) % 3600f;
        _warning = Mathf.MoveToward(_warning, _warningTarget, dt / HullWarning.EaseTime);

        // A phase rather than a product of time and period, so that the period can change without a jump.
        _breath = (_breath + (dt / Mathf.Lerp(BreathSlow, BreathQuick, _warning))) % 1f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        if (_warning > 0f)
        {
            DrawWarning(size);
        }

        DrawSweep(size);
        var glow = ArtLibrary.Glow;
        var green = Palette.GardenGlow.Lerp(HullWarning.Warm, _warning * 0.6f);
        foreach (var mote in _motes)
        {
            float yFrac = (mote.V - (_elapsed * mote.Speed)) % 1f;
            if (yFrac < 0f)
            {
                yFrac += 1f;
            }

            float sway = Mathf.Sin((_elapsed * 0.7f) + mote.Phase) * 0.018f;
            var pos = new Vector2((mote.U + sway) * size.X, yFrac * size.Y);
            float pulse = 0.45f + (0.3f * Mathf.Sin((_elapsed * 1.4f) + mote.Phase));
            var color = mote.Warm ? Palette.Energy : green;
            if (glow is not null)
            {
                float halo = mote.Radius * 9f;
                DrawTextureRect(glow, new Rect2(pos - new Vector2(halo, halo) / 2f, new Vector2(halo, halo)), tile: false, modulate: color with { A = pulse * 0.22f });
            }

            DrawCircle(pos, mote.Radius, color with { A = pulse * 0.7f });
        }
    }

    /// <summary>A bright arc on the dial ring with a fading tail, like a radar sweep.</summary>
    private void DrawSweep(Vector2 size)
    {
        var center = BloomBackdrop.RingCenter(size);
        float radius = BloomBackdrop.RingScale(size) * 0.56f;
        float head = (_elapsed * SweepSpeed) - (Mathf.Pi / 2f);
        var color = Palette.HoloLine.Lerp(HullWarning.Warm, _warning * 0.7f);
        const int pieces = 6;
        for (int i = 0; i < pieces; i++)
        {
            float from = head - (SweepLength * (i + 1) / pieces);
            float to = head - (SweepLength * i / pieces);
            float alpha = 0.22f * (1f - (i / (float)pieces));
            DrawArc(center, radius, from, to, 8, color with { A = alpha }, 2f, antialiased: true);
        }
    }

    /// <summary>
    /// The hull warning: a faint warm glow on the horizon, over the backdrop's green one, and a soft glow in from
    /// the screen's edges that swells and fades with each slow breath. The breath grows with the warning, so it
    /// is hardly there while the warning starts.
    /// </summary>
    private void DrawWarning(Vector2 size)
    {
        var tint = HullWarning.Tint(_warning);
        if (ArtLibrary.Glow is { } glow)
        {
            float horizon = size.Y * 0.78f;
            var rect = new Rect2(new Vector2(size.X * -0.45f, horizon - (size.Y * 0.3f)), new Vector2(size.X * 1.9f, size.Y * 0.6f));
            DrawTextureRect(glow, rect, tile: false, modulate: tint with { A = HorizonAlpha * _warning });
        }

        float breath = 0.5f - (0.5f * Mathf.Cos(_breath * Mathf.Tau));
        float alpha = _warning * (EdgeAlpha + (BreathAlpha * _warning * breath));
        DrawTextureRect(_vignette, new Rect2(Vector2.Zero, size), tile: false, modulate: tint with { A = alpha });
    }

    /// <summary>
    /// A white glow that is clear in the middle and rises smoothly toward the edges, strongest in the corners.
    /// Stretched over the screen, it fades in as an oval, with no line where it starts.
    /// </summary>
    private static GradientTexture2D Vignette()
    {
        var gradient = new Gradient
        {
            Offsets = [0f, 0.4f, 0.7f, 1f],
            Colors = [Colors.White with { A = 0f }, Colors.White with { A = 0f }, Colors.White with { A = 0.4f }, Colors.White],
        };

        // The radius reaches the corners: the middle of each edge is at 0.71 of it.
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 128,
            Height = 128,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f + 0.7071f, 0.5f),
        };
    }

    private readonly record struct Mote(float U, float V, float Radius, float Speed, float Phase, bool Warm);
}
