using Bloom.Core;
using Bloom.Core.Map;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// The sector map between nodes: the whole map on a <see cref="MapView"/>, what the tapped node holds, a
/// button to set course for it, and one to open the ship for refitting. Choosing goes through
/// <see cref="GameRoot.Act"/>; opening the ship only switches screens.
/// </summary>
public partial class MapScreen : Control
{
    private MapView _map = null!;
    private Label _title = null!;
    private Label _status = null!;
    private RichTextLabel _info = null!;
    private Button _ship = null!;
    private Button _go = null!;
    private int? _selected;

    private static RunSession? Session => GameRoot.Instance.Session;

    public override void _Ready()
    {
        _map = GetNode<MapView>("%MapView");
        _title = GetNode<Label>("%Title");
        _status = GetNode<Label>("%StatusLabel");
        _info = GetNode<RichTextLabel>("%InfoText");
        _ship = GetNode<Button>("%ShipButton");
        _go = GetNode<Button>("%GoButton");
        BloomTheme.AttachPanelFrame(GetNode<Control>("Layout/InfoPanel"));

        _map.NodeTapped += id =>
        {
            _selected = id;
            Refresh();
        };
        _ship.Pressed += () => GameRoot.Instance.ShowShip(true);
        _go.Pressed += Go;
        GameRoot.Instance.Changed += Refresh;

        // With a single way on, it is chosen already; the player still confirms with Set course.
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

    private void Refresh()
    {
        if (Session is not { State.Map: { } map } session)
        {
            return;
        }

        var run = session.State;
        if (_selected is int id && (id < 0 || id >= map.Nodes.Count))
        {
            _selected = null;
        }

        var reachable = session.Map.Reachable(run);
        bool canGo = _selected is int chosen && reachable.Any(n => n.Id == chosen);
        _title.Text = run.Sectors > 1 ? $"Sector {run.Sector} of {run.Sectors}" : "Sector map";
        _status.Text = $"Hull {run.Hull} / {run.MaxHull}  ·  Salvage {run.Salvage}";
        _ship.Text = run.Cargo.Pieces.Count > 0 ? $"Ship · {run.Cargo.Pieces.Count} held" : "Ship";
        _go.Disabled = !canGo;
        _map.SetState(session, _selected);
        _info.Text = _selected is int node ? Describe(run, map.Node(node), canGo) : Hint;
    }

    private static string Hint =>
        "Tap a node to see what it holds. The glowing nodes are the ones you can reach next.\n" +
        $"[color=#{Palette.Muted.ToHtml(false)}]Open the ship to refit it at any time before you set course.[/color]";

    private static string Describe(RunState run, MapNode node, bool canGo)
    {
        var (name, text) = node.Type switch
        {
            NodeType.Skirmish => ("Skirmish", "A fight. The win pays salvage and a choice of 1 piece from 3."),
            NodeType.HardFight => ("Hard fight", "A stronger enemy. The win pays more salvage, and one of the 3 pieces is always rare."),
            NodeType.Boss => ("Boss", run.Sector >= run.Sectors
                ? "The machine that guards the way out of the sector. Beat it to complete the voyage."
                : "The machine that guards the way out of the sector. Beat it to reach the next one."),
            NodeType.Grove => ("Grove", $"A garden habitat. Its gardeners repair {Rules.GroveRepairPercent}% of the hull, up to the maximum."),
            NodeType.Dock => ("Dock", "A market: buy pieces and hull repair, or sell pieces for salvage."),
            NodeType.Shipyard => ("Shipyard", "Add a hull section to the ship, for free."),
            NodeType.Event => ("Event", "Something strange in the dark, and a choice to make."),
            _ => (node.Type.ToString(), ""),
        };

        string muted = Palette.Muted.ToHtml(false);
        string footer = canGo
            ? MapEngine.PoolFor(node) is null
                ? "Set course to go there."
                : "Set course to commit to this fight. You'll see the enemy and can refit before it starts."
            : run.Path.Contains(node.Id)
                ? "Visited."
                : "Out of reach from here.";
        string colorHex = Palette.ForNode(node.Type).ToHtml(false);
        return $"[b][color=#{colorHex}]{name}[/color][/b]  {text}\n[color=#{muted}]{footer}[/color]";
    }

    private void Go()
    {
        if (_selected is int id)
        {
            GameRoot.Instance.Act(s => s.Map.Choose(s.State, id));
        }
    }
}
