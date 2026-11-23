using Bloom.Core.Map;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// The sector map between nodes: the whole map on a <see cref="MapView"/>, a button that sets course for
/// the tapped node and names it, and one that opens the ship for refitting. Only reachable nodes can be
/// picked. Choosing goes through <see cref="GameRoot.Act"/>; opening the ship only switches screens.
/// </summary>
/// <remarks>
/// Under the header are the voyage's seed, which a tap copies, and the Abandon button. Abandoning ends the
/// voyage for good, so it takes two presses: the first arms the button for <see cref="AbandonArmedSeconds"/>,
/// and only a second press in that time leaves for the hangar (<see cref="GameRoot.LeaveRun"/>).
/// A debug build (<see cref="GameRoot.TestTools"/>) also has a Hangar button, which sets the voyage aside for
/// testing without ending it (<see cref="GameRoot.ParkVoyage"/>). A test run has neither button: the row
/// above every screen ends it.
/// </remarks>
public partial class MapScreen : Control
{
    private const double AbandonArmedSeconds = 3;

    private MapView _map = null!;
    private Label _title = null!;
    private RichTextLabel _status = null!;
    private Button _ship = null!;
    private Button _go = null!;
    private Button _seed = null!;
    private Button _abandon = null!;
    private Godot.Timer _abandonTimer = null!;
    private bool _abandonArmed;
    private bool _seedCopied;
    private int? _selected;

    private static RunSession? Session => GameRoot.Instance.Session;

    public override void _Ready()
    {
        _map = GetNode<MapView>("%MapView");
        _title = GetNode<Label>("%Title");
        _status = GetNode<RichTextLabel>("%StatusLabel");
        _ship = GetNode<Button>("%ShipButton");
        _go = GetNode<Button>("%GoButton");

        _map.NodeTapped += Select;
        _ship.Pressed += () => GameRoot.Instance.ShowShip(true);
        _go.Pressed += Go;
        _seed = GetNode<Button>("%SeedButton");
        _seed.Pressed += CopySeed;
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            _seed.AddThemeColorOverride(state, Palette.Muted);
        }

        _abandon = GetNode<Button>("%AbandonButton");
        _abandon.Pressed += OnAbandonPressed;
        _abandonTimer = new Godot.Timer { OneShot = true, WaitTime = AbandonArmedSeconds };
        _abandonTimer.Timeout += () => ArmAbandon(false);
        AddChild(_abandonTimer);
        ArmAbandon(false);
        _abandon.Visible = !GameRoot.Instance.IsTest;
        var hangar = GetNode<Button>("%HangarButton");
        hangar.Visible = GameRoot.TestTools && !GameRoot.Instance.IsTest;
        hangar.Pressed += GameRoot.Instance.ParkVoyage;
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            hangar.AddThemeColorOverride(state, Palette.Muted);
        }

        GameRoot.Instance.Changed += Refresh;

        // With a single way on, it is chosen already; the player still confirms it.
        if (Session is { } session && session.Map.Reachable(session.State) is { Count: 1 } only)
        {
            _selected = only[0].Id;
        }

        Refresh();
        if (Session?.State is { } run)
        {
            var reachable = Session.Map.Reachable(run).Select(n => $"{n.Id} {n.Type}");
            GD.Print($"MapScreen: sector {run.Sector}, at node {run.CurrentNode?.Id.ToString() ?? "start"}, " +
                     $"{run.Map?.Nodes.Count ?? 0} nodes, can reach {string.Join(", ", reachable)}; hull {run.Hull}/{run.MaxHull}, salvage {run.Salvage}.");
        }
    }

    public override void _ExitTree() => GameRoot.Instance.Changed -= Refresh;

    private void Select(int nodeId)
    {
        if (Session is { } session && session.Map.Reachable(session.State).Any(n => n.Id == nodeId))
        {
            _selected = nodeId;
            Refresh();
        }
    }

    private void Refresh()
    {
        if (Session is not { State.Map: { } map } session)
        {
            return;
        }

        var run = session.State;
        var reachable = session.Map.Reachable(run);
        if (_selected is int id && !reachable.Any(n => n.Id == id))
        {
            _selected = null;
        }

        _title.Text = run.Sectors > 1 ? $"Sector {run.Sector} of {run.Sectors}" : "Sector map";
        _status.Text = $"{HullWarning.HullBbcode(run)}  ·  Salvage {run.Salvage}";
        _ship.Text = run.Cargo.Pieces.Count > 0 ? $"Ship · {run.Cargo.Pieces.Count} held" : "Ship";
        _go.Disabled = _selected is null;
        _go.Text = _selected is int chosen ? $"Set course · {NodeName(map.Node(chosen).Type)}" : "Set course";
        _seed.Text = _seedCopied ? $"Seed {GameRoot.SeedOf(run)} · copied" : $"Seed {GameRoot.SeedOf(run)}";
        _map.SetState(session, _selected);
    }

    /// <summary>A node type as the player reads it.</summary>
    internal static string NodeName(NodeType type) => type switch
    {
        NodeType.HardFight => "Hard fight",
        _ => type.ToString(),
    };

    private void CopySeed()
    {
        if (Session?.State is { } run)
        {
            DisplayServer.ClipboardSet(GameRoot.SeedOf(run));
            _seedCopied = true;
            Refresh();
        }
    }

    private void OnAbandonPressed()
    {
        if (_abandonArmed)
        {
            GameRoot.Instance.LeaveRun();
            return;
        }

        ArmAbandon(true);
        _abandonTimer.Start();
    }

    /// <summary>Armed, the button asks in coral and the next press abandons the voyage. It disarms by itself.</summary>
    private void ArmAbandon(bool armed)
    {
        _abandonArmed = armed;
        _abandon.Text = armed ? "Abandon voyage?" : "Abandon";
        var color = armed ? Palette.Refused : Palette.Muted;
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            _abandon.AddThemeColorOverride(state, color);
        }
    }

    private void Go()
    {
        if (_selected is int id)
        {
            GameRoot.Instance.Act(s => s.Map.Choose(s.State, id));
        }
    }
}
