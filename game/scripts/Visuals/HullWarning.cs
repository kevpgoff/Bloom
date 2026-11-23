using Bloom.Core.Run;
using Bloom.Game.Screens;
using Godot;

namespace Bloom.Game.Visuals;

/// <summary>
/// How worn the ship's hull is, as a level from 0 to 1, and the colors that follow it. Nothing here is a rule:
/// it only changes how a worn hull looks.
/// </summary>
/// <remarks>
/// <para>
/// The warning is meant to be felt rather than read, so it has no text and nothing in it changes suddenly. As the
/// level rises, the hull's numbers warm from white to amber and then to coral (<see cref="TextColor"/>). The
/// backdrop's horizon and edges warm and start to breathe slowly (<see cref="BackdropMotes"/>). In a fight, the
/// ship's open deck darkens toward red like emergency lighting, and its hologram now and then flickers
/// (<see cref="ShipGridView"/>).
/// </para>
/// <para>All the numbers here and in those two classes are placeholders, to be tuned by eye.</para>
/// </remarks>
internal static class HullWarning
{
    /// <summary>At this share of the maximum hull or more, the level is 0 and nothing shows.</summary>
    public const float StartsAt = 0.5f;

    /// <summary>At this share or less, the level is 1 and everything shows at full strength.</summary>
    public const float FullAt = 0.2f;

    /// <summary>How long the backdrop and the ship take to follow a change of level, in seconds for the whole range.</summary>
    public const float EaseTime = 2.5f;

    /// <summary>Amber: the color of the warning halfway up.</summary>
    public static readonly Color Warm = Palette.Heavy;

    /// <summary>Coral: the color of the warning at full strength, which is also the color of damage to the ship.</summary>
    public static readonly Color Hot = Palette.Refused;

    /// <summary>The open deck under emergency lighting: a dark ember, as dark as the usual deck.</summary>
    private static readonly Color EmergencyDeck = new(0.16f, 0.035f, 0.045f, 0.82f);

    /// <summary>
    /// 0 while the hull is at <see cref="StartsAt"/> of its maximum or more. It rises smoothly to 1 at
    /// <see cref="FullAt"/>, so it eases in and out of both ends.
    /// </summary>
    public static float Level(int hull, int maxHull)
    {
        if (maxHull <= 0)
        {
            return 0f;
        }

        float t = Mathf.Clamp((StartsAt - ((float)hull / maxHull)) / (StartsAt - FullAt), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    public static float Level(RunState run) => Level(run.Hull, run.MaxHull);

    /// <summary>The warning's color: amber as it starts, coral at full strength.</summary>
    public static Color Tint(float level) => Warm.Lerp(Hot, Mathf.Clamp(level, 0f, 1f));

    /// <summary>The color of the hull's numbers: white at level 0, amber halfway, and coral at full strength.</summary>
    public static Color TextColor(float level)
    {
        var warm = Warm.Lerp(Palette.Ink, 0.25f);
        var hot = Hot.Lerp(Palette.Ink, 0.12f);
        return level <= 0.5f ? Palette.Ink.Lerp(warm, level * 2f) : warm.Lerp(hot, (level - 0.5f) * 2f);
    }

    /// <summary>"Hull 23 / 60" as BBCode for a <see cref="RichTextLabel"/>, colored by <see cref="TextColor"/>.</summary>
    public static string HullBbcode(RunState run)
    {
        string text = $"Hull {run.Hull} / {run.MaxHull}";
        float level = Level(run);
        return level <= 0f ? text : $"[color=#{TextColor(level).ToHtml(false)}]{text}[/color]";
    }

    /// <summary>The color of the open deck between the ship's pieces.</summary>
    public static Color Deck(float level) => Palette.Deck.Lerp(EmergencyDeck, Mathf.Clamp(level, 0f, 1f) * 0.75f);
}
