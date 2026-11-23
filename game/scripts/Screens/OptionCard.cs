using Bloom.Core.Pieces;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// A tappable card for one choice on the reward, grove and dock screens: a framed surface in an accent
/// color, an optional piece shape on the left, and BBCode text. It grows to fit its text, but never below
/// its minimum height. Selection is shown by <see cref="SetSelected"/>; what a tap does is up to the screen.
/// </summary>
internal sealed class OptionCard
{
    private readonly Color _accent;
    private readonly NinePatchRect? _frame;
    private readonly RichTextLabel _text;

    public OptionCard(Color accent, string text, PieceDef? piece = null, float minHeight = 150, float shapeSize = 150, int fontSize = 24)
    {
        _accent = accent;
        Button = new Button
        {
            CustomMinimumSize = new Vector2(0, minHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            ClipContents = true,
        };
        _frame = BloomTheme.CreateFrameOverlay(ArtLibrary.PanelFrame, accent with { A = 0.65f });
        if (_frame is not null)
        {
            Button.AddChild(_frame);
        }

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (string side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        }

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 16);
        if (piece is not null)
        {
            row.AddChild(new ShapeView
            {
                Piece = piece,
                CustomMinimumSize = new Vector2(shapeSize, shapeSize),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        _text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = text,
        };
        _text.AddThemeFontSizeOverride("normal_font_size", fontSize);
        _text.AddThemeFontSizeOverride("bold_font_size", fontSize + 8);
        row.AddChild(_text);

        margin.AddChild(row);
        Button.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // A button does not size to its children, so follow the text as it wraps to the card's width.
        margin.MinimumSizeChanged += () =>
            Button.CustomMinimumSize = new Vector2(0, Mathf.Max(minHeight, margin.GetCombinedMinimumSize().Y));
        SetSelected(false);
    }

    public Button Button { get; }

    public string Text
    {
        get => _text.Text;
        set => _text.Text = value;
    }

    /// <summary>Lights the card as the chosen one, or returns it to its accent color.</summary>
    public void SetSelected(bool selected)
    {
        var box = BloomTheme.OfferSurface(_accent, selected);
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
        {
            Button.AddThemeStyleboxOverride(state, box);
        }

        Button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        if (_frame is not null)
        {
            _frame.Modulate = selected ? Palette.Selectable : _accent with { A = 0.65f };
        }
    }

    /// <summary>A short muted heading over a group of cards, such as the cargo hold's pieces at a dock.</summary>
    public static Label Heading(string text)
    {
        var label = new Label { Text = text, ThemeTypeVariation = "Heading" };
        label.AddThemeFontSizeOverride("font_size", 26);
        label.AddThemeColorOverride("font_color", Palette.Muted);
        return label;
    }
}
