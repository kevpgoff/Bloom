using Bloom.Core.Pieces;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>Placeholder colors until there is art. Pieces are colored by their first tag; crew without tags share one color.</summary>
internal static class Palette
{
    public static readonly Color LaneEven = new("1b2429");
    public static readonly Color LaneOdd = new("1f2a30");
    public static readonly Color Hull = new("33454f");
    public static readonly Color PieceText = new("161616");
    public static readonly Color DisabledText = new("c8c8c8");

    public static readonly Color Attack = new("ff8a80");
    public static readonly Color Heavy = new("ffb74d");
    public static readonly Color Block = new("81d4fa");
    public static readonly Color HeavyLane = new(1f, 0.55f, 0.2f, 0.22f);

    public static readonly Color Selectable = new("ffe066");
    public static readonly Color Chosen = new("7cfc9a");
    public static readonly Color Badge = new("fff3b0");
    public static readonly Color Boosted = new("7cfc9a");
    public static readonly Color Energy = new("ffe066");
    public static readonly Color Muted = new("a0a8ad");

    public static readonly Color FitOk = new(0.49f, 0.99f, 0.6f, 0.45f);
    public static readonly Color FitBad = new(1f, 0.32f, 0.32f, 0.45f);
    public static readonly Color Refused = new("ff5252");
    public static readonly Color CargoBack = new("161d21");
    public static readonly Color CargoSlot = new("1f2a30");
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
}
