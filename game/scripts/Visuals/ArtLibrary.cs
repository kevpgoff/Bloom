using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Pieces;
using Godot;

namespace Bloom.Game.Visuals;

/// <summary>
/// Loads and caches Option A vector icons and 9-slice UI frames from <c>res://art/</c>.
/// Supports both <c>.svg</c> and <c>.png</c> drop-in replacements; SVGs rasterize cleanly at
/// high DPI even before Godot Editor has generated <c>.import</c> files.
/// </summary>
internal static class ArtLibrary
{
    private const float IconSvgScale = 3f;
    private const float FrameSvgScale = 1f;
    private static readonly Dictionary<string, Texture2D?> Cache = [];

    public static Texture2D? CardFrame => Load("res://art/ui/card_frame", FrameSvgScale);

    public static Texture2D? PanelFrame => Load("res://art/ui/panel_frame", FrameSvgScale);

    public static Texture2D? ButtonFrame => Load("res://art/ui/button_frame", FrameSvgScale);

    public static Texture2D? EnergyBadge => Load("res://art/ui/energy_badge", IconSvgScale);

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

    public static Texture2D? EnemyIcon(string enemyDefId) =>
        Load($"res://art/icons/enemies/{enemyDefId}") ?? Load("res://art/icons/enemies/feral_harvester");

    private static Texture2D? Load(string basePath, float svgScale = IconSvgScale)
    {
        if (Cache.TryGetValue(basePath, out var cached))
        {
            return cached;
        }

        var texture = TryLoadTexture($"{basePath}.svg", svgScale) ?? TryLoadTexture($"{basePath}.png", svgScale);
        Cache[basePath] = texture;
        return texture;
    }

    private static Texture2D? TryLoadTexture(string path, float svgScale)
    {
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && Godot.FileAccess.FileExists(path))
        {
            byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
            if (bytes.Length > 0)
            {
                var image = new Image();
                if (image.LoadSvgFromBuffer(bytes, svgScale) == Error.Ok)
                {
                    image.GenerateMipmaps();
                    return ImageTexture.CreateFromImage(image);
                }
            }
        }

        if (ResourceLoader.Exists(path))
        {
            return GD.Load<Texture2D>(path);
        }

        if (Godot.FileAccess.FileExists(path))
        {
            var image = Image.LoadFromFile(path);
            if (image is not null)
            {
                image.GenerateMipmaps();
                return ImageTexture.CreateFromImage(image);
            }
        }

        return null;
    }
}
