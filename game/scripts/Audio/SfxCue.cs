using System.Text;

namespace Bloom.Game.Audio;

/// <summary>
/// Every sound effect the game can play. Each one is a file in <c>res://audio/sfx/</c> named after the member in
/// lowercase words joined by hyphens (<see cref="CardPlayWeapon"/> is <c>card-play-weapon.wav</c>), rendered by
/// <c>tools/SfxGen</c> from the recipe of the same name. A file named <c>card-draw_2.wav</c> is a second variant
/// of <see cref="CardDraw"/>. A file of the same name in a folder there, such as <c>heliotrope/</c>, is the
/// version of the cue for the ship class whose sound set has that name; see <see cref="SfxPlayer"/>.
/// </summary>
internal enum SfxCue
{
    /// <summary>A card from a crew piece, or from a piece with no tag, is played.</summary>
    CardPlay,

    /// <summary>A card is played, in the voice of its piece's first tag. One member per <see cref="Bloom.Core.Pieces.Tag"/>.</summary>
    CardPlayCore,
    CardPlayWeapon,
    CardPlayShield,
    CardPlaySolar,
    CardPlayGarden,
    CardPlayDrone,

    /// <summary>A card is dealt into the hand.</summary>
    CardDraw,

    /// <summary>The discard pile returns to the draw pile.</summary>
    Shuffle,

    /// <summary>The old hand sinks into the discard pile.</summary>
    HandDiscard,

    /// <summary>A new turn begins and energy refills.</summary>
    TurnStart,

    /// <summary>End Turn is pressed.</summary>
    EndTurn,

    /// <summary>The player's attack lands: under 10 damage, 10 or more, 20 or more.</summary>
    HitLight,
    HitMedium,
    HitHeavy,

    /// <summary>The enemy's block absorbs the whole attack.</summary>
    HitBlocked,

    FocusGain,

    /// <summary>Stored focus charges the attack that follows.</summary>
    FocusSpend,

    /// <summary>The reticle locks onto the enemy or one of its parts.</summary>
    TargetLock,

    /// <summary>One of a boss's parts goes dark.</summary>
    PartDisabled,

    BlockGain,

    EnergyGain,

    /// <summary>A disabled piece comes back online.</summary>
    Repair,

    /// <summary>A garden card grows.</summary>
    Grow,

    /// <summary>A piece's passive takes effect. What it does has its own sound, which follows.</summary>
    PassiveFire,

    /// <summary>The enemy raises block.</summary>
    EnemyBlock,

    /// <summary>The player's block absorbs the whole of an enemy's attack.</summary>
    ShipBlocked,

    /// <summary>The hull takes damage.</summary>
    ShipHit,

    /// <summary>A heavy attack, or one for 12 damage or more, gets through to the hull.</summary>
    ShipHitHeavy,

    /// <summary>A piece is disabled.</summary>
    PieceOffline,

    FightWon,

    FightLost,
}

internal static class SfxCueNames
{
    private static readonly Dictionary<string, SfxCue> ByName = Enum.GetValues<SfxCue>().ToDictionary(FileName);

    /// <summary>The cue's file name without its extension: <c>CardPlayWeapon</c> gives <c>card-play-weapon</c>.</summary>
    public static string FileName(this SfxCue cue)
    {
        string name = cue.ToString();
        var text = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                text.Append('-');
            }

            text.Append(char.ToLowerInvariant(name[i]));
        }

        return text.ToString();
    }

    /// <summary>The cue a sound file belongs to, or null if no cue has that name. A variant's "_2" is ignored.</summary>
    public static SfxCue? ForFile(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        int variant = name.LastIndexOf('_');
        if (variant >= 0)
        {
            name = name[..variant];
        }

        return ByName.TryGetValue(name, out var cue) ? cue : null;
    }
}
