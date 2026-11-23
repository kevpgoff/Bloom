using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// One part of a boss on the combat screen, drawn in a framed panel: its name, a hull bar, and what it does this
/// turn. A tap reports <see cref="Tapped"/>; the screen decides what it means. The part the player aims at wears
/// the reticle's corner brackets, and a disabled part goes dark.
/// </summary>
/// <remarks>Combat feedback can <see cref="Flash"/> it when it is hit, and find its hull bar with <see cref="BarRect"/>.</remarks>
public partial class PartPanel : Control
{
    private const float Pad = 12f;
    private const int NameSize = 17;
    private const int HullSize = 17;
    private const float BarHeight = 10f;
    private const float IconSize = 26f;
    private const float FlashLife = 0.25f;

    private string _name = "";
    private int _hull;
    private int _maxHull = 1;
    private Texture2D? _intentIcon;
    private string _intentText = "";
    private Color _intentColor = Colors.White;
    private bool _aimed;
    private bool _disabled;
    private Color _flashColor = Colors.White;
    private float _flashAge = FlashLife;

    /// <summary>The panel was tapped.</summary>
    public event Action? Tapped;

    /// <summary>Sets everything the panel shows.</summary>
    public void SetPart(string name, int hull, int maxHull, Texture2D? intentIcon, string intentText, Color intentColor, bool aimed)
    {
        _name = name.ToUpperInvariant();
        _hull = hull;
        _maxHull = Math.Max(1, maxHull);
        _disabled = hull <= 0;
        _intentIcon = intentIcon;
        _intentText = intentText;
        _intentColor = intentColor;
        _aimed = aimed && !_disabled;
        QueueRedraw();
    }

    /// <summary>The panel lights up in a color and fades back.</summary>
    public void Flash(Color color)
    {
        _flashColor = color;
        _flashAge = 0f;
        SetProcess(true);
    }

    /// <summary>The hull bar, in local coordinates.</summary>
    public Rect2 BarRect()
    {
        float top = Pad + NameSize + 10f;
        float width = Size.X - (Pad * 2) - HullTextWidth() - 8f;
        return new Rect2(Pad, top, Mathf.Max(0f, width), BarHeight);
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0, 112);
        SetProcess(false);
        Resized += QueueRedraw;
    }

    public override void _Process(double delta)
    {
        _flashAge += (float)delta;
        QueueRedraw();
        if (_flashAge >= FlashLife)
        {
            SetProcess(false);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            AcceptEvent();
            Tapped?.Invoke();
        }
    }

    private float HullTextWidth() =>
        BloomTheme.Hud.GetStringSize($"{_maxHull} / {_maxHull}", HorizontalAlignment.Left, -1, HullSize).X;

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        if (rect.Size.X <= 0)
        {
            return;
        }

        var line = _disabled ? Palette.Muted : Palette.Threat;
        float flash = _flashAge < FlashLife ? 1f - (_flashAge / FlashLife) : 0f;

        // The surface and a thin rim; the aimed part's rim is brighter and its surface warmer.
        var surface = Palette.SurfaceDeep.Lerp(line, _aimed ? 0.1f : 0.03f) with { A = _disabled ? 0.45f : 0.8f };
        DrawRect(rect, surface.Lerp(_flashColor, flash * 0.35f));
        DrawRect(rect.Grow(-1f), line with { A = _aimed ? 0.75f : _disabled ? 0.2f : 0.35f }, filled: false, width: _aimed ? 2f : 1.5f);

        var font = BloomTheme.Hud;
        float inner = Size.X - (Pad * 2);

        // The name, in tracked capitals.
        int nameSize = NameSize;
        while (nameSize > 12 && font.GetStringSize(_name, HorizontalAlignment.Left, -1, nameSize).X > inner)
        {
            nameSize--;
        }

        DrawString(font, new Vector2(Pad, Pad + font.GetAscent(nameSize)), _name, HorizontalAlignment.Left, inner, nameSize,
            _disabled ? Palette.Muted with { A = 0.7f } : Palette.Label(Palette.Threat));

        // The hull bar, with the numbers to its right.
        var bar = BarRect();
        DrawRect(bar, Palette.LineMuted with { A = 0.8f });
        if (_hull > 0)
        {
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * _hull / _maxHull, bar.Size.Y)), Palette.Threat);
        }

        string hullText = $"{_hull} / {_maxHull}";
        float hullWidth = HullTextWidth();
        DrawString(font, new Vector2(Size.X - Pad - hullWidth, bar.GetCenter().Y + (font.GetAscent(HullSize) * 0.4f)), hullText,
            HorizontalAlignment.Right, hullWidth, HullSize, _disabled ? Palette.Muted with { A = 0.7f } : Palette.Ink);

        // What it does this turn, or that it is out of the fight.
        float rowTop = bar.End.Y + 10f;
        float textX = Pad;
        var textColor = _disabled ? Palette.Muted with { A = 0.7f } : _intentColor;
        if (_intentIcon is { } icon && !_disabled)
        {
            DrawTextureRect(icon, new Rect2(Pad, rowTop, IconSize, IconSize), tile: false, modulate: _intentColor);
            textX += IconSize + 8f;
        }

        string text = _disabled ? "Disabled" : _intentText;
        int textSize = 20;
        float room = Size.X - Pad - textX;
        while (textSize > 14 && BloomTheme.Body.GetStringSize(text, HorizontalAlignment.Left, -1, textSize).X > room)
        {
            textSize--;
        }

        float baseline = rowTop + (IconSize / 2f) + (BloomTheme.Body.GetAscent(textSize) * 0.4f);
        DrawString(BloomTheme.Body, new Vector2(textX, baseline), text, HorizontalAlignment.Left, room, textSize, textColor);

        if (_aimed)
        {
            DrawBrackets(rect, Palette.Threat);
        }
    }

    /// <summary>The reticle's corner brackets, just inside the panel's corners.</summary>
    private void DrawBrackets(Rect2 rect, Color color)
    {
        float arm = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.2f;
        var r = rect.Grow(-4f);
        foreach (var (corner, sx, sy) in new[]
                 {
                     (r.Position, 1f, 1f),
                     (new Vector2(r.End.X, r.Position.Y), -1f, 1f),
                     (r.End, -1f, -1f),
                     (new Vector2(r.Position.X, r.End.Y), 1f, -1f),
                 })
        {
            DrawPolyline([corner + new Vector2(0, sy * arm), corner, corner + new Vector2(sx * arm, 0)], color, 3f, antialiased: true);
        }
    }
}
