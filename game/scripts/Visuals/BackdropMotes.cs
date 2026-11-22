using Bloom.Game.Screens;
using Godot;

namespace Bloom.Game.Visuals;

/// <summary>
/// The backdrop's moving layer: pollen and spore motes drifting upward with a soft glow, and a radar
/// sweep turning along the dial ring. It redraws every frame, so in low-processor mode (which saves
/// battery on phones) it draws once and stands still, and the engine can idle.
/// </summary>
public partial class BackdropMotes : Control
{
    private const int MoteCount = 30;
    private const float SweepSpeed = 0.12f;
    private const float SweepLength = 0.7f;

    private readonly Mote[] _motes = new Mote[MoteCount];
    private float _elapsed;

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
        _elapsed = (_elapsed + (float)delta) % 3600f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        DrawSweep(size);
        var glow = ArtLibrary.Glow;
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
            var color = mote.Warm ? Palette.Energy : Palette.GardenGlow;
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
        const int pieces = 6;
        for (int i = 0; i < pieces; i++)
        {
            float from = head - (SweepLength * (i + 1) / pieces);
            float to = head - (SweepLength * i / pieces);
            float alpha = 0.22f * (1f - (i / (float)pieces));
            DrawArc(center, radius, from, to, 8, Palette.HoloLine with { A = alpha }, 2f, antialiased: true);
        }
    }

    private readonly record struct Mote(float U, float V, float Radius, float Speed, float Phase, bool Warm);
}
