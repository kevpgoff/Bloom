using Bloom.Core.Cards;
using Bloom.Core.Content;
using Bloom.Core.Map;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Platform;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// The hangar, shown while there is no run: the player chooses a ship class from the rail at the bottom, one of
/// its loadouts and a seed, and launches. The chosen ship shows as a hologram with its starting pieces, and the
/// tabs under it list those pieces, the starting deck and the voyage's seed. Choosing changes no state; only
/// <see cref="GameRoot.Launch"/> starts a run.
/// </summary>
/// <remarks>
/// A debug build (<see cref="GameRoot.TestTools"/>) has a fourth tab, Test, which chooses where a test run starts:
/// on the map, at a node of one type, or at one event. While a start is chosen there, the main button launches
/// a test run (<see cref="GameRoot.LaunchTest"/>) and says so; "Off" returns it to a voyage. While a voyage waits
/// behind the hangar (<see cref="GameRoot.VoyageParked"/>), there is no "Off", and a button at the top goes back
/// to that voyage.
/// </remarks>
public partial class HangarScreen : Control
{
    private const float RailHeight = 112;
    private const float RailShape = 56;

    /// <summary>The node types a test run can start at, in the order of the Test tab.</summary>
    private static readonly NodeType[] TestNodes =
        [NodeType.Skirmish, NodeType.HardFight, NodeType.Boss, NodeType.Dock, NodeType.Shipyard, NodeType.Grove, NodeType.Event];

    private enum Tab
    {
        Pieces,
        Deck,
        Voyage,
        Test,
    }

    /// <summary>Where a test run starts: at a node of a type, or on the map when <see cref="Node"/> is null. An event node can name its event.</summary>
    private sealed record TestChoice(NodeType? Node, string? EventId = null);

    /// <summary>
    /// The start chosen on the Test tab, or null for a voyage. It is kept while the game runs, so the hangar
    /// reopens on it after a test and the same test can be launched again with one press.
    /// </summary>
    private static TestChoice? _lastTest;

    private readonly List<(ShipClassDef Ship, OptionCard Card)> _rail = [];
    private readonly List<(ShipLoadout Loadout, Button Button)> _loadoutButtons = [];
    private readonly Dictionary<Tab, Button> _tabButtons = [];
    private readonly List<(TestChoice? Choice, Button Button)> _testNodeButtons = [];
    private readonly List<(string? EventId, Button Button)> _testEventButtons = [];

    private ShipPreview _preview = null!;
    private Label _name = null!;
    private Label _commune = null!;
    private HBoxContainer _loadouts = null!;
    private RichTextLabel _info = null!;
    private Control _textPage = null!;
    private RichTextLabel _tabText = null!;
    private Control _voyagePage = null!;
    private Control _testPage = null!;
    private Control _testEvents = null!;
    private LineEdit _seed = null!;
    private Button _launch = null!;

    private ShipClassDef? _ship;
    private ShipLoadout? _loadout;
    private RunState? _start;
    private FittedPiece? _selectedPiece;
    private Tab _tab = Tab.Pieces;
    private TestChoice? _test;

    private static ContentDb? Content => GameRoot.Instance.Content;

    public override void _Ready()
    {
        _preview = GetNode<ShipPreview>("%Preview");
        _name = GetNode<Label>("%ShipName");
        _commune = GetNode<Label>("%Commune");
        _loadouts = GetNode<HBoxContainer>("%Loadouts");
        _info = GetNode<RichTextLabel>("%Info");
        _textPage = GetNode<Control>("%TextPage");
        _tabText = GetNode<RichTextLabel>("%TabText");
        _voyagePage = GetNode<Control>("%VoyagePage");
        _testPage = GetNode<Control>("%TestPage");
        _testEvents = GetNode<Control>("%TestEvents");
        _seed = GetNode<LineEdit>("%SeedEdit");
        _launch = GetNode<Button>("%LaunchButton");
        _commune.AddThemeColorOverride("font_color", Palette.Muted);
        GetNode<Label>("%SeedLabel").AddThemeColorOverride("font_color", Palette.Muted);

        _tabButtons[Tab.Pieces] = GetNode<Button>("%PiecesTab");
        _tabButtons[Tab.Deck] = GetNode<Button>("%DeckTab");
        _tabButtons[Tab.Voyage] = GetNode<Button>("%VoyageTab");
        _tabButtons[Tab.Test] = GetNode<Button>("%TestTab");
        _tabButtons[Tab.Test].Visible = GameRoot.TestTools;
        foreach (var (tab, button) in _tabButtons)
        {
            button.Pressed += () => ShowTab(tab);
        }

        _preview.PieceSelected += OnPieceSelected;
        _launch.Pressed += Launch;
        _seed.MaxLength = RunSeed.MaxLength;
        _seed.Text = GameRoot.NewSeedCode();
        _seed.TextSubmitted += _ => TidySeed();
        _seed.FocusExited += TidySeed;
        GetNode<Button>("%NewSeedButton").Pressed += () => _seed.Text = GameRoot.NewSeedCode();

        if (Content is not { Ships.Count: > 0 } content)
        {
            return;
        }

        var back = GetNode<Button>("%BackButton");
        back.Visible = GameRoot.Instance.VoyageParked;
        back.Pressed += GameRoot.Instance.ResumeVoyage;
        if (GameRoot.TestTools)
        {
            // A voyage that waits must not be replaced, so only test runs can be launched until it is resumed.
            _test = _lastTest ?? (GameRoot.Instance.VoyageParked ? new TestChoice(null) : null);
            _tab = _test is null ? Tab.Pieces : Tab.Test;
            BuildTestPage(content);
        }

        var rail = GetNode<HBoxContainer>("%Rail");
        foreach (var ship in content.Ships)
        {
            var card = new OptionCard(Palette.HoloLine, $"[center]{ship.Name}[/center]", minHeight: RailHeight, shapeSize: RailShape, fontSize: 22, hull: ship.HullShape, stacked: true);
            card.Button.Pressed += () => ChooseShip(ship, null);
            _rail.Add((ship, card));
            rail.AddChild(card.Button);
        }

        // The hangar opens on the ship and loadout chosen last, or on the first of each.
        var remembered = content.Ships.FirstOrDefault(s => s.Id == UserSettings.HangarShip) ?? content.Ships[0];
        ChooseShip(remembered, UserSettings.HangarLoadout);
        GD.Print($"HangarScreen: {content.Ships.Count} ship classes; showing {_ship?.Id} (loadout {_loadout?.Id}), seed {_seed.Text}.");
    }

    /// <summary>Shows a ship class with the named loadout, or with its first one.</summary>
    private void ChooseShip(ShipClassDef ship, string? loadoutId)
    {
        _ship = ship;
        foreach (var (other, card) in _rail)
        {
            card.SetSelected(other == ship);
        }

        foreach (var (_, button) in _loadoutButtons)
        {
            _loadouts.RemoveChild(button);
            button.QueueFree();
        }

        _loadoutButtons.Clear();
        foreach (var loadout in ship.Loadouts)
        {
            var button = new Button
            {
                Text = ShipText.Loadout(ship, loadout),
                CustomMinimumSize = new Vector2(0, 64),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.None,
            };
            button.AddThemeFontSizeOverride("font_size", 24);
            button.Pressed += () => ChooseLoadout(loadout);
            _loadoutButtons.Add((loadout, button));
            _loadouts.AddChild(button);
        }

        // A ship with a single loadout still shows it, by name, so the rows below stay where they are from ship to ship.
        ChooseLoadout(ship.Loadouts.FirstOrDefault(l => l.Id == loadoutId) ?? ship.Loadouts[0]);
    }

    private void ChooseLoadout(ShipLoadout loadout)
    {
        if (_ship is not { } ship || Content is not { } content)
        {
            return;
        }

        _loadout = loadout;
        foreach (var (other, button) in _loadoutButtons)
        {
            MarkChosen(button, other == loadout);
        }

        // The ship as a run would start with it. The seed does not matter here: only the ship is read.
        _start = RunFactory.NewRun(content, new RunSetup(ship.Id, Seed: 0, LoadoutId: loadout.Id));
        _selectedPiece = null;
        _preview.SetShip(content, _start);

        var fitter = new ShipFitter(content);
        int cells = _start.Ship.Grid.CellCount;
        int free = cells - _start.Ship.Fitted.Sum(f => fitter.CellsOf(f).Count);
        int cards = DeckBuilder.Build(_start.Ship, content).Count;
        var lines = new List<string> { $"Hull {_start.MaxHull}  ·  {cells} cells, {free} free  ·  {cards} cards" };
        if (ShipText.Rule(ship.Rule) is { Length: > 0 } rule)
        {
            lines.Add($"[color=#{Palette.HoloBright.ToHtml(false)}]{rule}[/color]");
        }

        if (!string.IsNullOrWhiteSpace(ship.GrownFrom))
        {
            lines.Add($"[color=#{Palette.Muted.ToHtml(false)}]{ship.GrownFrom}[/color]");
        }

        _name.Text = ship.Name;
        _commune.Text = ShipText.Commune(ship.Commune);
        _info.Text = string.Join("\n", lines);
        ShowTab(_tab);
        ShowTest();
    }

    private void OnPieceSelected(FittedPiece? piece)
    {
        _selectedPiece = piece;
        ShowTab(piece is null ? _tab : Tab.Pieces);
    }

    private void ShowTab(Tab tab)
    {
        _tab = tab;
        foreach (var (other, button) in _tabButtons)
        {
            MarkChosen(button, other == tab);
        }

        _voyagePage.Visible = tab == Tab.Voyage;
        _testPage.Visible = tab == Tab.Test;
        _textPage.Visible = tab is Tab.Pieces or Tab.Deck;
        if (_start is not { } start || Content is not { } content)
        {
            return;
        }

        _tabText.Text = tab switch
        {
            Tab.Pieces when _selectedPiece is { } selected => CardText.PieceDetails(content.Piece(selected.Piece.DefId), content),
            Tab.Pieces => PieceList(start, content),
            Tab.Deck => DeckList(start, content),
            _ => "",
        };
    }

    /// <summary>
    /// Fills the Test tab: where a test run starts, and under it the events, shown while an event node is chosen.
    /// </summary>
    private void BuildTestPage(ContentDb content)
    {
        var starts = new List<(string Text, TestChoice? Choice)>();
        if (!GameRoot.Instance.VoyageParked)
        {
            starts.Add(("Off", null));
        }

        starts.Add(("Map", new TestChoice(null)));
        starts.AddRange(TestNodes.Select(type => (MapScreen.NodeName(type), (TestChoice?)new TestChoice(type))));

        var nodes = GetNode<Container>("%TestNodes");
        foreach (var (text, choice) in starts)
        {
            // Choosing the event node again keeps the event that was chosen for it.
            var button = Chip(text, () => ChooseTest(choice is { Node: NodeType.Event } && _test is { Node: NodeType.Event } ? _test : choice));
            _testNodeButtons.Add((choice, button));
            nodes.AddChild(button);
        }

        foreach (var (text, id) in content.Events.Select(e => (e.Name, (string?)e.Id)).Prepend(("Any event", null)))
        {
            var button = Chip(text, () => ChooseTest(new TestChoice(NodeType.Event, id)));
            _testEventButtons.Add((id, button));
            _testEvents.AddChild(button);
        }
    }

    private static Button Chip(string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(96, 56), FocusMode = FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 22);
        button.Pressed += pressed;
        return button;
    }

    private void ChooseTest(TestChoice? choice)
    {
        _test = choice;
        _lastTest = choice;
        ShowTest();
    }

    /// <summary>Lights the chosen start on the Test tab, and names on the main button what it launches.</summary>
    private void ShowTest()
    {
        foreach (var (choice, button) in _testNodeButtons)
        {
            MarkChosen(button, choice?.Node == _test?.Node && (choice is null) == (_test is null));
        }

        _testEvents.Visible = _test is { Node: NodeType.Event };
        foreach (var (id, button) in _testEventButtons)
        {
            MarkChosen(button, _testEvents.Visible && id == _test?.EventId);
        }

        if (_ship is { } ship)
        {
            _launch.Text = _test is { } test
                ? $"Test · {ship.Name} · {(test.Node is { } node ? MapScreen.NodeName(node) : "Map")}"
                : $"Launch · {ship.Name}";
        }
    }

    /// <summary>The starting pieces, one line for each kind: "2× Burning Glass   Equipment · Weapon · Common".</summary>
    private static string PieceList(RunState start, ContentDb content)
    {
        string muted = Palette.Muted.ToHtml(false);
        return string.Join("\n", start.Ship.Fitted
            .GroupBy(f => f.Piece.DefId)
            .Select(group =>
            {
                var def = content.Piece(group.Key);
                string count = group.Count() > 1 ? $"{group.Count()}× " : "";
                return $"{count}{def.Name}   [color=#{muted}]{CardText.Kind(def)}[/color]";
            }));
    }

    /// <summary>The starting deck, one line for each card: "2× Lance (2 energy): Deal 12 damage."</summary>
    private static string DeckList(RunState start, ContentDb content) =>
        string.Join("\n", DeckBuilder.Build(start.Ship, content)
            .GroupBy(card => card.CardId)
            .Select(group => $"{group.Count()}× {CardText.Card(content.Card(group.Key))}"));

    /// <summary>Shows the seed as it will be stored: in capitals, without spaces around it.</summary>
    private void TidySeed() => _seed.Text = RunSeed.Normalize(_seed.Text);

    private void Launch()
    {
        if (_ship is not { } ship || _loadout is not { } loadout)
        {
            return;
        }

        // An emptied seed field means any voyage will do.
        string seed = RunSeed.Normalize(_seed.Text) is { Length: > 0 } typed ? typed : GameRoot.NewSeedCode();
        UserSettings.RememberHangar(ship.Id, loadout.Id);
        var setup = RunSetup.FromSeedText(ship.Id, seed, loadout.Id);
        if (_test is { } test)
        {
            GameRoot.Instance.LaunchTest(setup, test.Node is { } node ? new TestStart(node, test.EventId) : null);
        }
        else
        {
            GameRoot.Instance.Launch(setup);
        }
    }

    /// <summary>Lights a tab or a loadout button as the chosen one, as <see cref="OptionCard.SetSelected"/> does for a card.</summary>
    private static void MarkChosen(Button button, bool chosen)
    {
        var box = BloomTheme.OfferSurface(Palette.HoloLine, chosen);
        box.ContentMarginLeft = 16;
        box.ContentMarginRight = 16;
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
        {
            button.AddThemeStyleboxOverride(state, box);
        }

        var color = chosen ? Palette.Selectable : Palette.Ink;
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            button.AddThemeColorOverride(state, color);
        }
    }
}
