using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Platform;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// The shell that hosts one screen at a time: the one for the run's phase, or the Fitting screen when the
/// player opens the ship from the map or a dock (<see cref="GameRoot.ShipOpen"/>). Screens only draw state and
/// forward input; game rules belong in Bloom.Core. After a won fight the combat screen stays up a moment
/// (<see cref="CombatScreen.TakeExitHold"/>), so the last hit can play before the Reward screen.
/// </summary>
public partial class MainScreen : Control
{
    private const string CombatScene = "res://scenes/Combat.tscn";
    private const string RewardScene = "res://scenes/Reward.tscn";
    private const string FittingScene = "res://scenes/Fitting.tscn";
    private const string MapScene = "res://scenes/Map.tscn";
    private const string GroveScene = "res://scenes/Grove.tscn";
    private const string DockScene = "res://scenes/Dock.tscn";
    private const string ShipyardScene = "res://scenes/Shipyard.tscn";

    /// <summary>The layout's margin from the edges of a desktop window, as in Main.tscn.</summary>
    private const float EdgeMargin = 16f;

    private Control _layout = null!;
    private Control _host = null!;
    private Label _status = null!;
    private string? _shownScene;
    private bool _swapQueued;
    private bool _holding;
    private Insets _loggedInsets;

    public override void _Ready()
    {
        Theme = BloomTheme.Build();
        _layout = GetNode<Control>("Layout");
        _host = GetNode<Control>("Layout/ScreenHost");
        _status = GetNode<Label>("Layout/Status");
        _status.Text = GameRoot.Instance.Status;
        GameRoot.Instance.Changed += QueueSwap;
        GetViewport().SizeChanged += FitToScreen;
        FitToScreen();
        ShowScreenForPhase();
    }

    public override void _ExitTree()
    {
        GameRoot.Instance.Changed -= QueueSwap;
        GetViewport().SizeChanged -= FitToScreen;
    }

    /// <summary>
    /// Keeps every screen clear of a phone's camera cutout and rounded corners (<see cref="ScreenInsets"/>).
    /// The backdrop still fills the whole screen.
    /// </summary>
    private void FitToScreen()
    {
        var insets = ScreenInsets.For(GetViewport());
        _layout.OffsetLeft = Mathf.Max(EdgeMargin, insets.Left);
        _layout.OffsetTop = Mathf.Max(EdgeMargin, insets.Top);
        _layout.OffsetRight = -Mathf.Max(EdgeMargin, insets.Right);
        _layout.OffsetBottom = -Mathf.Max(EdgeMargin, insets.Bottom);

        if (insets != _loggedInsets)
        {
            _loggedInsets = insets;
            GD.Print($"MainScreen: screen insets left {insets.Left:0}, top {insets.Top:0}, right {insets.Right:0}, bottom {insets.Bottom:0} (viewport {GetViewport().GetVisibleRect().Size}).");
        }
    }

    private static string SceneFor(RunPhase phase) => phase switch
    {
        RunPhase.Map => GameRoot.Instance.ShipOpen ? FittingScene : MapScene,
        RunPhase.Prepare => FittingScene,
        RunPhase.Reward => RewardScene,
        RunPhase.Grove => GroveScene,
        RunPhase.Dock => GameRoot.Instance.ShipOpen ? FittingScene : DockScene,
        RunPhase.Shipyard => ShipyardScene,
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

    private void EndHold()
    {
        _holding = false;
        ShowScreenForPhase();
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
        if (scene == _shownScene || _holding)
        {
            return;
        }

        // The timer checks the phase again when it fires, so whatever the run does meanwhile still shows.
        if (_host.GetChildCount() > 0 && _host.GetChild(0) is CombatScreen combat && combat.TakeExitHold() is > 0f and var hold)
        {
            _holding = true;
            GetTree().CreateTimer(hold).Timeout += EndHold;
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
