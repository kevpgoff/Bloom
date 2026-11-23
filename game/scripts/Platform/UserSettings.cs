using Godot;

namespace Bloom.Game.Platform;

/// <summary>
/// The player's settings, kept in <c>user://settings.cfg</c> apart from the run. No screen shows them yet, so the
/// file is written with its defaults the first time, ready to be edited by hand:
/// <code>
/// [feedback]
/// shake_strength=1.0   ; 0 turns screen shake off, 1 is full strength
/// haptics=true
///
/// [audio]
/// music_volume=1.0     ; 0 turns the music off, 1 is full volume
/// sfx_volume=1.0       ; the same for the sound effects
/// </code>
/// </summary>
internal static class UserSettings
{
    private const string FilePath = "user://settings.cfg";
    private const string Feedback = "feedback";
    private const string ShakeKey = "shake_strength";
    private const string HapticsKey = "haptics";
    private const string Audio = "audio";
    private const string MusicVolumeKey = "music_volume";
    private const string SfxVolumeKey = "sfx_volume";

    private static bool _loaded;
    private static float _shakeStrength = 1f;
    private static bool _haptics = true;
    private static float _musicVolume = 1f;
    private static float _sfxVolume = 1f;

    /// <summary>How strongly the screen shakes, from 0 (never) to 1.</summary>
    public static float ShakeStrength
    {
        get
        {
            Load();
            return _shakeStrength;
        }
    }

    /// <summary>Whether the phone vibrates for feedback.</summary>
    public static bool Haptics
    {
        get
        {
            Load();
            return _haptics;
        }
    }

    /// <summary>How loud the music plays, from 0 (off) to 1.</summary>
    public static float MusicVolume
    {
        get
        {
            Load();
            return _musicVolume;
        }
    }

    /// <summary>How loud the sound effects play, from 0 (off) to 1.</summary>
    public static float SfxVolume
    {
        get
        {
            Load();
            return _sfxVolume;
        }
    }

    /// <summary>Reads the file, or writes it with the defaults. Called at startup so the file is there to edit.</summary>
    public static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var config = new ConfigFile();
        var error = config.Load(FilePath);
        if (error is not (Error.Ok or Error.FileNotFound))
        {
            // Keep a file the player may have mistyped; use the defaults until it is fixed.
            GD.PushWarning($"UserSettings: {FilePath} could not be read ({error}); using the defaults.");
            return;
        }

        _shakeStrength = Mathf.Clamp(config.GetValue(Feedback, ShakeKey, 1f).AsSingle(), 0f, 1f);
        _haptics = config.GetValue(Feedback, HapticsKey, true).AsBool();
        _musicVolume = Mathf.Clamp(config.GetValue(Audio, MusicVolumeKey, 1f).AsSingle(), 0f, 1f);
        _sfxVolume = Mathf.Clamp(config.GetValue(Audio, SfxVolumeKey, 1f).AsSingle(), 0f, 1f);
        if (config.HasSectionKey(Feedback, ShakeKey) && config.HasSectionKey(Feedback, HapticsKey) &&
            config.HasSectionKey(Audio, MusicVolumeKey) && config.HasSectionKey(Audio, SfxVolumeKey))
        {
            return;
        }

        // Write any missing setting, so the file shows everything that can be changed.
        config.SetValue(Feedback, ShakeKey, _shakeStrength);
        config.SetValue(Feedback, HapticsKey, _haptics);
        config.SetValue(Audio, MusicVolumeKey, _musicVolume);
        config.SetValue(Audio, SfxVolumeKey, _sfxVolume);
        if (config.Save(FilePath) is var saved and not Error.Ok)
        {
            GD.PushWarning($"UserSettings: could not write {FilePath} ({saved}).");
        }
    }
}
