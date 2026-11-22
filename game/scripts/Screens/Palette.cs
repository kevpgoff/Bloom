using Bloom.Core.Cards;
using Bloom.Core.Pieces;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Option A "Holo Grove" palette: a near-black teal night, cyan hologram lines, and tag colors that read
/// as light. A piece's color is used for its outline, glyph and label, never as a solid fill.
/// </summary>
internal static class Palette
{
    public static readonly Color BackdropTop = new("020a0a");
    public static readonly Color BackdropMid = new("051416");
    public static readonly Color BackdropGlow = new("08262a");
    public static readonly Color SurfaceDeep = new("030e0f");
    public static readonly Color Surface = new("051516");
    public static readonly Color CardSurface = new("061a1b");
    public static readonly Color ButtonBg = new("06191a");
    public static readonly Color ButtonHover = new("0a2627");
    public static readonly Color ButtonPressed = new("0d3233");

    public static readonly Color Ink = new("e6fbf6");
    public static readonly Color Muted = new("7fb3ad");

    /// <summary>The hologram line color: panel borders, grid marks, brackets.</summary>
    public static readonly Color HoloLine = new("5fe8e0");

    /// <summary>A brighter hologram line, for hovered and emphasized edges.</summary>
    public static readonly Color HoloBright = new("a6fff7");
    public static readonly Color LineMuted = new("1d4644");

    /// <summary>The open deck between pieces, and the root traces that run across it.</summary>
    public static readonly Color Deck = new(0.024f, 0.098f, 0.098f, 0.78f);
    public static readonly Color Trace = new("1f5a57");

    public static readonly Color Attack = new("ff6f7d");
    public static readonly Color Heavy = new("ffb347");
    public static readonly Color Block = new("5ff0ff");

    /// <summary>The enemy: anomalies and feral machines, in a hot ember that no ship part uses.</summary>
    public static readonly Color Threat = new("ff6a5c");

    public static readonly Color Selectable = new("ffe27a");
    public static readonly Color Chosen = new("6dffa8");
    public static readonly Color Badge = new("6dffa8");
    public static readonly Color Boosted = new("6dffa8");
    public static readonly Color Energy = new("ffd36b");
    public static readonly Color GardenGlow = new("6dffa8");

    /// <summary>The draw effect: cards coming into the hand.</summary>
    public static readonly Color Draw = new("ff8ff0");

    /// <summary>The repair effect: a disabled piece coming back online.</summary>
    public static readonly Color Repair = new("7ee0d6");

    /// <summary>The focus effect: a pale gold lens light.</summary>
    public static readonly Color Focus = new("fff0b0");

    /// <summary>A disabled piece and a refused move.</summary>
    public static readonly Color Refused = new("ff5a6e");

    public static readonly Color FitOk = new(0.43f, 1f, 0.66f, 0.8f);
    public static readonly Color FitBad = new(1f, 0.35f, 0.43f, 0.8f);
    public static readonly Color CargoBack = new(0.012f, 0.055f, 0.06f, 0.9f);
    public static readonly Color CargoSlot = new(0.024f, 0.098f, 0.098f, 0.8f);
    public static readonly Color CargoOver = new(0.25f, 0.06f, 0.09f, 0.8f);
    public static readonly Color CargoDrop = new(1f, 0.89f, 0.48f, 0.12f);

    private static readonly Color Crew = new("ff8ff0");
    private static readonly Color Untagged = new("b9c7c4");

    public static Color ForPiece(PieceDef def)
    {
        if (def.Tags.Count == 0)
        {
            return def.Kind == PieceKind.Crew ? Crew : Untagged;
        }

        return def.Tags[0] switch
        {
            Tag.Core => new Color("fff0b0"),
            Tag.Weapon => new Color("ff6f7d"),
            Tag.Shield => new Color("5ff0ff"),
            Tag.Solar => new Color("ffd36b"),
            Tag.Garden => new Color("6dffa8"),
            Tag.Drone => new Color("a99bff"),
            _ => Untagged,
        };
    }

    public static Color ForEffect(Effect effect) => effect switch
    {
        DamageEffect => Attack,
        BlockEffect => Block,
        GainEnergyEffect => Energy,
        DrawEffect => Draw,
        RepairEffect => Repair,
        FocusEffect => Focus,
        GrowEffect => GardenGlow,
        _ => Ink,
    };

    /// <summary>The glyph on a piece: its color, a little whiter. Keeps the color's alpha.</summary>
    public static Color Glyph(Color piece) => piece.Lerp(Colors.White, 0.2f) with { A = piece.A };

    /// <summary>The name on a piece: its color, much whiter, so it stays readable over scanlines.</summary>
    public static Color Label(Color piece) => piece.Lerp(Colors.White, 0.45f) with { A = piece.A };
}
