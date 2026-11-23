using Bloom.Core;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// At a dock: the pieces for sale, its hull repairs and the cargo hold's pieces, which the dock buys, as option
/// cards. Tapping a card selects it and the middle button, which names the trade and its price, makes it
/// through <see cref="GameRoot.Act"/>. The dock stays until the player leaves, so the cards are rebuilt
/// after every trade. Ship opens the Fitting screen, where the dock also buys fitted pieces.
/// </summary>
public partial class DockScreen : Control
{
    private enum Trade
    {
        Buy,
        Repair,
        Sell,
    }

    private readonly List<(OptionCard Card, Trade Trade, int Value)> _options = [];
    private (Trade Trade, int Value)? _selected;
    private Label _status = null!;
    private VBoxContainer _list = null!;
    private Control _cargoNote = null!;
    private Button _ship = null!;
    private Button _action = null!;

    private static RunSession? Session => GameRoot.Instance.Session;

    public override void _Ready()
    {
        _status = GetNode<Label>("%StatusLabel");
        _list = GetNode<VBoxContainer>("%Options");
        _cargoNote = GetNode<Control>("%CargoNote");
        _ship = GetNode<Button>("%ShipButton");
        _action = GetNode<Button>("%ActionButton");

        _ship.Pressed += () => GameRoot.Instance.ShowShip(true);
        _action.Pressed += MakeTrade;
        GetNode<Button>("%LeaveButton").Pressed += () => GameRoot.Instance.Act(s => s.Dock.Leave(s.State));
        GameRoot.Instance.Changed += Rebuild;
        Rebuild();

        if (Session?.State is { Dock: { } dock } run)
        {
            GD.Print($"DockScreen: for sale {string.Join(", ", dock.Pieces)}; hull {run.Hull}/{run.MaxHull}, salvage {run.Salvage}, cargo {run.Cargo.Pieces.Count}.");
        }
    }

    public override void _ExitTree() => GameRoot.Instance.Changed -= Rebuild;

    private void Rebuild()
    {
        if (Session is not { State: { Dock: { } dock } run } session)
        {
            return;
        }

        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        _options.Clear();
        _status.Text = $"Hull {run.Hull} / {run.MaxHull}  ·  Salvage {run.Salvage}";
        _ship.Text = run.Cargo.Pieces.Count > 0 ? $"Ship · {run.Cargo.Pieces.Count}" : "Ship";
        _cargoNote.Visible = run.Cargo.IsFull;
        string muted = Palette.Muted.ToHtml(false);

        if (dock.Pieces.Count > 0)
        {
            _list.AddChild(OptionCard.Heading("For sale"));
        }

        for (int i = 0; i < dock.Pieces.Count; i++)
        {
            var def = session.Content.Piece(dock.Pieces[i]);
            int price = session.Dock.Price(def.Id);
            var priceColor = session.Dock.CanBuy(run, i) ? Palette.Energy : Palette.Refused;
            string text = $"[b]{def.Name}[/b]   [color=#{priceColor.ToHtml(false)}]{price} salvage[/color]\n" +
                          $"[color=#{muted}]{CardText.Kind(def)}[/color]\n{CardText.Cards(def, session.Content)}";
            if (def.Bonus is { } bonus)
            {
                text += $"\n{CardText.Bonus(bonus)}";
            }

            Add(new OptionCard(Palette.ForPiece(def), text, def, minHeight: 170, shapeSize: 120, fontSize: 22), Trade.Buy, i);
        }

        int repair = DockEngine.RepairAmount(run);
        string title = $"[b][color=#{Palette.Label(Palette.Repair).ToHtml(false)}]Repair hull[/color][/b]{(dock.Repairs > 0 ? $"   [color=#{muted}]{dock.Repairs} left[/color]" : "")}";
        string detail = dock.Repairs == 0 ? "None left."
            : repair == 0 ? "The hull is already whole."
            : $"+{repair} hull  ·  {run.Hull} → {run.Hull + repair}   [color=#{(run.Salvage >= Rules.DockRepairPrice ? Palette.Energy : Palette.Refused).ToHtml(false)}]{Rules.DockRepairPrice} salvage[/color]";
        Add(new OptionCard(Palette.Repair, $"{title}\n{detail}", minHeight: 120), Trade.Repair, 0);

        if (run.Cargo.Pieces.Count > 0)
        {
            _list.AddChild(OptionCard.Heading($"Cargo hold  ·  {run.Cargo.Pieces.Count} / {Rules.CargoCapacity}"));
        }

        foreach (var piece in run.Cargo.Pieces)
        {
            var def = session.Content.Piece(piece.DefId);
            string text = $"[b]{CardText.PieceName(def, piece)}[/b]   [color=#{Palette.Boosted.ToHtml(false)}]+{session.Dock.SellPrice(piece)} salvage[/color]\n" +
                          $"[color=#{muted}]{CardText.Kind(def)}[/color]";
            Add(new OptionCard(Palette.ForPiece(def), text, def, minHeight: 110, shapeSize: 80, fontSize: 22), Trade.Sell, piece.Id);
        }

        // A selection survives a trade only while it still means the same thing.
        if (_selected is { } selected && !_options.Exists(o => (o.Trade, o.Value) == selected))
        {
            _selected = null;
        }

        ShowSelection();
    }

    private void Add(OptionCard card, Trade trade, int value)
    {
        card.Button.Pressed += () =>
        {
            _selected = (trade, value);
            ShowSelection();
        };
        _list.AddChild(card.Button);
        _options.Add((card, trade, value));
    }

    private void ShowSelection()
    {
        if (Session is not { State: { Dock: { } dock } run } session)
        {
            return;
        }

        foreach (var (card, trade, value) in _options)
        {
            card.SetSelected(_selected == (trade, value));
        }

        (_action.Text, _action.Disabled) = _selected switch
        {
            (Trade.Buy, int index) => ($"Buy · {session.Dock.Price(dock.Pieces[index])}", !session.Dock.CanBuy(run, index)),
            (Trade.Repair, _) => ($"Repair · {Rules.DockRepairPrice}", !DockEngine.CanRepair(run)),
            (Trade.Sell, int id) when run.Cargo.Pieces.Find(p => p.Id == id) is { } piece =>
                ($"Sell · +{session.Dock.SellPrice(piece)}", !session.Dock.CanSell(run, id)),
            _ => ("Buy", true),
        };
    }

    private void MakeTrade()
    {
        if (_selected is not { } selected)
        {
            return;
        }

        // A bought or sold piece leaves the list; a repair can be bought again.
        if (selected.Trade != Trade.Repair)
        {
            _selected = null;
        }

        GameRoot.Instance.Act(s =>
        {
            switch (selected.Trade)
            {
                case Trade.Buy:
                    s.Dock.Buy(s.State, selected.Value);
                    break;
                case Trade.Repair:
                    s.Dock.Repair(s.State);
                    break;
                case Trade.Sell:
                    s.Dock.Sell(s.State, selected.Value);
                    break;
            }
        });
    }
}
