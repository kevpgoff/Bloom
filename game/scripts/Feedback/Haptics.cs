using Bloom.Game.Platform;
using Godot;

namespace Bloom.Game.Feedback;

/// <summary>How strong a haptic pulse is. Each kind is one short vibration; patterns are timed by the caller.</summary>
internal enum HapticKind
{
    /// <summary>A barely-there click: a card played, a card drawn, energy or focus gained.</summary>
    Tick,

    /// <summary>A small hit, or block gained.</summary>
    Light,

    /// <summary>A solid hit, or a piece repaired.</summary>
    Medium,

    /// <summary>The ship takes a big hit, or a piece is disabled.</summary>
    Heavy,
}

/// <summary>
/// Vibrates the phone through <see cref="Input.VibrateHandheld"/>, which works on Android (with the VIBRATE
/// permission in the export preset), iOS and the web, and does nothing on desktop. Pulses are skipped while
/// <see cref="UserSettings.Haptics"/> is off.
/// </summary>
internal static class Haptics
{
    private static ulong _busyUntilMs;
    private static float _busyAmplitude;

    /// <summary>Prints each pulse, so the haptics can be checked on desktop. The FX demo turns it on.</summary>
    public static bool LogPulses { get; set; }

    public static void Play(HapticKind kind)
    {
        var (ms, amplitude) = kind switch
        {
            HapticKind.Tick => (8, 0.25f),
            HapticKind.Light => (14, 0.45f),
            HapticKind.Medium => (24, 0.7f),
            _ => (40, 1f),
        };
        Play(ms, amplitude);
    }

    /// <summary>
    /// One vibration. A new vibration cuts off the one in progress on Android, so a weaker pulse is skipped
    /// while a stronger one is still running. That is judged in real time, as the motor runs; a run faster than
    /// real time (<c>--fixed-fps</c> without a display) skips more pulses than a phone would.
    /// </summary>
    public static void Play(int milliseconds, float amplitude)
    {
        if (!UserSettings.Haptics)
        {
            return;
        }

        ulong now = Time.GetTicksMsec();
        if (now < _busyUntilMs && amplitude < _busyAmplitude)
        {
            return;
        }

        _busyUntilMs = now + (ulong)milliseconds;
        _busyAmplitude = amplitude;
        Input.VibrateHandheld(milliseconds, amplitude);
        if (LogPulses)
        {
            GD.Print($"Haptics: {milliseconds} ms at {amplitude:0.00}");
        }
    }
}
