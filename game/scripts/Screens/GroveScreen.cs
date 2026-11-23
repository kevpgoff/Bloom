using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// At a grove: repair the hull, or upgrade one piece, as option cards. Each upgradable piece has its own
/// card showing its cards as they would be upgraded. Tapping a card selects it and the main button, which
/// names the choice, confirms it through <see cref="GameRoot.Act"/>.
/// </summary>
public partial class GroveScreen : Control
{
    private readonly List<(OptionCard Card, string Confirm, Action<RunSession> Action)> _options = [];
    private int? _selected;
    private Button _confirm = null!;

    private static RunSession? Session => GameRoot.Instance.Session;

    public override void _Ready()
    {
        _confirm = GetNode<Button>("%ConfirmButton");
        _confirm.Pressed += Confirm;
        if (Session is not { State: { } run } session)
        {
            return;
        }

        GetNode<RichTextLabel>("%Message").Text =
            "A garden habitat turns slowly in the sunlight. Its gardeners come aboard with grafts and resin.\n" +
            $"{HullWarning.HullBbcode(run)}  ·  Salvage {run.Salvage}";

        var options = GetNode<VBoxContainer>("%Options");
        int repair = GroveEngine.RepairAmount(run);
        string title = $"[b][color=#{Palette.Label(Palette.GardenGlow).ToHtml(false)}]Repair hull[/color][/b]";
        AddOption(
            options,
            new OptionCard(Palette.GardenGlow, $"{title}\n{(repair > 0 ? $"+{repair} hull  ·  {run.Hull} → {run.Hull + repair}" : "The hull is already whole.")}"),
            "Repair hull",
            s => s.Grove.Repair(s.State));

        var upgradable = PieceUpgrades.Upgradable(session.Content, run);
        if (upgradable.Count > 0)
        {
            options.AddChild(OptionCard.Heading("Upgrade"));
        }

        foreach (var piece in upgradable)
        {
            var def = session.Content.Piece(piece.DefId);
            int id = piece.Id;
            string where = run.Cargo.Pieces.Contains(piece) ? $"   [color=#{Palette.Muted.ToHtml(false)}]Cargo hold[/color]" : "";
            string text = $"[b]{CardText.PieceName(def, upgraded: true)}[/b]{where}\n{CardText.Cards(def, session.Content, upgraded: true)}";
            AddOption(
                options,
                new OptionCard(Palette.ForPiece(def), text, def, minHeight: 140, shapeSize: 108, fontSize: 22),
                $"Upgrade · {def.Name}",
                s => s.Grove.Upgrade(s.State, id));
        }

        // Repair is the usual choice, so it starts selected unless the hull is already whole.
        _selected = repair > 0 ? 0 : null;
        ShowSelection();
        GD.Print($"GroveScreen: hull {run.Hull}/{run.MaxHull}, repair {repair}, upgradable {string.Join(", ", upgradable.Select(p => p.DefId))}.");
    }

    private void AddOption(VBoxContainer options, OptionCard card, string confirm, Action<RunSession> action)
    {
        int index = _options.Count;
        card.Button.Pressed += () =>
        {
            _selected = index;
            ShowSelection();
        };
        options.AddChild(card.Button);
        _options.Add((card, confirm, action));
    }

    private void ShowSelection()
    {
        for (int i = 0; i < _options.Count; i++)
        {
            _options[i].Card.SetSelected(i == _selected);
        }

        _confirm.Disabled = _selected is null;
        _confirm.Text = _selected is int index ? _options[index].Confirm : "Confirm";
    }

    private void Confirm()
    {
        if (_selected is int index)
        {
            GameRoot.Instance.Act(_options[index].Action);
        }
    }
}
