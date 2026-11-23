using Bloom.Game.Screens;
using Godot;

namespace Bloom.Game.Visuals;

/// <summary>
/// The Holo Grove backdrop behind every screen: a dark teal night with a glow low on the horizon,
/// bioluminescent root veins rising from the bottom edge, dashed HUD rings around the ship, and faint
/// scanlines. Purely cosmetic.
/// </summary>
/// <remarks>
/// This layer is static: Godot keeps what <see cref="_Draw"/> drew and redraws it only on a resize. The
/// moving parts (pollen motes, a radar sweep and the hull warning) are a child <see cref="BackdropMotes"/>,
/// which stands still in low-processor mode.
/// </remarks>
public partial class BloomBackdrop : Control
{
    private const int ScanlineSpacing = 3;
    private const int VeinCount = 7;

    /// <summary>The center of the HUD rings: behind the ship grid on the combat and refit screens.</summary>
    public static Vector2 RingCenter(Vector2 size) => new(size.X * 0.5f, size.Y * 0.42f);

    /// <summary>The radius scale of the HUD rings.</summary>
    public static float RingScale(Vector2 size) => Mathf.Max(size.X, size.Y) * 0.68f;

    private BackdropMotes _motes = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;

        _motes = new BackdropMotes();
        AddChild(_motes);
        _motes.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Shows a hull warning from 0 to 1 (<see cref="HullWarning"/>) on the moving layer, which eases toward it.</summary>
    public void ShowWarning(float level) => _motes.Warning = level;

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        DrawSky(size);
        DrawHorizon(size);
        DrawRings(size);
        DrawVeins(size);
        DrawScanlines(size);
    }

    private void DrawSky(Vector2 size)
    {
        const int bands = 32;
        float bandHeight = size.Y / bands;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)(bands - 1);
            var color = Palette.BackdropTop.Lerp(Palette.BackdropMid, Mathf.Clamp(t * 1.4f, 0f, 1f));
            if (t > 0.6f)
            {
                color = color.Lerp(Palette.BackdropGlow, (t - 0.6f) / 0.4f * 0.45f);
            }

            DrawRect(new Rect2(0, i * bandHeight, size.X, bandHeight + 1f), color);
        }
    }

    /// <summary>A soft green-teal glow low on the screen, as if the grove lit the horizon.</summary>
    private void DrawHorizon(Vector2 size)
    {
        if (ArtLibrary.Glow is not { } glow)
        {
            return;
        }

        float horizon = size.Y * 0.78f;
        DrawTextureRect(glow, CenteredRect(new Vector2(size.X * 0.5f, horizon), new Vector2(size.X * 1.9f, size.Y * 0.6f)), tile: false, modulate: Palette.GardenGlow with { A = 0.07f });
        DrawTextureRect(glow, CenteredRect(new Vector2(size.X * 0.5f, horizon), new Vector2(size.X * 1.3f, size.Y * 0.22f)), tile: false, modulate: Palette.HoloLine with { A = 0.06f });
        DrawLine(new Vector2(0, horizon), new Vector2(size.X, horizon), Palette.HoloLine with { A = 0.05f }, 1f);
    }

    /// <summary>Three HUD rings around the ship: dashed, ticked like a dial, and dotted.</summary>
    private void DrawRings(Vector2 size)
    {
        var center = RingCenter(size);
        float scale = RingScale(size);

        DrawDashedRing(center, scale * 0.36f, 96, 0.5f, Palette.HoloLine with { A = 0.11f }, 1.2f);

        float dial = scale * 0.56f;
        DrawArc(center, dial, 0f, Mathf.Tau, 128, Palette.HoloLine with { A = 0.06f }, 1f, antialiased: true);
        for (int i = 0; i < 72; i++)
        {
            var dir = Vector2.FromAngle(i * Mathf.Tau / 72f);
            float length = i % 6 == 0 ? 14f : 6f;
            DrawLine(center + (dir * dial), center + (dir * (dial - length)), Palette.HoloLine with { A = i % 6 == 0 ? 0.13f : 0.08f }, 1f, antialiased: true);
        }

        float outer = scale * 0.78f;
        var dot = Palette.GardenGlow with { A = 0.1f };
        for (int i = 0; i < 180; i++)
        {
            DrawCircle(center + (Vector2.FromAngle(i * Mathf.Tau / 180f) * outer), 1.2f, dot);
        }

        DrawLine(new Vector2(center.X, 0), new Vector2(center.X, size.Y), Palette.HoloLine with { A = 0.04f }, 1f);
        DrawLine(new Vector2(0, center.Y), new Vector2(size.X, center.Y), Palette.HoloLine with { A = 0.04f }, 1f);
    }

    /// <summary>
    /// Root veins that climb from the bottom edge in straight runs and 45° turns, like the traces on the
    /// deck, branching now and then and ending in glowing nodes. The same every time: a fixed seed.
    /// </summary>
    private void DrawVeins(Vector2 size)
    {
        var rng = new XorShift(0x9E3779B9u);
        float unit = size.X / 18f;
        for (int v = 0; v < VeinCount; v++)
        {
            float x = size.X * ((v + 0.5f) / VeinCount) + ((rng.Next01() - 0.5f) * unit);
            var color = v % 2 == 0 ? Palette.GardenGlow : Palette.HoloLine;
            int steps = 5 + (int)(rng.Next01() * 5);
            Grow(new Vector2(x, size.Y + 4f), steps, unit, color, ref rng, depth: 0);
        }
    }

    private void Grow(Vector2 start, int steps, float unit, Color color, ref XorShift rng, int depth)
    {
        var points = new List<Vector2> { start };
        var at = start;
        for (int i = 0; i < steps; i++)
        {
            float roll = rng.Next01();
            float dx = roll < 0.25f ? -1f : roll < 0.5f ? 1f : 0f;
            float length = unit * (0.8f + (rng.Next01() * 1.2f));
            at += new Vector2(dx * length * 0.7f, -length);
            points.Add(at);

            if (depth == 0 && i > 0 && rng.Next01() < 0.35f)
            {
                Grow(at, 2 + (int)(rng.Next01() * 3), unit * 0.7f, color, ref rng, depth + 1);
                DrawCircle(at, 2f, color with { A = 0.16f });
            }
        }

        var line = points.ToArray();
        DrawPolyline(line, color with { A = 0.035f }, 6f, antialiased: true);
        DrawPolyline(line, color with { A = depth == 0 ? 0.13f : 0.1f }, 1.4f, antialiased: true);
        DrawCircle(at, depth == 0 ? 3f : 2.2f, color with { A = 0.22f });
        DrawCircle(at, depth == 0 ? 7f : 5f, color with { A = 0.04f });
    }

    private void DrawScanlines(Vector2 size)
    {
        var color = Palette.HoloLine with { A = 0.018f };
        for (float y = 0; y < size.Y; y += ScanlineSpacing)
        {
            DrawLine(new Vector2(0, y), new Vector2(size.X, y), color, 1f);
        }
    }

    private void DrawDashedRing(Vector2 center, float radius, int dashes, float duty, Color color, float width)
    {
        float step = Mathf.Tau / dashes;
        for (int i = 0; i < dashes; i++)
        {
            float a = i * step;
            DrawArc(center, radius, a, a + (step * duty), 4, color, width, antialiased: true);
        }
    }

    private static Rect2 CenteredRect(Vector2 center, Vector2 size) => new(center - (size / 2f), size);

    /// <summary>A tiny fixed-seed generator, so the veins come out the same on every run and device.</summary>
    private struct XorShift(uint state)
    {
        private uint _state = state;

        public float Next01()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
