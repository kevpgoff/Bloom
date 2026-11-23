using Bloom.Game.Screens;
using Godot;

namespace Bloom.Game.Visuals;

/// <summary>
/// Option A "Holo Grove" theme: dark glass panels with thin hologram borders, the Saira typeface, and
/// 9-slice bracket frames laid over panels and cards.
/// </summary>
/// <remarks>
/// All four fonts come from one variable font file, <c>res://art/fonts/Saira.ttf</c>. Its width axis gives
/// both faces: Saira at width 100 for body text, and Saira Semi Condensed (width 87.5) for headings,
/// numbers and the tracked uppercase labels on the hologram parts.
/// </remarks>
internal static class BloomTheme
{
    private const string FontPath = "res://art/fonts/Saira.ttf";

    /// <summary>Theme type variation for screen titles and the enemy's name: <c>theme_type_variation = &amp;"Heading"</c>.</summary>
    public const string Heading = "Heading";

    private static Font? _body;
    private static Font? _display;
    private static Font? _displayBold;
    private static Font? _hud;

    /// <summary>Running text: Saira Medium.</summary>
    public static Font Body => _body ??= Variation(weight: 500, width: 100, spacing: 0);

    /// <summary>Headings and names: Saira Semi Condensed SemiBold.</summary>
    public static Font Display => _display ??= Variation(weight: 600, width: 87.5f, spacing: 0);

    /// <summary>Numbers on cards and badges: Saira Semi Condensed Bold.</summary>
    public static Font DisplayBold => _displayBold ??= Variation(weight: 700, width: 87.5f, spacing: 0);

    /// <summary>Uppercase hologram labels (piece names, captions, lane marks): Semi Condensed SemiBold, tracked out.</summary>
    public static Font Hud => _hud ??= Variation(weight: 600, width: 87.5f, spacing: 2);

    public static Theme Build()
    {
        var theme = new Theme { DefaultFont = Body };

        theme.SetColor("font_color", "Label", Palette.Ink);
        theme.SetColor("default_color", "RichTextLabel", Palette.Ink);
        theme.SetFont("normal_font", "RichTextLabel", Body);
        theme.SetFont("bold_font", "RichTextLabel", Display);

        theme.SetTypeVariation(Heading, "Label");
        theme.SetFont("font", Heading, Display);
        theme.SetColor("font_color", Heading, Palette.Ink);

        // Panels: dark glass with a thin hologram border; AttachPanelFrame adds the corner brackets.
        theme.SetStylebox("panel", "PanelContainer", FlatBox(Palette.Surface with { A = 0.9f }, Palette.HoloLine with { A = 0.3f }, border: 1, radius: 3));

        // Buttons: a dark chamfered plate with bracketed corners (ui/button_frame), tinted per state.
        theme.SetFont("font", "Button", Display);
        theme.SetStylebox("normal", "Button", ButtonBox(Palette.HoloLine with { A = 0.85f }, FlatBox(Palette.ButtonBg with { A = 0.92f }, Palette.HoloLine with { A = 0.5f }, border: 2, radius: 3, padX: 14, padY: 10)));
        theme.SetStylebox("hover", "Button", ButtonBox(Palette.HoloBright, FlatBox(Palette.ButtonHover, Palette.HoloLine, border: 2, radius: 3, padX: 14, padY: 10)));
        theme.SetStylebox("pressed", "Button", ButtonBox(Colors.White, FlatBox(Palette.ButtonPressed, Palette.HoloBright, border: 2, radius: 3, padX: 14, padY: 10)));
        theme.SetStylebox("disabled", "Button", ButtonBox(Palette.LineMuted.Lightened(0.15f), FlatBox(Palette.SurfaceDeep with { A = 0.85f }, Palette.LineMuted, border: 1, radius: 3, padX: 14, padY: 10)));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetColor("font_color", "Button", Palette.Ink);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Palette.HoloBright);
        theme.SetColor("font_disabled_color", "Button", Palette.Muted with { A = 0.7f });

        // Text fields (the hangar's seed): a dark well with the hologram's edge, brighter while it is being typed in.
        theme.SetFont("font", "LineEdit", Hud);
        theme.SetStylebox("normal", "LineEdit", FlatBox(Palette.SurfaceDeep with { A = 0.92f }, Palette.HoloLine with { A = 0.5f }, border: 2, radius: 3, padX: 14, padY: 8));
        theme.SetStylebox("focus", "LineEdit", FlatBox(Palette.SurfaceDeep with { A = 0.92f }, Palette.HoloBright, border: 2, radius: 3, padX: 14, padY: 8));
        theme.SetColor("font_color", "LineEdit", Palette.Ink);
        theme.SetColor("caret_color", "LineEdit", Palette.HoloBright);
        theme.SetColor("selection_color", "LineEdit", Palette.HoloLine with { A = 0.35f });

        // The enemy's hull bar, in the enemy's ember.
        theme.SetStylebox("background", "ProgressBar", FlatBox(Palette.SurfaceDeep, Palette.Threat with { A = 0.4f }, border: 1, radius: 1));
        theme.SetStylebox("fill", "ProgressBar", FlatBox(Palette.Threat with { A = 0.85f }, Palette.Threat.Lightened(0.25f), border: 1, radius: 1));

        return theme;
    }

    /// <summary>A card in the hand: dark glass, edged in the color of the piece that adds it.</summary>
    public static StyleBoxFlat CardSurface(Color accent, bool highlighted, bool hovered, bool disabled)
    {
        var bg = disabled
            ? Palette.SurfaceDeep
            : Palette.CardSurface.Lerp(accent, hovered ? 0.1f : 0.04f);
        var border = highlighted
            ? Palette.Selectable
            : disabled
                ? Palette.LineMuted
                : hovered
                    ? accent
                    : accent with { A = 0.35f };
        var box = FlatBox(bg, border, border: highlighted ? 2 : 1, radius: 2);
        box.ShadowColor = highlighted ? Palette.Selectable with { A = 0.3f } : new Color(0f, 0f, 0f, disabled ? 0.1f : 0.35f);
        box.ShadowSize = highlighted ? 10 : 4;
        box.ShadowOffset = highlighted ? Vector2.Zero : new Vector2(0, 3);
        return box;
    }

    /// <summary>An offered piece on the Reward screen.</summary>
    public static StyleBoxFlat OfferSurface(Color accent, bool selected)
    {
        var bg = Palette.Surface.Lerp(accent, selected ? 0.1f : 0.03f) with { A = 0.92f };
        var border = selected ? Palette.Selectable : accent with { A = 0.35f };
        var box = FlatBox(bg, border, border: selected ? 2 : 1, radius: 3);
        box.ShadowColor = selected ? Palette.Selectable with { A = 0.25f } : new Color(0f, 0f, 0f, 0.3f);
        box.ShadowSize = selected ? 10 : 4;
        return box;
    }

    /// <summary>
    /// A 9-slice frame laid over a Control without taking input. The corner margin is a quarter of the
    /// texture's width, so a replacement texture of another size still works.
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

    /// <summary>Lays the bracket frame over a PanelContainer.</summary>
    public static void AttachPanelFrame(Control panel, Color? tint = null)
    {
        if (CreateFrameOverlay(ArtLibrary.PanelFrame, tint ?? Palette.HoloLine with { A = 0.7f }) is { } overlay)
        {
            panel.AddChild(overlay);
        }
    }

    /// <summary>
    /// A button state drawn from <c>ui/button_frame</c>, tinted <paramref name="tint"/>. The texture's 20 px
    /// margins keep its corners unscaled. Without the texture, the flat <paramref name="fallback"/> is used.
    /// </summary>
    private static StyleBox ButtonBox(Color tint, StyleBoxFlat fallback)
    {
        if (ArtLibrary.ButtonFrame is not { } texture)
        {
            return fallback;
        }

        var box = new StyleBoxTexture
        {
            Texture = texture,
            ModulateColor = tint,
            DrawCenter = true,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        box.SetTextureMarginAll(20);
        return box;
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

    /// <summary>One instance of the variable font. Falls back to Godot's default font if the file is missing.</summary>
    private static Font Variation(float weight, float width, int spacing)
    {
        if (!ResourceLoader.Exists(FontPath) || GD.Load<FontFile>(FontPath) is not { } file)
        {
            GD.PushWarning($"BloomTheme: {FontPath} is missing; using the default font.");
            return ThemeDB.FallbackFont;
        }

        // The axes must be keyed by their numeric OpenType tags: Godot ignores the string "wght", and Saira's
        // default instance is Thin (weight 100), so a missed key silently draws every label hairline-thin.
        var server = TextServerManager.GetPrimaryInterface();
        var font = new FontVariation
        {
            BaseFont = file,
            VariationOpentype = new Godot.Collections.Dictionary
            {
                [server.NameToTag("wght")] = weight,
                [server.NameToTag("wdth")] = width,
            },
        };
        font.SetSpacing(TextServer.SpacingType.Glyph, spacing);
        return font;
    }
}
