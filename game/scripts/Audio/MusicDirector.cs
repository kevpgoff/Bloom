using Bloom.Core.Combat;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Platform;
using Godot;

namespace Bloom.Game.Audio;

/// <summary>
/// Autoloaded after <see cref="GameRoot"/>. It plays music that follows the run, in four moods:
/// <list type="bullet">
/// <item>voyage: every screen outside a fight;</item>
/// <item>fight: a skirmish, a hard fight, or a fight an event started;</item>
/// <item>boss: a fight against an enemy from the boss pool;</item>
/// <item>stings: one short piece when a run is won and one when it is lost.</item>
/// </list>
/// Each of the first three moods is a shuffled, looping playlist with its own player. When the mood changes, the old
/// player fades out and is paused, so the voyage music resumes where it stopped after a fight.
/// </summary>
/// <remarks>
/// The music files are not in the repo: <c>tools/import-music.sh</c> builds them into <c>res://audio/music/</c>
/// (see <c>game/audio/CREDITS.md</c>). Without them, the game runs in silence.
/// </remarks>
public partial class MusicDirector : Node
{
    public const string MusicDir = "res://audio/music";
    private const string MusicBus = "Music";

    /// <summary>
    /// How long to wait at exit for the audio thread to mix once more (see <see cref="_ExitTree"/>). Two of the
    /// blocks mixed by Godot's dummy driver, which headless runs use: 4096 frames, about 93 ms at 44.1 kHz.
    /// Real audio drivers mix far more often.
    /// </summary>
    private const int ExitMixWaitMs = 200;

    /// <summary>Fade times in seconds. Placeholders, to tune by ear.</summary>
    public static class Tuning
    {
        /// <summary>How long the music of the mood being left takes to fade out.</summary>
        public const float FadeOut = 1.0f;

        /// <summary>How long the music of the new mood takes to fade in.</summary>
        public const float FadeIn = 1.5f;
    }

    private enum Mood
    {
        None,
        Voyage,
        Fight,
        Boss,
        Won,
        Lost,
    }

    private static readonly Mood[] AllMoods = [Mood.Voyage, Mood.Fight, Mood.Boss, Mood.Won, Mood.Lost];

    /// <summary>One player per mood, indexed by <see cref="Mood"/>. A mood without music has none.</summary>
    private readonly AudioStreamPlayer?[] _players = new AudioStreamPlayer?[AllMoods.Length + 1];
    private readonly Tween?[] _fades = new Tween?[AllMoods.Length + 1];

    /// <summary>
    /// Whether each mood's player has a playback, playing or paused. Kept here because Godot doesn't count a paused
    /// player as playing, and a paused player must resume rather than start over.
    /// </summary>
    private readonly bool[] _started = new bool[AllMoods.Length + 1];
    private readonly List<AudioStreamPlayer> _pausedByApp = [];

    /// <summary>
    /// Every stream and playlist this node holds. Their C# wrappers keep them alive until they are disposed, so they
    /// are disposed on exit; otherwise Godot reports them as resources still in use.
    /// </summary>
    private readonly List<Resource> _held = [];
    private Mood _mood = Mood.None;
    private RunSession? _session;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ApplyVolume();

        var loop = new Dictionary<Mood, int>();
        foreach (var (mood, folder) in new[] { (Mood.Voyage, "voyage"), (Mood.Fight, "fight"), (Mood.Boss, "boss") })
        {
            var streams = LoadFolder(folder);
            _held.AddRange(streams);
            loop[mood] = streams.Count;
            if (streams.Count > 0)
            {
                var playlist = new AudioStreamPlaylist { Loop = true, Shuffle = true, StreamCount = streams.Count };
                _held.Add(playlist);
                for (int i = 0; i < streams.Count; i++)
                {
                    playlist.SetListStream(i, streams[i]);
                }

                AddPlayer(mood, playlist);
            }
        }

        int stings = 0;
        foreach (var (mood, name) in new[] { (Mood.Won, "won"), (Mood.Lost, "lost") })
        {
            string path = $"{MusicDir}/stings/{name}.ogg";
            if (ResourceLoader.Exists(path) && ResourceLoader.Load<AudioStream>(path) is { } sting)
            {
                _held.Add(sting);
                AddPlayer(mood, sting);
                stings++;
            }
        }

        if (loop.Values.Sum() + stings == 0)
        {
            GD.Print($"MusicDirector: no music in {MusicDir}; run tools/import-music.sh to build it.");
            return;
        }

        GD.Print($"MusicDirector: voyage {loop[Mood.Voyage]}, fight {loop[Mood.Fight]}, boss {loop[Mood.Boss]}, stings {stings}.");
        GameRoot.Instance.Changed += Follow;

        // A run that is resumed already won or lost starts in silence; its sting played when it ended.
        _session = GameRoot.Instance.Session;
        var start = MoodFor(_session);
        _mood = start is Mood.Won or Mood.Lost ? start : Mood.None;
        Follow();
    }

    /// <summary>
    /// Stops the music and releases the streams as the game quits. Godot frees a stopped playback only after the
    /// audio thread has mixed once more, and its audio server doesn't free playbacks that are still playing or paused
    /// when it shuts down. Either way, Godot would report them as leaked. So the players are stopped, then the game
    /// waits briefly for the audio thread to mix.
    /// </summary>
    public override void _ExitTree()
    {
        GameRoot.Instance.Changed -= Follow;
        bool stopped = false;
        foreach (var mood in AllMoods)
        {
            if (_started[(int)mood] && _players[(int)mood] is { } player)
            {
                player.Stop();
                stopped = true;
            }
        }

        if (stopped)
        {
            OS.DelayMsec(ExitMixWaitMs);
        }

        foreach (var resource in _held)
        {
            resource.Dispose();
        }

        _held.Clear();
    }

    /// <summary>On a phone, the music stops while the game is in the background.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationPaused)
        {
            foreach (var mood in AllMoods)
            {
                if (_started[(int)mood] && _players[(int)mood] is { StreamPaused: false } player)
                {
                    player.StreamPaused = true;
                    _pausedByApp.Add(player);
                }
            }
        }
        else if (what == NotificationApplicationResumed)
        {
            foreach (var player in _pausedByApp)
            {
                player.StreamPaused = false;
            }

            _pausedByApp.Clear();
        }
    }

    private static Mood MoodFor(RunSession? session) => session?.State switch
    {
        null => Mood.None,
        { Phase: RunPhase.Fight, Combat: { } combat } =>
            session.Content.Enemy(combat.Enemy.DefId).Pool == EnemyPool.Boss ? Mood.Boss : Mood.Fight,
        { Phase: RunPhase.Won } => Mood.Won,
        { Phase: RunPhase.Lost } => Mood.Lost,
        _ => Mood.Voyage,
    };

    private static List<AudioStream> LoadFolder(string folder)
    {
        string dir = $"{MusicDir}/{folder}";
        var streams = new List<AudioStream>();
        if (!DirAccess.DirExistsAbsolute(dir))
        {
            return streams;
        }

        var files = ResourceLoader.ListDirectory(dir).Where(f => f.EndsWith(".ogg", StringComparison.Ordinal)).ToList();
        files.Sort(StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (ResourceLoader.Load<AudioStream>($"{dir}/{file}") is { } stream)
            {
                streams.Add(stream);
            }
        }

        return streams;
    }

    private static void ApplyVolume()
    {
        int bus = AudioServer.GetBusIndex(MusicBus);
        if (bus < 0)
        {
            GD.PushWarning($"MusicDirector: there is no {MusicBus} bus (default_bus_layout.tres); the volume setting is ignored.");
            return;
        }

        float volume = UserSettings.MusicVolume;
        AudioServer.SetBusMute(bus, volume <= 0f);
        AudioServer.SetBusVolumeDb(bus, volume > 0f ? Mathf.LinearToDb(volume) : 0f);
    }

    private void AddPlayer(Mood mood, AudioStream stream)
    {
        var player = new AudioStreamPlayer { Stream = stream, Bus = MusicBus, Name = $"{mood}Music" };
        AddChild(player);
        _players[(int)mood] = player;
    }

    /// <summary>Moves the music to the mood of the run's current phase. Runs after every action and new run.</summary>
    private void Follow()
    {
        // A new run starts its music from the beginning, on a new shuffle.
        if (GameRoot.Instance.Session != _session)
        {
            _session = GameRoot.Instance.Session;
            foreach (var mood in AllMoods)
            {
                if (mood is not (Mood.Won or Mood.Lost) && _players[(int)mood] is { } player)
                {
                    _fades[(int)mood]?.Kill();
                    player.Stop();
                    player.StreamPaused = false;
                    _pausedByApp.Remove(player);
                    _started[(int)mood] = false;
                }
            }
        }

        var next = MoodFor(_session);
        if (next == _mood)
        {
            return;
        }

        _mood = next;
        GD.Print(next switch
        {
            Mood.Won or Mood.Lost => $"MusicDirector: the {next.ToString().ToLowerInvariant()} sting.",
            Mood.None => "MusicDirector: no music.",
            _ => $"MusicDirector: {next.ToString().ToLowerInvariant()} music.",
        });
        foreach (var mood in AllMoods)
        {
            if (mood != next)
            {
                FadeOut(mood);
            }
        }

        FadeIn(next);
    }

    private void FadeIn(Mood mood)
    {
        if (mood == Mood.None || _players[(int)mood] is not { } player)
        {
            return;
        }

        _fades[(int)mood]?.Kill();
        _pausedByApp.Remove(player);
        player.StreamPaused = false;
        if (mood is Mood.Won or Mood.Lost)
        {
            // A sting plays once, from its start, at full volume.
            player.VolumeLinear = 1f;
            player.Play();
            _started[(int)mood] = true;
            return;
        }

        if (!_started[(int)mood])
        {
            player.VolumeLinear = 0f;
            player.Play();
            _started[(int)mood] = true;
        }

        var fade = CreateTween();
        fade.TweenProperty(player, "volume_linear", 1f, Tuning.FadeIn);
        _fades[(int)mood] = fade;
    }

    private void FadeOut(Mood mood)
    {
        // A player that is already paused has nothing to fade.
        if (!_started[(int)mood] || _players[(int)mood] is not { StreamPaused: false } player)
        {
            return;
        }

        _fades[(int)mood]?.Kill();
        var fade = CreateTween();
        fade.TweenProperty(player, "volume_linear", 0f, Tuning.FadeOut);
        if (mood is Mood.Won or Mood.Lost)
        {
            fade.TweenCallback(Callable.From(() =>
            {
                player.Stop();
                _started[(int)mood] = false;
            }));
        }
        else
        {
            fade.TweenCallback(Callable.From(() => player.StreamPaused = true));
        }

        _fades[(int)mood] = fade;
    }
}
