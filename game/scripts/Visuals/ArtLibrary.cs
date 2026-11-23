using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Map;
using Bloom.Core.Pieces;
using Godot;

namespace Bloom.Game.Visuals;

/// <summary>
/// Loads and caches the icons and 9-slice UI frames in <c>res://art/</c>, as <c>.svg</c> or <c>.png</c>.
/// Everything goes through Godot's importer, so the editor, a run from the command line and an exported
/// build all get the same textures. The size comes from each file's <c>.import</c>: icons import at
/// <c>svg/scale=3.0</c> with mipmaps, and frames at 1.0 because a 9-slice draws its corners in texture pixels.
/// A file that has not been imported yet (no <c>.import</c> next to it) is treated as missing.
/// </summary>
internal static class ArtLibrary
{
    /// <summary>Icons are drawn up to about 76 px; a narrower texture was imported at the default scale.</summary>
    private const int MinIconWidth = 128;
    private static readonly Dictionary<string, Texture2D?> Cache = [];

    public static Texture2D? CardFrame => Load("res://art/ui/card_frame", isIcon: false);

    public static Texture2D? PanelFrame => Load("res://art/ui/panel_frame", isIcon: false);

    public static Texture2D? ButtonFrame => Load("res://art/ui/button_frame", isIcon: false);

    public static Texture2D? EnergyBadge => Load("res://art/ui/energy_badge");

    /// <summary>A soft white radial glow, tinted and drawn behind glyphs and the enemy.</summary>
    public static Texture2D? Glow => Load("res://art/textures/glow", isIcon: false);

    public static Texture2D? PieceIcon(PieceDef def)
    {
        if (Load($"res://art/icons/pieces/{def.Id}") is { } exact)
        {
            return exact;
        }

        if (def.Tags.Count == 0)
        {
            return Load(def.Kind == PieceKind.Crew
                ? "res://art/icons/pieces/spore_singer"
                : "res://art/icons/pieces/bridge");
        }

        string fallback = def.Tags[0] switch
        {
            Tag.Core => "bridge",
            Tag.Weapon => "light_lance",
            Tag.Shield => "hull_plating",
            Tag.Solar => "sun_sail",
            Tag.Garden => "moss_reactor",
            Tag.Drone => "seed_drone",
            _ => "bridge",
        };
        return Load($"res://art/icons/pieces/{fallback}");
    }

    public static Texture2D? EffectIcon(Effect effect)
    {
        string name = effect switch
        {
            DamageEffect => "damage",
            BlockEffect => "block",
            GainEnergyEffect => "energy",
            DrawEffect => "draw",
            RepairEffect => "repair",
            FocusEffect => "focus",
            GrowEffect => "grow",
            _ => "energy",
        };
        return Load($"res://art/icons/effects/{name}");
    }

    public static Texture2D? IntentIcon(IntentKind kind) => kind switch
    {
        IntentKind.Attack => Load("res://art/icons/effects/damage"),
        IntentKind.HeavyAttack => Load("res://art/icons/intents/heavy_attack"),
        IntentKind.Block => Load("res://art/icons/effects/block"),
        _ => null,
    };

    /// <summary>The icon for a kind of node on the sector map, or null if it is missing.</summary>
    public static Texture2D? NodeIcon(NodeType type)
    {
        string name = type switch
        {
            NodeType.HardFight => "hard_fight",
            _ => type.ToString().ToLowerInvariant(),
        };
        return Load($"res://art/icons/nodes/{name}");
    }

    public static Texture2D? EnemyIcon(string enemyDefId) =>
        Load($"res://art/icons/enemies/{enemyDefId}") ?? Load("res://art/icons/enemies/feral_harvester");

    private static Texture2D? Load(string basePath, bool isIcon = true)
    {
        if (Cache.TryGetValue(basePath, out var cached))
        {
            return cached;
        }

        string? path = new[] { $"{basePath}.svg", $"{basePath}.png" }.FirstOrDefault(p => ResourceLoader.Exists(p));
        var texture = path is null ? null : GD.Load<Texture2D>(path);
        if (texture is not null && isIcon && texture.GetWidth() < MinIconWidth)
        {
            GD.PushWarning($"ArtLibrary: {path} is {texture.GetWidth()} px wide and will look soft. " +
                           "Import icons at svg/scale=3.0 with mipmaps on; see game/art/README.md.");
        }

        Cache[basePath] = texture;
        return texture;
    }
}
