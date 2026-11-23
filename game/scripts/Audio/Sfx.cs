using Godot;

namespace Bloom.Game.Audio;

/// <summary>
/// Plays a sound effect: <c>Sfx.Play(SfxCue.BlockGain)</c>. The call is made at the moment the sound should start,
/// usually beside the visual effect and the haptic pulse of the same event. <see cref="SfxPlayer"/> does the work;
/// without it, or without a file for the cue, the call does nothing.
/// </summary>
internal static class Sfx
{
    /// <summary>Prints each cue as it is asked for, so the sounds can be checked without hearing them. The FX demo turns it on.</summary>
    public static bool LogCues { get; set; }

    /// <param name="cue">Which sound.</param>
    /// <param name="pitch">A factor on the playback speed; 1 is the sound as rendered.</param>
    /// <param name="volumeDb">Added to the cue's own volume.</param>
    public static void Play(SfxCue cue, float pitch = 1f, float volumeDb = 0f)
    {
        if (LogCues)
        {
            GD.Print(pitch == 1f ? $"Sfx: {cue.FileName()}" : $"Sfx: {cue.FileName()} at pitch {pitch:0.00}");
        }

        SfxPlayer.Instance?.Play(cue, pitch, volumeDb);
    }
}
