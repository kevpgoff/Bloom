using Bloom.Game.Screens;
using Godot;

namespace Bloom.Game.Visuals;

/// <summary>
/// Atmospheric Option A backdrop: deep botanical-space horizon gradient, faint Art Nouveau
/// astrolabe rings, and gently drifting golden spore/pollen motes. Purely cosmetic.
/// </summary>
public partial class BloomBackdrop : Control
{
    private const int MoteCount = 26;

    private readonly Mote[] _motes = new Mote[MoteCount];
    private float _elapsed;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        for (int i = 0; i < MoteCount; i++)
        {
            // Deterministic golden-ratio scatter so the starfield looks balanced on first frame.
            float u = ((i * 0.61803399f) + 0.13f) % 1f;
            float v = ((i * 0.38196601f) + 0.29f) % 1f;
            float radius = 1.4f + ((i % 4) * 0.85f);
            float speed = 0.006f + ((i % 5) * 0.0025f);
            float phase = i * 1.17f;
            bool warm = i % 3 != 0;
            _motes[i] = new Mote(u, v, radius, speed, phase, warm);
        }
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        // Vertical sky/space gradient: deep teal-indigo above warming into soft mossy horizon below.
        const int bands = 24;
        float bandHeight = size.Y / bands;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)(bands - 1);
            var color = Palette.BackdropTop.Lerp(Palette.BackdropMid, Mathf.Clamp(t * 1.3f, 0f, 1f));
            if (t > 0.65f)
            {
                color = color.Lerp(Palette.BackdropGlow, (t - 0.65f) / 0.35f * 0.35f);
            }

            DrawRect(new Rect2(0, i * bandHeight, size.X, bandHeight + 1f), color);
        }

        // Subtle Art Nouveau celestial astrolabe arcs behind the ship grid.
        var center = new Vector2(size.X * 0.5f, size.Y * 0.42f);
        float maxR = Mathf.Max(size.X, size.Y) * 0.68f;
        DrawArc(center, maxR * 0.36f, 0f, Mathf.Tau, 72, Palette.BrassLine with { A = 0.11f }, 1.5f, antialiased: true);
        DrawArc(center, maxR * 0.56f, 0f, Mathf.Tau, 84, Palette.BrassLine with { A = 0.08f }, 1.2f, antialiased: true);
        DrawArc(center, maxR * 0.78f, 0f, Mathf.Tau, 96, Palette.GardenGlow with { A = 0.07f }, 1.2f, antialiased: true);

        // Diagonal harmonic meridian lines.
        DrawLine(new Vector2(center.X, 0), new Vector2(center.X, size.Y), Palette.BrassLine with { A = 0.05f }, 1f);
        DrawLine(new Vector2(0, center.Y), new Vector2(size.X, center.Y), Palette.BrassLine with { A = 0.05f }, 1f);

        // Gently drifting golden pollen & bioluminescent spore motes.
        foreach (var mote in _motes)
        {
            float yFrac = (mote.V - (_elapsed * mote.Speed)) % 1f;
            if (yFrac < 0f)
            {
                yFrac += 1f;
            }

            float sway = Mathf.Sin((_elapsed * 0.7f) + mote.Phase) * 0.018f;
            var pos = new Vector2((mote.U + sway) * size.X, yFrac * size.Y);
            float pulse = 0.35f + (0.35f * Mathf.Sin((_elapsed * 1.4f) + mote.Phase));
            var tint = (mote.Warm ? Palette.Energy : Palette.GardenGlow) with { A = pulse * 0.55f };
            DrawCircle(pos, mote.Radius, tint);
        }
    }

    private readonly record struct Mote(float U, float V, float Radius, float Speed, float Phase, bool Warm);
}
