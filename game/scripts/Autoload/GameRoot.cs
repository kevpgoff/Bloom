using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Run;
using Bloom.Core.Save;
using Bloom.Game.Dev;
using Bloom.Game.Platform;
using Godot;

namespace Bloom.Game.Autoload;

/// <summary>
/// Autoloaded singleton that owns the content and the current run. Screens read from it and send
/// every player action through <see cref="Act"/>, which runs it in <see cref="RunSession.Do(Action{RunSession})"/>
/// (so it is saved) and then raises <see cref="CombatEvents"/> and <see cref="Changed"/>.
/// </summary>
/// <remarks>
/// With <c>-- --fx-demo</c> in a debug build, the run is kept in memory with a fixed seed and an
/// <see cref="FxDemo"/> plays it, so the saved run is never touched.
/// </remarks>
public partial class GameRoot : Node
{
    public const string DataDir = "res://data";
    public const string StartingShip = "seedling";

    private const string UnreadableSaveCopy = "user://run.unreadable.json";

    private const ulong DemoSeed = 7;

    private readonly GodotSaveStore _fileStore = new();
    private ISaveStore _store = null!;

    public static GameRoot Instance { get; private set; } = null!;

    public ContentDb? Content { get; private set; }

    public RunSession? Session { get; private set; }

    /// <summary>One line for the status label, shown when no run could be started.</summary>
    public string Status { get; private set; } = "";

    /// <summary>
    /// Whether the Fitting screen is open over the sector map or a dock. It is only which screen shows, not
    /// part of the run, so it is not saved: a resumed run opens on the map or the dock.
    /// </summary>
    public bool ShipOpen { get; private set; }

    /// <summary>Raised after every action and after a new run starts. Screens redraw on it.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised after an action that changed a fight, with what happened in order (<see cref="RunSession.LastEvents"/>),
    /// before <see cref="Changed"/>. The combat screen plays them as feedback.
    /// </summary>
    public event Action<IReadOnlyList<CombatEvent>>? CombatEvents;

    public override void _Ready()
    {
        Instance = this;
        UserSettings.Load();
        bool demo = FxDemo.IsRequested;
        _store = demo ? new MemorySaveStore() : _fileStore;
        try
        {
            Content = GodotContentSource.Load(DataDir);
        }
        catch (ContentException e)
        {
            Status = "Content failed to load. See the output log.";
            GD.PushError(e.Message);
            return;
        }

        Status = $"Content loaded: pieces {Content.Pieces.Count}, cards {Content.Cards.Count}, " +
                 $"enemies {Content.Enemies.Count}, ship classes {Content.Ships.Count}.";
        GD.Print($"GameRoot: {Status}");

        if (demo)
        {
            Session = RunSession.StartNew(Content, _store, StartingShip, DemoSeed);
            AddChild(new FxDemo());
            GD.Print("GameRoot: FX demo; the run is kept in memory and not saved.");
            return;
        }

        try
        {
            Session = RunSession.TryResume(Content, _store);
        }
        catch (SaveException e)
        {
            // A new run starts right away and overwrites the file, so keep a copy of it first.
            _fileStore.CopyTo(UnreadableSaveCopy);
            GD.PushWarning($"GameRoot: the saved run could not be read; a copy is at {UnreadableSaveCopy}. {e.Message}");
        }

        if (Session is not null)
        {
            GD.Print("GameRoot: resumed the saved run.");
        }
        else
        {
            StartNewRun();
            GD.Print("GameRoot: started a new run.");
        }
    }

    /// <summary>A new run opens on the map of its first sector.</summary>
    public RunSession StartNewRun()
    {
        var content = Content ?? throw new InvalidOperationException("Content is not loaded.");
        ShipOpen = false;

        // The only non-deterministic step: picking the seed. Everything after it comes from the run's own RNG.
        ulong seed = ((ulong)GD.Randi() << 32) | GD.Randi();
        Session = RunSession.StartNew(content, _store, StartingShip, seed);
        Changed?.Invoke();
        return Session;
    }

    /// <summary>Opens or closes the Fitting screen over the map or a dock. Changes no state, so nothing is saved.</summary>
    public void ShowShip(bool open)
    {
        if (ShipOpen != open)
        {
            ShipOpen = open;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Runs one player action through the session, which saves after it, then raises <see cref="Changed"/>.
    /// Returns false when the rules reject the action; they reject it before changing anything, so the
    /// state is still valid.
    /// </summary>
    public bool Act(Action<RunSession> action)
    {
        if (Session is not { } session)
        {
            return false;
        }

        bool done = true;
        try
        {
            session.Do(action);
        }
        catch (InvalidOperationException e)
        {
            GD.PushWarning($"GameRoot: action rejected: {e.Message}");
            done = false;
        }

        if (done && session.LastEvents.Count > 0)
        {
            CombatEvents?.Invoke(session.LastEvents);
        }

        Changed?.Invoke();
        return done;
    }
}
