using Bloom.Core.Content;
using Bloom.Core.Run;
using Bloom.Core.Save;
using Bloom.Game.Platform;
using Godot;

namespace Bloom.Game.Autoload;

/// <summary>
/// Autoloaded singleton that owns the content and the current run. Screens read from it and send
/// every player action through <see cref="Act"/>, which runs it in <see cref="RunSession.Do(Action{RunSession})"/>
/// (so it is saved) and then raises <see cref="Changed"/>.
/// </summary>
public partial class GameRoot : Node
{
    public const string DataDir = "res://data";
    public const string StartingShip = "seedling";

    private const string UnreadableSaveCopy = "user://run.unreadable.json";

    private readonly GodotSaveStore _store = new();

    public static GameRoot Instance { get; private set; } = null!;

    public ContentDb? Content { get; private set; }

    public RunSession? Session { get; private set; }

    /// <summary>One line for the status label, shown when no run could be started.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Raised after every action and after a new run starts. Screens redraw on it.</summary>
    public event Action? Changed;

    public override void _Ready()
    {
        Instance = this;
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

        try
        {
            Session = RunSession.TryResume(Content, _store);
        }
        catch (SaveException e)
        {
            // A new run starts right away and overwrites the file, so keep a copy of it first.
            _store.CopyTo(UnreadableSaveCopy);
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

    /// <summary>A new run opens between fights, on the Fitting screen.</summary>
    public RunSession StartNewRun()
    {
        var content = Content ?? throw new InvalidOperationException("Content is not loaded.");

        // The only non-deterministic step: picking the seed. Everything after it comes from the run's own RNG.
        ulong seed = ((ulong)GD.Randi() << 32) | GD.Randi();
        Session = RunSession.StartNew(content, _store, StartingShip, seed);
        Changed?.Invoke();
        return Session;
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

        Changed?.Invoke();
        return done;
    }
}
