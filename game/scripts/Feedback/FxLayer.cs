using Bloom.Game.Platform;
using Bloom.Game.Screens;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Feedback;

/// <summary>How a particle is drawn.</summary>
public enum ParticleShape
{
    /// <summary>A soft glowing mote.</summary>
    Dot,

    /// <summary>A short bright line along the direction of travel.</summary>
    Streak,

    /// <summary>A small hexagon outline, for shield shards.</summary>
    Hex,

    /// <summary>A small filled diamond that tumbles, for sprouts and spores.</summary>
    Leaf,
}

/// <summary>
/// The combat screen's effects, drawn in code over the screen like the rest of the Holo Grove look: glowing
/// particles, rings, bolts, flashes and ghost cards, blended additively so they read as light, plus floating
/// numbers in Saira with dark outlines on a normally blended child. It also shakes <see cref="ShakeTarget"/>.
/// </summary>
/// <remarks>
/// <para>
/// Positions are in this layer's own coordinates without the shake; use <see cref="FromGlobal(Vector2)"/> to
/// convert a widget's position. The layer draws itself with the current shake, so effects stay on their widgets.
/// </para>
/// <para>
/// Time is the layer's own clock, advanced by the frame's delta, so <c>--fixed-fps</c> recordings play the effects
/// at their real speed. The layer processes only while something plays or is scheduled, so low-processor mode can
/// idle; effects still play in that mode, because each one ends on its own.
/// </para>
/// </remarks>
public partial class FxLayer : Control
{
    private const int MaxParticles = 600;
    private const float MaxShakePixels = 14f;
    private const float TraumaDecayPerSecond = 1.6f;

    /// <summary>The longest step taken in one frame, so a frame after the app comes back from the background doesn't jump.</summary>
    private const float MaxStep = 0.05f;

    /// <summary>Particle sizes are given in design pixels and drawn this much larger, so they read on a phone.</summary>
    private const float ParticleScale = 1.6f;

    private readonly List<Particle> _particles = [];
    private readonly List<Effect> _effects = [];
    private readonly List<FloatingText> _texts = [];
    private readonly List<(float Due, Action Action)> _scheduled = [];
    private readonly RandomNumberGenerator _rng = new();
    private Control _numbers = null!;
    private Texture2D? _glow;
    private float _clock;
    private float? _firingAt;
    private float _trauma;
    private Vector2 _shake;
    private bool _warmedUp;

    /// <summary>The control that the screen shake moves. Its position is set from zero every frame while shaking.</summary>
    public Control? ShakeTarget { get; set; }

    /// <summary>
    /// The time new effects start at: the exact due time while a scheduled action runs (it may run up to a frame
    /// late), so effects scheduled together stay in step at any frame rate.
    /// </summary>
    private float Now => _firingAt ?? _clock;

    private bool Busy => _particles.Count > 0 || _effects.Count > 0 || _texts.Count > 0 || _scheduled.Count > 0 || _trauma > 0f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        _glow = ArtLibrary.Glow;

        // Numbers keep normal blending, so their dark outlines survive.
        _numbers = new Control { Name = "Numbers", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_numbers);
        _numbers.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _numbers.Draw += DrawNumbers;

        SetProcess(Busy);
        QueueRedraw(); // draws the warm-up quad, so the additive shader is ready before the first hit
    }

    /// <summary>A point on screen in this layer's coordinates, without the current shake.</summary>
    public Vector2 FromGlobal(Vector2 global) => global - GlobalPosition - _shake;

    public Rect2 FromGlobal(Rect2 global) => new(FromGlobal(global.Position), global.Size);

    /// <summary>Runs <paramref name="action"/> after <paramref name="delay"/> seconds, unless this screen goes away first.</summary>
    public void Schedule(float delay, Action action)
    {
        _scheduled.Add((Now + Mathf.Max(0f, delay), action));
        Wake();
    }

    /// <summary>Adds trauma (0 to 1). The shake is its square, so small hits barely move the screen and big ones jolt it.</summary>
    public void Shake(float trauma)
    {
        _trauma = Mathf.Min(1f, _trauma + trauma);
        Wake();
    }

    /// <summary>Particles thrown out from a point.</summary>
    public void Burst(
        Vector2 at,
        Color color,
        int count,
        float speed,
        float life,
        float size = 2.5f,
        ParticleShape shape = ParticleShape.Dot,
        float direction = 0f,
        float spread = Mathf.Tau,
        float gravity = 0f,
        float drag = 3f,
        float delay = 0f)
    {
        for (int i = 0; i < count && _particles.Count < MaxParticles; i++)
        {
            float angle = direction + _rng.RandfRange(-spread / 2f, spread / 2f);
            _particles.Add(new Particle
            {
                Age = _clock - Now,
                Pos = at,
                Vel = Vector2.FromAngle(angle) * speed * _rng.RandfRange(0.35f, 1f),
                Life = life * _rng.RandfRange(0.6f, 1f),
                Delay = delay,
                Size = size * _rng.RandfRange(0.7f, 1.3f),
                Shape = shape,
                Color = color,
                Gravity = gravity,
                Drag = drag,
                Rot = _rng.RandfRange(0f, Mathf.Tau),
                Spin = _rng.RandfRange(-6f, 6f),
            });
        }

        Wake();
    }

    /// <summary>Motes that travel from one point to another along gentle curves, leaving one after another.</summary>
    public void Stream(
        Vector2 from,
        Vector2 to,
        Color color,
        int count,
        float duration,
        float size = 2.5f,
        ParticleShape shape = ParticleShape.Dot,
        float arc = 40f,
        float delay = 0f)
    {
        var normal = (to - from).Orthogonal().Normalized();
        for (int i = 0; i < count && _particles.Count < MaxParticles; i++)
        {
            var jitter = new Vector2(_rng.RandfRange(-6f, 6f), _rng.RandfRange(-6f, 6f));
            AddPathParticle(
                from + jitter,
                ((from + to) / 2f) + (normal * _rng.RandfRange(-arc, arc)),
                to,
                color,
                size * _rng.RandfRange(0.7f, 1.3f),
                shape,
                life: duration * 0.6f,
                delay: delay + (duration * 0.4f * i / Math.Max(1, count - 1)));
        }

        Wake();
    }

    /// <summary>Motes that swirl in from a circle to its center.</summary>
    public void Implode(Vector2 at, Color color, int count, float radius, float life, float size = 2.5f, ParticleShape shape = ParticleShape.Dot, float delay = 0f)
    {
        for (int i = 0; i < count && _particles.Count < MaxParticles; i++)
        {
            float angle = (i * Mathf.Tau / count) + _rng.RandfRange(-0.3f, 0.3f);
            AddPathParticle(
                at + (Vector2.FromAngle(angle) * radius * _rng.RandfRange(0.75f, 1.1f)),
                at + (Vector2.FromAngle(angle + 0.9f) * radius * 0.6f),
                at,
                color,
                size,
                shape,
                life * _rng.RandfRange(0.75f, 1f),
                delay);
        }

        Wake();
    }

    /// <summary>A ring (or hexagon) that grows or closes from one radius to another as it fades.</summary>
    public void Ring(Vector2 at, Color color, float fromRadius, float toRadius, float life, float width = 3f, bool hex = false, float delay = 0f) =>
        Add(new RingEffect(at, fromRadius, toRadius, width, hex, _rng.RandfRange(0f, Mathf.Tau)), color, life, delay);

    /// <summary>A rectangle outline that grows outward by <paramref name="grow"/> pixels as it fades.</summary>
    public void RectRing(Rect2 rect, Color color, float grow, float life, float width = 3f, float delay = 0f) =>
        Add(new RectRingEffect(rect, grow, width), color, life, delay);

    /// <summary>
    /// A bolt of light from one point to another, arriving after <paramref name="travel"/> seconds. With
    /// <paramref name="beam"/>, the whole line stays lit for <paramref name="linger"/> seconds after it arrives.
    /// </summary>
    public void Bolt(Vector2 from, Vector2 to, Color color, float travel, float width = 4f, Color? core = null, float linger = 0.08f, bool beam = false, float delay = 0f) =>
        Add(new BoltEffect(from, to, travel, width, core ?? Colors.White, linger, beam), color, travel + linger, delay);

    /// <summary>A fill over the given rectangles that fades, or flickers out when <paramref name="flicker"/> is set.</summary>
    public void Flash(IReadOnlyList<Rect2> rects, Color color, float life, bool flicker = false, float delay = 0f) =>
        Add(new FlashEffect(rects, flicker), color, life, delay);

    /// <summary>A bright scan line that sweeps up over the given rectangles, like a piece being rebuilt.</summary>
    public void Sweep(IReadOnlyList<Rect2> rects, Color color, float life, float delay = 0f) =>
        Add(new SweepEffect(rects), color, life, delay);

    /// <summary>A soft glow that swells and fades.</summary>
    public void Pulse(Vector2 at, Color color, float radius, float life, float delay = 0f) =>
        Add(new PulseEffect(at, radius), color, life, delay);

    /// <summary>
    /// A card outline that pops and fades where it was, or flies to <paramref name="flyTo"/>. A flying card can first
    /// wait in place, dimmed, for <paramref name="hold"/> seconds; <paramref name="life"/> includes the wait.
    /// </summary>
    public void GhostCard(Rect2 rect, Color color, float life, Rect2? flyTo = null, float hold = 0f, float delay = 0f) =>
        Add(new GhostCardEffect(rect, flyTo, hold), color, life, delay);

    /// <summary>The edges of the screen glow in a color, for hits on the ship. <paramref name="strength"/> runs from 0 to 1.</summary>
    public void Vignette(Color color, float life, float strength = 1f, float delay = 0f) =>
        Add(new VignetteEffect(), color with { A = color.A * Mathf.Clamp(strength, 0f, 1f) }, life, delay);

    /// <summary>
    /// The part of a hull bar that a hit takes away: it stays lit for <paramref name="holdFor"/> seconds (until the
    /// hit lands), flashes, then drains to the new value. Fractions are of the bar's width.
    /// </summary>
    public void BarChunk(Rect2 bar, float fromFraction, float toFraction, Color color, float holdFor, float delay = 0f) =>
        Add(new BarChunkEffect(bar, fromFraction, toFraction, holdFor), color, holdFor + BarChunkEffect.Tail, delay);

    /// <summary>A number or word that pops, rises and fades.</summary>
    public void FloatText(string text, Vector2 at, Color color, int size, float life = 0.9f, float rise = 60f, float delay = 0f)
    {
        _texts.Add(new FloatingText(text, at, color, size, Now + delay, life, rise));
        Wake();
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, MaxStep);
        _clock += dt;
        RunDueActions();
        StepParticles(dt);
        _effects.RemoveAll(e => _clock - e.Start > e.Life);
        _texts.RemoveAll(t => _clock - t.Start > t.Life);
        StepShake(dt);
        QueueRedraw();
        _numbers.QueueRedraw();
        if (!Busy)
        {
            SetProcess(false);
        }
    }

    public override void _ExitTree()
    {
        // The target outlives a frame of this layer only while the screen is being freed; leave it unshaken.
        if (ShakeTarget is { } target && IsInstanceValid(target))
        {
            target.Position = Vector2.Zero;
        }
    }

    public override void _Draw()
    {
        if (!_warmedUp)
        {
            DrawRect(new Rect2(0, 0, 1, 1), Colors.Transparent);
            _warmedUp = true;
        }

        DrawSetTransform(_shake);
        foreach (var effect in _effects)
        {
            float age = _clock - effect.Start;
            if (age >= 0f)
            {
                effect.Draw(this, age, Mathf.Clamp(age / effect.Life, 0f, 1f));
            }
        }

        foreach (var p in _particles)
        {
            DrawParticle(p);
        }
    }

    private static float EaseOut(float t) => 1f - Mathf.Pow(1f - t, 3f);

    private static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - (Mathf.Pow((-2f * t) + 2f, 3f) / 2f);

    private static Color Fade(Color color, float alpha) => color with { A = color.A * alpha };

    private void Wake()
    {
        if (IsInsideTree() && !IsProcessing())
        {
            SetProcess(true);
        }
    }

    private void Add(Effect effect, Color color, float life, float delay)
    {
        effect.Color = color;
        effect.Start = Now + delay;
        effect.Life = Mathf.Max(0.01f, life);
        _effects.Add(effect);
        Wake();
    }

    private void AddPathParticle(Vector2 from, Vector2 ctrl, Vector2 to, Color color, float size, ParticleShape shape, float life, float delay)
    {
        if (_particles.Count >= MaxParticles)
        {
            return;
        }

        _particles.Add(new Particle
        {
            Age = _clock - Now,
            OnPath = true,
            Pos = from,
            From = from,
            Ctrl = ctrl,
            To = to,
            Life = life,
            Delay = delay,
            Size = size,
            Shape = shape,
            Color = color,
            Rot = _rng.RandfRange(0f, Mathf.Tau),
            Spin = _rng.RandfRange(-5f, 5f),
        });
    }

    private void RunDueActions()
    {
        if (_scheduled.Count == 0)
        {
            return;
        }

        // Copy first: an action may schedule more.
        var due = _scheduled.Where(s => s.Due <= _clock).OrderBy(s => s.Due).ToList();
        if (due.Count == 0)
        {
            return;
        }

        _scheduled.RemoveAll(s => s.Due <= _clock);
        foreach (var (at, action) in due)
        {
            _firingAt = at;
            try
            {
                action();
            }
            finally
            {
                _firingAt = null;
            }
        }
    }

    private void StepParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            float age = p.Age - p.Delay;
            if (age > p.Life)
            {
                _particles[i] = _particles[^1];
                _particles.RemoveAt(_particles.Count - 1);
                continue;
            }

            if (age >= 0f)
            {
                if (p.OnPath)
                {
                    float u = EaseInOut(Mathf.Clamp(age / p.Life, 0f, 1f));
                    var next = p.From.Lerp(p.Ctrl, u).Lerp(p.Ctrl.Lerp(p.To, u), u);
                    p.Vel = dt > 0f ? (next - p.Pos) / dt : p.Vel;
                    p.Pos = next;
                }
                else
                {
                    p.Vel *= Mathf.Exp(-p.Drag * dt);
                    p.Vel.Y += p.Gravity * dt;
                    p.Pos += p.Vel * dt;
                }

                p.Rot += p.Spin * dt;
            }

            _particles[i] = p;
        }
    }

    private void StepShake(float dt)
    {
        if (_trauma <= 0f)
        {
            return;
        }

        _trauma = Mathf.Max(0f, _trauma - (TraumaDecayPerSecond * dt));
        float amount = MaxShakePixels * UserSettings.ShakeStrength * _trauma * _trauma;
        float t = _clock;
        var wobble = new Vector2(
            Mathf.Sin(t * 53f) + (0.5f * Mathf.Sin((t * 31f) + 1.3f)),
            Mathf.Sin((t * 47f) + 0.7f) + (0.5f * Mathf.Sin((t * 37f) + 2.1f))) / 1.5f;
        _shake = _trauma > 0f ? wobble * amount : Vector2.Zero;
        if (ShakeTarget is { } target && IsInstanceValid(target))
        {
            target.Position = _shake;
        }
    }

    private void DrawParticle(Particle p)
    {
        float age = p.Age - p.Delay;
        if (age < 0f)
        {
            return;
        }

        float k = Mathf.Clamp(age / p.Life, 0f, 1f);
        float alpha = p.OnPath
            ? (k < 0.15f ? k / 0.15f : k > 0.8f ? (1f - k) / 0.2f : 1f)
            : 1f - (k * k);
        var c = Fade(p.Color, alpha);
        p.Size *= ParticleScale;
        switch (p.Shape)
        {
            case ParticleShape.Dot:
                DrawGlow(p.Pos, p.Size * 7f, Fade(c, 0.45f));
                DrawCircle(p.Pos, p.Size, c);
                break;
            case ParticleShape.Streak:
            {
                float speed = p.Vel.Length();
                var dir = speed > 0.01f ? p.Vel / speed : Vector2.Right;
                float length = (p.Size * 3f) + (speed * 0.04f);
                DrawLine(p.Pos - (dir * length), p.Pos, c, p.Size * 0.8f, antialiased: true);
                DrawGlow(p.Pos, p.Size * 5f, Fade(c, 0.35f));
                break;
            }

            case ParticleShape.Hex:
            {
                float r = p.Size * 1.6f;
                var points = new Vector2[7];
                for (int i = 0; i < 7; i++)
                {
                    points[i] = p.Pos + (Vector2.FromAngle(p.Rot + (i * Mathf.Tau / 6f)) * r);
                }

                DrawPolyline(points, c, 1.5f, antialiased: true);
                break;
            }

            case ParticleShape.Leaf:
            {
                float s = p.Size;
                var points = new[] { new Vector2(0, -2f * s), new Vector2(s, 0), new Vector2(0, 2f * s), new Vector2(-s, 0) };
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = p.Pos + points[i].Rotated(p.Rot);
                }

                DrawColoredPolygon(points, c);
                DrawGlow(p.Pos, s * 6f, Fade(c, 0.3f));
                break;
            }
        }
    }

    private void DrawGlow(Vector2 at, float size, Color color)
    {
        if (_glow is not null && size > 0f)
        {
            DrawTextureRect(_glow, new Rect2(at - (new Vector2(size, size) / 2f), new Vector2(size, size)), tile: false, modulate: color);
        }
    }

    private void DrawNumbers()
    {
        var font = BloomTheme.DisplayBold;
        foreach (var t in _texts)
        {
            float age = _clock - t.Start;
            if (age < 0f)
            {
                continue;
            }

            float k = Mathf.Clamp(age / t.Life, 0f, 1f);
            float scale = age < 0.12f ? Mathf.Lerp(1.4f, 1f, EaseOut(age / 0.12f)) : 1f;
            float alpha = k < 0.6f ? 1f : 1f - ((k - 0.6f) / 0.4f);
            var pos = t.At + new Vector2(0f, -t.Rise * EaseOut(k)) + _shake;
            var size = font.GetStringSize(t.Text, HorizontalAlignment.Left, -1, t.Size);
            var baseline = new Vector2(-size.X / 2f, font.GetAscent(t.Size) - (size.Y / 2f));
            _numbers.DrawSetTransform(pos, 0f, new Vector2(scale, scale));
            _numbers.DrawStringOutline(font, baseline, t.Text, HorizontalAlignment.Left, -1, t.Size, Math.Max(4, t.Size / 7), Palette.SurfaceDeep with { A = 0.9f * alpha });
            _numbers.DrawString(font, baseline, t.Text, HorizontalAlignment.Left, -1, t.Size, t.Color with { A = alpha });
        }

        _numbers.DrawSetTransform(Vector2.Zero);
    }

    private struct Particle
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public Vector2 From;
        public Vector2 Ctrl;
        public Vector2 To;
        public float Age;
        public float Delay;
        public float Life;
        public float Size;
        public float Drag;
        public float Gravity;
        public float Rot;
        public float Spin;
        public Color Color;
        public ParticleShape Shape;
        public bool OnPath;
    }

    private sealed record FloatingText(string Text, Vector2 At, Color Color, int Size, float Start, float Life, float Rise);

    /// <summary>One effect that is not a particle. <c>k</c> runs from 0 to 1 over its life.</summary>
    private abstract class Effect
    {
        public Color Color { get; set; }

        public float Start { get; set; }

        public float Life { get; set; }

        public abstract void Draw(FxLayer fx, float age, float k);
    }

    private sealed class RingEffect(Vector2 at, float from, float to, float width, bool hex, float turn) : Effect
    {
        public override void Draw(FxLayer fx, float age, float k)
        {
            float r = Mathf.Lerp(from, to, EaseOut(k));
            var c = Fade(Color, 1f - k);
            if (!hex)
            {
                fx.DrawArc(at, r, 0f, Mathf.Tau, 56, c, width, antialiased: true);
                return;
            }

            var points = new Vector2[7];
            for (int i = 0; i < 7; i++)
            {
                points[i] = at + (Vector2.FromAngle(turn + (i * Mathf.Tau / 6f)) * r);
            }

            fx.DrawPolyline(points, c, width, antialiased: true);
        }
    }

    private sealed class RectRingEffect(Rect2 rect, float grow, float width) : Effect
    {
        public override void Draw(FxLayer fx, float age, float k) =>
            fx.DrawRect(rect.Grow(grow * EaseOut(k)), Fade(Color, 1f - k), filled: false, width: width);
    }

    private sealed class BoltEffect(Vector2 from, Vector2 to, float travel, float width, Color core, float linger, bool beam) : Effect
    {
        public override void Draw(FxLayer fx, float age, float k)
        {
            float distance = from.DistanceTo(to);
            if (distance < 1f)
            {
                return;
            }

            var dir = (to - from) / distance;
            float trail = distance * 0.45f;
            Vector2 head, tail;
            float alpha;
            if (age <= travel)
            {
                float h = Mathf.Pow(Mathf.Clamp(age / travel, 0f, 1f), 1.3f);
                head = from.Lerp(to, h);
                tail = head - (dir * Mathf.Min(trail, distance * h));
                alpha = 1f;
            }
            else
            {
                float l = Mathf.Clamp((age - travel) / Mathf.Max(0.01f, linger), 0f, 1f);
                head = to;
                tail = beam ? from : (to - (dir * trail)).Lerp(to, l);
                alpha = 1f - l;
            }

            fx.DrawLine(tail, head, Fade(Color, 0.3f * alpha), width * 3.2f, antialiased: true);
            fx.DrawLine(tail, head, Fade(Color, 0.9f * alpha), width, antialiased: true);
            fx.DrawLine(tail, head, Fade(core, alpha), Mathf.Max(1f, width * 0.4f), antialiased: true);
            fx.DrawGlow(head, width * 7f, Fade(Color, 0.6f * alpha));
        }
    }

    private sealed class FlashEffect(IReadOnlyList<Rect2> rects, bool flicker) : Effect
    {
        public override void Draw(FxLayer fx, float age, float k)
        {
            float alpha = (1f - k) * (1f - k);
            if (flicker && ((int)(age * 22f) % 2) == 1)
            {
                alpha *= 0.2f;
            }

            foreach (var rect in rects)
            {
                fx.DrawRect(rect, Fade(Color, alpha));
            }
        }
    }

    private sealed class SweepEffect(IReadOnlyList<Rect2> rects) : Effect
    {
        public override void Draw(FxLayer fx, float age, float k)
        {
            if (rects.Count == 0)
            {
                return;
            }

            var bounds = rects.Aggregate(rects[0], (box, r) => box.Merge(r));
            float y = Mathf.Lerp(bounds.End.Y, bounds.Position.Y, EaseInOut(k));
            float alpha = k < 0.85f ? 1f : (1f - k) / 0.15f;
            foreach (var r in rects)
            {
                if (y < r.Position.Y || y > r.End.Y)
                {
                    continue;
                }

                fx.DrawRect(new Rect2(r.Position.X, y, r.Size.X, Mathf.Min(16f, r.End.Y - y)), Fade(Color, 0.25f * alpha));
                fx.DrawLine(new Vector2(r.Position.X, y), new Vector2(r.End.X, y), Fade(Color, alpha), 3f);
            }
        }
    }

    private sealed class PulseEffect(Vector2 at, float radius) : Effect
    {
        public override void Draw(FxLayer fx, float age, float k) =>
            fx.DrawGlow(at, radius * 2f * Mathf.Lerp(0.6f, 1.25f, EaseOut(k)), Fade(Color, 1f - k));
    }

    private sealed class GhostCardEffect(Rect2 rect, Rect2? flyTo, float hold) : Effect
    {
        public override void Draw(FxLayer fx, float age, float k)
        {
            Rect2 r;
            float alpha;
            if (flyTo is not null && age < hold)
            {
                r = rect;
                alpha = 0.4f;
            }
            else if (flyTo is { } target)
            {
                k = Mathf.Clamp((age - hold) / Mathf.Max(0.01f, Life - hold), 0f, 1f);
                float u = EaseInOut(k);
                r = new Rect2(rect.Position.Lerp(target.Position, u), rect.Size.Lerp(target.Size, u));
                alpha = 0.85f * (k < 0.8f ? 1f : (1f - k) / 0.2f);
            }
            else
            {
                float scale = 1f + (0.14f * EaseOut(k));
                var size = rect.Size * scale;
                r = new Rect2(rect.GetCenter() - (size / 2f), size);
                alpha = 0.9f * Mathf.Pow(1f - k, 1.5f);
            }

            fx.DrawRect(r, Fade(Color, 0.12f * alpha));
            fx.DrawRect(r, Fade(Color, alpha), filled: false, width: 2f);
            float arm = Mathf.Min(r.Size.X, r.Size.Y) * 0.18f;
            var bright = Fade(Color.Lerp(Colors.White, 0.3f), alpha);
            foreach (var (corner, sx, sy) in new[] { (r.Position, 1f, 1f), (new Vector2(r.End.X, r.Position.Y), -1f, 1f), (r.End, -1f, -1f), (new Vector2(r.Position.X, r.End.Y), 1f, -1f) })
            {
                fx.DrawPolyline([corner + new Vector2(0, sy * arm), corner, corner + new Vector2(sx * arm, 0)], bright, 3f, antialiased: true);
            }
        }
    }

    private sealed class VignetteEffect : Effect
    {
        public override void Draw(FxLayer fx, float age, float k)
        {
            float alpha = k < 0.08f ? k / 0.08f : Mathf.Pow(1f - ((k - 0.08f) / 0.92f), 1.5f);
            var outer = Fade(Color, 0.55f * alpha);
            var inner = Fade(Color, 0f);

            // The vignette belongs to the screen, not its content, so it does not shake.
            var size = fx.Size;
            var o = -fx._shake;
            float th = Mathf.Min(size.X, size.Y) * 0.2f;
            var tl = o;
            var tr = o + new Vector2(size.X, 0);
            var br = o + size;
            var bl = o + new Vector2(0, size.Y);
            var itl = tl + new Vector2(th, th);
            var itr = tr + new Vector2(-th, th);
            var ibr = br - new Vector2(th, th);
            var ibl = bl + new Vector2(th, -th);
            Color[] colors = [outer, outer, inner, inner];
            fx.DrawPolygon([tl, tr, itr, itl], colors);
            fx.DrawPolygon([tr, br, ibr, itr], colors);
            fx.DrawPolygon([br, bl, ibl, ibr], colors);
            fx.DrawPolygon([bl, tl, itl, ibl], colors);
        }
    }

    private sealed class BarChunkEffect(Rect2 bar, float from, float to, float holdFor) : Effect
    {
        public const float Tail = 0.45f;
        private const float FlashTime = 0.1f;

        public override void Draw(FxLayer fx, float age, float k)
        {
            var inner = bar.Grow(-2f);
            float left = inner.Position.X + (inner.Size.X * Mathf.Clamp(to, 0f, 1f));
            float right = inner.Position.X + (inner.Size.X * Mathf.Clamp(from, 0f, 1f));
            float alpha;
            var color = Color.Lerp(Colors.White, 0.45f);
            if (age < holdFor)
            {
                alpha = 0.55f;
            }
            else if (age < holdFor + FlashTime)
            {
                alpha = 1f;
                color = Colors.White;
            }
            else
            {
                float d = Mathf.Clamp((age - holdFor - FlashTime) / (Tail - FlashTime), 0f, 1f);
                right = Mathf.Lerp(right, left, EaseInOut(d));
                alpha = 0.8f * (1f - d);
            }

            if (right > left)
            {
                fx.DrawRect(new Rect2(left, inner.Position.Y, right - left, inner.Size.Y), Fade(color, alpha));
            }
        }
    }
}
