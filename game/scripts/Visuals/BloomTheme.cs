using Bloom.Game.Screens;
using Godot;

namespace Bloom.Game.Visuals;

/// <summary>
/// Option A solarpunk vector theme: deep botanical-slate surfaces, warm parchment typography,
/// brass/sage accents, and reusable Art Nouveau 9-slice frame overlays.
/// </summary>
internal static class BloomTheme
{
    public static Theme Build()
    {
        var theme = new Theme();

        // Warm parchment default typography on dark botanical slate.
        theme.SetColor("font_color", "Label", Palette.Ink);
        theme.SetColor("default_color", "RichTextLabel", Palette.Ink);

        // PanelContainer: deep slate glass card with subtle warm brass/sage hairline.
        var panel = FlatBox(Palette.Surface, Palette.BrassLine, border: 2, radius: 12);
        theme.SetStylebox("panel", "PanelContainer", panel);

        // Buttons: crisp chamfer-inspired solarpunk buttons with tactile press/hover states.
        theme.SetStylebox("normal", "Button", FlatBox(Palette.ButtonBg, Palette.BrassLine, border: 2, radius: 10, padX: 14, padY: 10));
        theme.SetStylebox("hover", "Button", FlatBox(Palette.ButtonHover, Palette.BrassBright, border: 2, radius: 10, padX: 14, padY: 10));
        theme.SetStylebox("pressed", "Button", FlatBox(Palette.ButtonPressed, Palette.Selectable, border: 2, radius: 10, padX: 14, padY: 10));
        theme.SetStylebox("disabled", "Button", FlatBox(Palette.SurfaceDeep, Palette.LineMuted, border: 1, radius: 10, padX: 14, padY: 10));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());

        theme.SetColor("font_color", "Button", Palette.Ink);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Palette.Selectable);
        theme.SetColor("font_disabled_color", "Button", Palette.Muted);

        // ProgressBar: enemy hull bar styled in warm coral/terracotta over dark hull track.
        var barBg = FlatBox(Palette.SurfaceDeep, Palette.LineMuted, border: 1, radius: 7);
        var barFill = FlatBox(Palette.Attack.Darkened(0.18f), Palette.Attack, border: 1, radius: 7);
        theme.SetStylebox("background", "ProgressBar", barBg);
        theme.SetStylebox("fill", "ProgressBar", barFill);

        return theme;
    }

    /// <summary>Creates a card button StyleBoxFlat tinted subtly by the piece's commune/tag accent.</summary>
    public static StyleBoxFlat CardSurface(Color accent, bool highlighted, bool hovered, bool disabled)
    {
        var bg = disabled
            ? Palette.SurfaceDeep
            : hovered
                ? Palette.CardSurface.Lerp(accent, 0.14f)
                : Palette.CardSurface.Lerp(accent, 0.06f);
        var border = highlighted
            ? Palette.Selectable
            : disabled
                ? Palette.LineMuted
                : hovered
                    ? accent.Lightened(0.2f)
                    : accent with { A = 0.75f };
        var box = FlatBox(bg, border, border: highlighted ? 3 : 2, radius: 12);
        box.ShadowColor = new Color(0f, 0f, 0f, disabled ? 0.1f : 0.35f);
        box.ShadowSize = highlighted ? 8 : 4;
        box.ShadowOffset = new Vector2(0, 3);
        return box;
    }

    /// <summary>Creates an offer card StyleBoxFlat for the Reward screen.</summary>
    public static StyleBoxFlat OfferSurface(Color accent, bool selected)
    {
        var bg = selected ? Palette.CardSurface.Lerp(accent, 0.14f) : Palette.Surface;
        var border = selected ? Palette.Selectable : accent with { A = 0.55f };
        var box = FlatBox(bg, border, border: selected ? 4 : 2, radius: 12);
        box.ShadowColor = new Color(0f, 0f, 0f, 0.3f);
        box.ShadowSize = selected ? 8 : 4;
        return box;
    }

    /// <summary>
    /// Creates a non-interactive 9-slice Art Nouveau border overlay that stretches cleanly over any Control.
    /// Corner margins scale proportionally from the loaded texture width so drop-in replacements work at any DPI.
    /// </summary>
    public static NinePatchRect? CreateFrameOverlay(Texture2D? texture, Color tint, float cornerFraction = 0.25f)
    {
        if (texture is null)
        {
            return null;
        }

        int margin = Math.Max(4, Mathf.RoundToInt(texture.GetWidth() * cornerFraction));
        var patch = new NinePatchRect
        {
            Texture = texture,
            Modulate = tint,
            DrawCenter = false,
            PatchMarginLeft = margin,
            PatchMarginTop = margin,
            PatchMarginRight = margin,
            PatchMarginBottom = margin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        patch.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return patch;
    }

    /// <summary>Adds a subtle Art Nouveau panel border overlay to an existing PanelContainer.</summary>
    public static void AttachPanelFrame(Control panel, Color? tint = null)
    {
        if (CreateFrameOverlay(ArtLibrary.PanelFrame, tint ?? Palette.BrassLine with { A = 0.55f }) is { } overlay)
        {
            panel.AddChild(overlay);
        }
    }

    public static StyleBoxFlat FlatBox(Color bg, Color borderColor, int border = 2, int radius = 10, int padX = 0, int padY = 0)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = borderColor,
            AntiAliasing = true,
            AntiAliasingSize = 1f,
        };
        box.SetBorderWidthAll(border);
        box.SetCornerRadiusAll(radius);
        if (padX > 0 || padY > 0)
        {
            box.ContentMarginLeft = padX;
            box.ContentMarginRight = padX;
            box.ContentMarginTop = padY;
            box.ContentMarginBottom = padY;
        }

        return box;
    }
}
