using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// The shell that hosts one screen at a time: the one for the run's phase. Screens only draw state and
/// forward input; game rules belong in Bloom.Core.
/// </summary>
public partial class MainScreen : Control
{
    private const string CombatScene = "res://scenes/Combat.tscn";
    private const string RewardScene = "res://scenes/Reward.tscn";
    private const string FittingScene = "res://scenes/Fitting.tscn";

    private Control _host = null!;
    private Label _status = null!;
    private string? _shownScene;
    private bool _swapQueued;

    public override void _Ready()
    {
        _host = GetNode<Control>("Layout/ScreenHost");
        _status = GetNode<Label>("Layout/Status");
        _status.Text = GameRoot.Instance.Status;
        GameRoot.Instance.Changed += QueueSwap;
        ShowScreenForPhase();
    }

    public override void _ExitTree() => GameRoot.Instance.Changed -= QueueSwap;

    private static string SceneFor(RunPhase phase) => phase switch
    {
        RunPhase.Reward => RewardScene,
        RunPhase.Refit => FittingScene,
        _ => CombatScene, // the fight, and its end: defeat or the voyage complete
    };

    /// <summary>Swaps at the end of the frame, so a screen is never removed while it handles its own tap.</summary>
    private void QueueSwap()
    {
        if (_swapQueued)
        {
            return;
        }

        _swapQueued = true;
        Callable.From(() =>
        {
            _swapQueued = false;
            ShowScreenForPhase();
        }).CallDeferred();
    }

    private void ShowScreenForPhase()
    {
        // Without a run (the content failed to load), only the status message shows.
        _status.Visible = GameRoot.Instance.Session is null;
        if (GameRoot.Instance.Session is not { } session)
        {
            return;
        }

        string scene = SceneFor(session.State.Phase);
        if (scene == _shownScene)
        {
            return;
        }

        foreach (var child in _host.GetChildren())
        {
            _host.RemoveChild(child);
            child.QueueFree();
        }

        _host.AddChild(GD.Load<PackedScene>(scene).Instantiate());
        _shownScene = scene;
    }
}
