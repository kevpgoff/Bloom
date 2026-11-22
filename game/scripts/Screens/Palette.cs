using Bloom.Core.Cards;
using Bloom.Core.Pieces;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Option A solarpunk palette: deep botanical-slate surfaces paired with warm parchment ink,
/// muted brass linework, and luminous commune/tag accents.
/// </summary>
internal static class Palette
{
    public static readonly Color BackdropTop = new("0d161b");
    public static readonly Color BackdropMid = new("132026");
    public static readonly Color BackdropGlow = new("243b36");
    public static readonly Color SurfaceDeep = new("111a20");
    public static readonly Color Surface = new("172329");
    public static readonly Color CardSurface = new("1b2931");
    public static readonly Color ButtonBg = new("203038");
    public static readonly Color ButtonHover = new("293e47");
    public static readonly Color ButtonPressed = new("16242b");

    public static readonly Color Ink = new("f2ede4");
    public static readonly Color BrassLine = new("9ea88e");
    public static readonly Color BrassBright = new("e9c46a");
    public static readonly Color LineMuted = new("35474f");
    public static readonly Color GardenGlow = new("7bc47f");

    public static readonly Color LaneEven = new(0.08f, 0.14f, 0.18f, 0.0f);
    public static readonly Color LaneOdd = new(0.62f, 0.72f, 0.68f, 0.045f);
    public static readonly Color HullShadow = new(0.02f, 0.05f, 0.07f, 0.55f);
    public static readonly Color Hull = new("243740");
    public static readonly Color HullBorder = new("4f6c75");
    public static readonly Color PieceText = new("131a1e");
    public static readonly Color DisabledText = new("cfd6d9");

    public static readonly Color Attack = new("ff8a80");
    public static readonly Color Heavy = new("ffb74d");
    public static readonly Color Block = new("81d4fa");
    public static readonly Color HeavyLane = new(1f, 0.58f, 0.22f, 0.18f);

    public static readonly Color Selectable = new("ffe066");
    public static readonly Color Chosen = new("7cfc9a");
    public static readonly Color Badge = new("fff3b0");
    public static readonly Color Boosted = new("7cfc9a");
    public static readonly Color Energy = new("ffe066");
    public static readonly Color Muted = new("9fb0b5");

    public static readonly Color FitOk = new(0.49f, 0.99f, 0.6f, 0.45f);
    public static readonly Color FitBad = new(1f, 0.32f, 0.32f, 0.45f);
    public static readonly Color Refused = new("ff5252");
    public static readonly Color CargoBack = new(0.08f, 0.12f, 0.15f, 0.92f);
    public static readonly Color CargoSlot = new("1c2930");
    public static readonly Color CargoOver = new("4a2326");
    public static readonly Color CargoDrop = new(1f, 0.88f, 0.4f, 0.18f);

    private static readonly Color Crew = new("d8a7ca");
    private static readonly Color Untagged = new("9e9e9e");

    public static Color ForPiece(PieceDef def)
    {
        if (def.Tags.Count == 0)
        {
            return def.Kind == PieceKind.Crew ? Crew : Untagged;
        }

        return def.Tags[0] switch
        {
            Tag.Core => new Color("e9c46a"),
            Tag.Weapon => new Color("e76f51"),
            Tag.Shield => new Color("8ab6d6"),
            Tag.Solar => new Color("f4a261"),
            Tag.Garden => new Color("7bc47f"),
            Tag.Drone => new Color("b39ddb"),
            _ => Untagged,
        };
    }

    public static Color ForEffect(Effect effect) => effect switch
    {
        DamageEffect => Attack,
        BlockEffect => Block,
        GainEnergyEffect => Energy,
        DrawEffect => Crew,
        RepairEffect => BrassBright,
        FocusEffect => Heavy,
        GrowEffect => GardenGlow,
        _ => Ink,
    };
}
