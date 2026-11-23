using Bloom.Game.Autoload;
using Bloom.Game.Platform;
using Godot;

namespace Bloom.Game.Audio;

/// <summary>
/// Autoloaded after <see cref="MusicDirector"/>. It loads every sound effect in <c>res://audio/sfx/</c> at startup
/// and plays them for <see cref="Sfx.Play"/>, any number at once, on the Sfx bus. Each play is varied a little in
/// pitch and volume, and a cue with several files never plays the same one twice running, so a sound heard often
/// does not grate.
/// </summary>
/// <remarks>
/// <para>
/// The files directly in the folder are the base set. Each folder inside it is a ship's sound set: that ship's
/// own version of some of the cues. While the run's ship class names a set (<c>soundSet</c> in
/// <c>ships.json</c>), a cue plays from that set if the set has it, and from the base set otherwise.
/// </para>
/// <para>
/// The files are rendered by <c>tools/SfxGen</c> from the recipes in <c>tools/SfxGen/recipes</c> and
/// <c>tools/SfxGen/sets</c>, and are kept in the repo. How loud each cue is against the others is set here, in
/// <see cref="Tuning"/>; how loud all of them are is the player's <see cref="UserSettings.SfxVolume"/>.
/// </para>
/// </remarks>
public partial class SfxPlayer : Node
{
    public const string SfxDir = "res://audio/sfx";
    private const string SfxBus = "Sfx";

    /// <summary>How many sounds can play at once. A card that draws cards and hits can start eight within half a second.</summary>
    private const int Voices = 24;

    /// <summary>How long to wait at exit for the audio thread to mix once more; see <see cref="MusicDirector"/>.</summary>
    private const int ExitMixWaitMs = 200;

    /// <summary>How each cue is played. All are placeholders, to tune by ear.</summary>
    internal static class Tuning
    {
        /// <summary>
        /// A sound made of notes stays in tune with the music, so its pitch barely moves; a sound made of noise
        /// can move by half a semitone.
        /// </summary>
        public static Mix For(SfxCue cue) => cue switch
        {
            SfxCue.CardDraw => new Mix(PitchRange: 0.4f, MinGapMs: 30),
            SfxCue.HitLight or SfxCue.HitMedium or SfxCue.HitHeavy or SfxCue.HitBlocked => new Mix(PitchRange: 0.4f),
            SfxCue.ShipHit or SfxCue.ShipHitHeavy or SfxCue.ShipBlocked => new Mix(PitchRange: 0.3f),
            SfxCue.Shuffle or SfxCue.HandDiscard => new Mix(PitchRange: 0.4f),
            SfxCue.FightWon or SfxCue.FightLost => new Mix(PitchRange: 0f, VolumeRange: 0f),
            _ => new Mix(),
        };
    }

    /// <param name="VolumeDb">Added to the file's own level.</param>
    /// <param name="PitchRange">Each play is moved by up to this many semitones, up or down.</param>
    /// <param name="VolumeRange">Each play is moved by up to this many dB, up or down.</param>
    /// <param name="MinGapMs">A repeat of the same cue sooner than this is skipped.</param>
    internal readonly record struct Mix(float VolumeDb = 0f, float PitchRange = 0.1f, float VolumeRange = 1.5f, int MinGapMs = 40);

    private readonly Dictionary<SfxCue, List<AudioStream>> _streams = [];

    /// <summary>The ships' sets by name. Each holds only the cues that the set has a version of.</summary>
    private readonly Dictionary<string, Dictionary<SfxCue, List<AudioStream>>> _sets = new(StringComparer.Ordinal);
    private readonly Dictionary<SfxCue, int> _lastVariant = [];
    private readonly Dictionary<SfxCue, double> _lastPlayAt = [];

    /// <summary>The streams this node holds, disposed on exit so that Godot does not report them as still in use.</summary>
    private readonly List<Resource> _held = [];
    private AudioStreamPlayer? _player;
    private AudioStreamPlaybackPolyphonic? _playback;

    /// <summary>
    /// Whether the player was ever started. Kept here because the player, a child of this node, has already
    /// stopped itself by the time <see cref="_ExitTree"/> runs, so its own <c>Playing</c> says no.
    /// </summary>
    private bool _started;
    private bool _pausedByApp;

    /// <summary>
    /// Seconds of game time since startup. Sounds are mixed in game time, so gaps between them are judged in it
    /// too; a movie recording (<c>--write-movie</c>), which runs faster than a clock, then sounds like the game.
    /// </summary>
    private double _clock;

    internal static SfxPlayer? Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        ApplyVolume();

        int files = DirAccess.DirExistsAbsolute(SfxDir) ? Load(SfxDir, _streams) : 0;
        var missing = Enum.GetValues<SfxCue>().Where(cue => !_streams.ContainsKey(cue)).Select(cue => cue.FileName()).ToList();
        GD.Print(files == 0
            ? $"SfxPlayer: no sound effects in {SfxDir}; run tools/sfx.sh render, then import."
            : $"SfxPlayer: {_streams.Count} cues from {files} files; {(missing.Count == 0 ? "none missing" : "no file for " + string.Join(", ", missing))}.");
        if (files == 0)
        {
            return;
        }

        LoadSets();

        var polyphonic = new AudioStreamPolyphonic { Polyphony = Voices };
        _held.Add(polyphonic);
        _player = new AudioStreamPlayer { Stream = polyphonic, Bus = SfxBus, Name = "Sfx" };
        AddChild(_player);
    }

    public override void _Process(double delta) => _clock += delta;

    /// <summary>Stops the sounds and releases the streams as the game quits, for the reason given in <see cref="MusicDirector"/>.</summary>
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (_started)
        {
            _player?.Stop();
            OS.DelayMsec(ExitMixWaitMs);
        }

        // The playback's C# wrapper keeps it, and the last sound of each of its voices, alive until it is disposed.
        _playback?.Dispose();
        _playback = null;
        foreach (var resource in _held)
        {
            resource.Dispose();
        }

        _held.Clear();
        _streams.Clear();
        _sets.Clear();
    }

    /// <summary>On a phone, sounds stop while the game is in the background.</summary>
    public override void _Notification(int what)
    {
        if (_player is not { } player)
        {
            return;
        }

        if (what == NotificationApplicationPaused && player.Playing && !player.StreamPaused)
        {
            player.StreamPaused = true;
            _pausedByApp = true;
        }
        else if (what == NotificationApplicationResumed && _pausedByApp)
        {
            player.StreamPaused = false;
            _pausedByApp = false;
        }
    }

    /// <summary>
    /// The name of the file that <see cref="Play"/> would play for the cue now, without its extension or its
    /// variant: <c>heliotrope/card-play</c> when the run's ship has its own version, and <c>card-play</c> otherwise.
    /// </summary>
    internal string NameOf(SfxCue cue) =>
        ShipSet() is { } set && _sets.TryGetValue(set, out var own) && own.ContainsKey(cue) ? $"{set}/{cue.FileName()}" : cue.FileName();

    internal void Play(SfxCue cue, float pitch, float volumeDb)
    {
        if (_player is not { } player || _pausedByApp || StreamsOf(cue) is not { } variants)
        {
            return;
        }

        var mix = Tuning.For(cue);
        if (_lastPlayAt.TryGetValue(cue, out double last) && (_clock - last) * 1000 < mix.MinGapMs)
        {
            return;
        }

        _lastPlayAt[cue] = _clock;

        int variant = 0;
        if (variants.Count > 1)
        {
            // Any variant but the one that played last.
            variant = _lastVariant.TryGetValue(cue, out int before)
                ? (before + 1 + (int)(GD.Randi() % (uint)(variants.Count - 1))) % variants.Count
                : (int)(GD.Randi() % (uint)variants.Count);
            _lastVariant[cue] = variant;
        }

        // The player runs all the time once started: its one stream mixes whatever sounds are playing.
        if (!player.Playing || _playback is null)
        {
            player.Play();
            _playback?.Dispose();
            _playback = player.GetStreamPlayback() as AudioStreamPlaybackPolyphonic;
            _started = true;
        }

        float semitones = (float)GD.RandRange(-mix.PitchRange, mix.PitchRange);
        float db = mix.VolumeDb + volumeDb + (float)GD.RandRange(-mix.VolumeRange, mix.VolumeRange);
        _playback?.PlayStream(variants[variant], 0f, db, pitch * Mathf.Pow(2f, semitones / 12f), AudioServer.PlaybackType.Default, SfxBus);
    }

    /// <summary>
    /// The sound set of the ship in the current run, or null when there is no run or its ship uses only the base
    /// set. It is looked up at each play, so a new run with another ship changes the sounds at once.
    /// </summary>
    private static string? ShipSet() =>
        GameRoot.Instance is { Session: { } session, Content: { } content }
            ? content.Ships.FirstOrDefault(ship => ship.Id == session.State.ShipClassId)?.SoundSet
            : null;

    /// <summary>The files to choose from for a cue: the ship's own if its set has the cue, or else the base set's.</summary>
    private List<AudioStream>? StreamsOf(SfxCue cue) =>
        ShipSet() is { } set && _sets.TryGetValue(set, out var own) && own.TryGetValue(cue, out var variants)
            ? variants
            : _streams.GetValueOrDefault(cue);

    /// <summary>
    /// Loads each folder inside <see cref="SfxDir"/> as a ship's sound set, and warns about a ship class that
    /// names a set with no folder, since that ship would play only the base set.
    /// </summary>
    private void LoadSets()
    {
        // ResourceLoader lists a folder with a slash after its name.
        var folders = ResourceLoader.ListDirectory(SfxDir).Where(f => f.EndsWith('/')).Select(f => f.TrimEnd('/')).ToList();
        folders.Sort(StringComparer.Ordinal);
        foreach (string name in folders)
        {
            var cues = new Dictionary<SfxCue, List<AudioStream>>();
            int files = Load($"{SfxDir}/{name}", cues);
            if (files > 0)
            {
                _sets[name] = cues;
                GD.Print($"SfxPlayer: set '{name}' has {cues.Count} cues from {files} files.");
            }
        }

        foreach (var ship in GameRoot.Instance?.Content?.Ships ?? [])
        {
            if (ship.SoundSet is { } set && !_sets.ContainsKey(set))
            {
                GD.PushWarning($"SfxPlayer: the ship class '{ship.Id}' names the sound set '{set}', but {SfxDir}/{set} holds no sounds. It plays the base set.");
            }
        }
    }

    /// <summary>Loads every sound file in a folder that belongs to a cue, and returns how many there were.</summary>
    private int Load(string dir, Dictionary<SfxCue, List<AudioStream>> into)
    {
        var files = ResourceLoader.ListDirectory(dir).Where(f => f.EndsWith(".wav", StringComparison.Ordinal)).ToList();
        files.Sort(StringComparer.Ordinal);
        int loaded = 0;
        foreach (string file in files)
        {
            if (SfxCueNames.ForFile(file) is not { } cue)
            {
                GD.PushWarning($"SfxPlayer: {dir}/{file} belongs to no cue in SfxCue.");
                continue;
            }

            if (ResourceLoader.Load<AudioStream>($"{dir}/{file}") is { } stream)
            {
                _held.Add(stream);
                if (!into.TryGetValue(cue, out var variants))
                {
                    into[cue] = variants = [];
                }

                variants.Add(stream);
                loaded++;
            }
        }

        return loaded;
    }

    private static void ApplyVolume()
    {
        int bus = AudioServer.GetBusIndex(SfxBus);
        if (bus < 0)
        {
            GD.PushWarning($"SfxPlayer: there is no {SfxBus} bus (default_bus_layout.tres); the volume setting is ignored.");
            return;
        }

        float volume = UserSettings.SfxVolume;
        AudioServer.SetBusMute(bus, volume <= 0f);
        AudioServer.SetBusVolumeDb(bus, volume > 0f ? Mathf.LinearToDb(volume) : 0f);
    }
}
