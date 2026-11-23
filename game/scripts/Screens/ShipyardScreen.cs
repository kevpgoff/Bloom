using Bloom.Core.Map;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// At a shipyard, or with a boss's free hull section: the whole grid on a <see cref="ShipyardBoard"/> and the
/// offered sections as option cards under it. Tapping a card shows that section on the board as a ghost; the
/// main button names it and attaches it where the ghost is, through <see cref="GameRoot.Act"/>. Leave takes none.
/// </summary>
public partial class ShipyardScreen : Control
{
    private readonly List<OptionCard> _cards = [];
    private int? _selected;
    private ShipyardBoard _board = null!;
    private Button _attach = null!;

    private static RunSession? Session => GameRoot.Instance.Session;

    public override void _Ready()
    {
        _board = GetNode<ShipyardBoard>("%Board");
        _attach = GetNode<Button>("%AttachButton");
        _attach.Pressed += Attach;
        _board.GhostChanged += ShowSelection;
        GetNode<Button>("%LeaveButton").Pressed += () => GameRoot.Instance.Act(s => s.Shipyard.Leave(s.State));
        if (Session is not { State: { Shipyard: { } yard } run } session)
        {
            return;
        }

        // A boss's free section comes at the start of the next sector, away from any shipyard.
        bool atShipyard = run.CurrentNode is { Type: NodeType.Shipyard };
        GetNode<Label>("%Title").Text = atShipyard ? "Shipyard" : "Hull section";
        GetNode<Label>("%Message").Text = atShipyard
            ? "Shipwrights tend racks of grown hull, still green at the seams."
            : "Spare hull drifts where the fight was. The crew tows it alongside.";
        GetNode<Label>("%StatusLabel").Text = $"Hull {run.Hull} / {run.MaxHull}  ·  Salvage {run.Salvage}";

        var options = GetNode<HBoxContainer>("%Options");
        options.Visible = yard.Sections.Count > 0;
        string muted = Palette.Muted.ToHtml(false);
        for (int i = 0; i < yard.Sections.Count; i++)
        {
            var section = session.Content.Section(yard.Sections[i]);
            var card = new OptionCard(
                Palette.HoloLine,
                $"[center][b]{section.Name}[/b]\n[color=#{muted}]+{section.Shape.Count} cells[/color][/center]",
                minHeight: 190,
                shapeSize: 96,
                fontSize: 22,
                hull: section.Shape,
                stacked: true);
            int index = i;
            card.Button.Pressed += () => Select(index);
            options.AddChild(card.Button);
            _cards.Add(card);
        }

        if (_cards.Count > 0)
        {
            Select(0);
        }
        else
        {
            ShowSelection();
        }

        GD.Print($"ShipyardScreen: offers {string.Join(", ", yard.Sections)}; hull {run.Ship.Grid.CellCount} cells.");
    }

    private void Select(int index)
    {
        if (Session is not { } session || session.Shipyard.Offered(session.State, index) is not { } section)
        {
            return;
        }

        _selected = index;
        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].SetSelected(i == index);
        }

        _board.ShowSection(section.Shape);
    }

    private void ShowSelection()
    {
        var section = _selected is int index && Session is { } session ? session.Shipyard.Offered(session.State, index) : null;
        _attach.Text = section is null ? "Attach" : $"Attach · {section.Name}";
        _attach.Disabled = section is null || !_board.Fits;
    }

    private void Attach()
    {
        if (_selected is int index && _board.Fits)
        {
            var origin = _board.GhostOrigin;
            var rotation = _board.GhostRotation;
            GameRoot.Instance.Act(s => s.Shipyard.Attach(s.State, index, origin, rotation));
        }
    }
}
