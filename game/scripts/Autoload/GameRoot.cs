using Bloom.Core.Content;
using Bloom.Core.Run;
using Bloom.Core.Save;
using Bloom.Game.Platform;
using Godot;

namespace Bloom.Game.Autoload;

/// <summary>
/// Autoloaded singleton that owns the content and the current run. Screens read from it and send
/// every player action through <see cref="Session"/>, which saves after each one.
/// </summary>
public partial class GameRoot : Node
{
    public const string DataDir = "res://data";
    public const string StartingShip = "seedling";

    private readonly GodotSaveStore _store = new();

    public static GameRoot Instance { get; private set; } = null!;

    public ContentDb? Content { get; private set; }

    public RunSession? Session { get; private set; }

    /// <summary>One line for the placeholder status label.</summary>
    public string Status { get; private set; } = "";

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
            // Keep the file so nothing is lost; a new run will overwrite it.
            GD.PushWarning($"GameRoot: the saved run could not be read and was ignored. {e.Message}");
        }

        if (Session is not null)
        {
            GD.Print("GameRoot: resumed the saved run.");
        }
    }

    public RunSession StartNewRun()
    {
        var content = Content ?? throw new InvalidOperationException("Content is not loaded.");

        // The only non-deterministic step: picking the seed. Everything after it comes from the run's own RNG.
        ulong seed = ((ulong)GD.Randi() << 32) | GD.Randi();
        Session = RunSession.StartNew(content, _store, StartingShip, seed);
        return Session;
    }
}
