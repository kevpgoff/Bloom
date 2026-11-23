using Bloom.Core.Content;
using Bloom.Core.Map;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// At an event: its story and its choices as option cards. A card shows only the choice's label, with its salvage
/// cost and the tag it needs, if any; the owner wants events read, so outcomes stay hidden until chosen. A choice
/// that needs a tag no fitted piece carries is not shown. Tapping a card selects it and the main button, which names
/// it, takes it through <see cref="GameRoot.Act"/>. Then the result's text and what it changed are shown, and the
/// button continues: to the map, the hull section or the fight.
/// </summary>
public partial class EventScreen : Control
{
    private readonly List<(OptionCard Card, int Index)> _cards = [];
    private int? _selected;
    private Button _action = null!;

    private static RunSession? Session => GameRoot.Instance.Session;

    public override void _Ready()
    {
        _action = GetNode<Button>("%ActionButton");
        _action.Pressed += Act;
        GameRoot.Instance.Changed += Rebuild;
        Rebuild();
    }

    public override void _ExitTree() => GameRoot.Instance.Changed -= Rebuild;

    /// <summary>Shows the event as it stands: its choices before one is taken, its result after.</summary>
    private void Rebuild()
    {
        if (Session is not { State: { Event: { } visit } run } session || session.Event.Current(run) is not { } ev)
        {
            return;
        }

        GetNode<Label>("%Title").Text = ev.Name;
        GetNode<RichTextLabel>("%StatusLabel").Text = $"{HullWarning.HullBbcode(run)}  ·  Salvage {run.Salvage}";
        GetNode<Label>("%Story").Text = ev.Text;

        var options = GetNode<VBoxContainer>("%Options");
        foreach (var child in options.GetChildren())
        {
            options.RemoveChild(child);
            child.QueueFree();
        }

        _cards.Clear();
        if (session.Event.Result(run) is { } result)
        {
            ShowResult(session, ev, visit, result);
        }
        else
        {
            ShowChoices(session, ev);
        }
    }

    private void ShowChoices(RunSession session, EventDef ev)
    {
        var options = GetNode<VBoxContainer>("%Options");
        string muted = Palette.Muted.ToHtml(false);
        for (int i = 0; i < ev.Choices.Count; i++)
        {
            var choice = ev.Choices[i];
            var state = session.Event.StateOf(session.State, choice);
            if (state == ChoiceState.Hidden)
            {
                continue;
            }

            // The tag is muted; a cost is priced as at the dock, red when the ship cannot pay it.
            var notes = new List<string>();
            if (choice.NeedsTag is { } tag)
            {
                notes.Add($"[color=#{muted}]{tag}[/color]");
            }

            if (choice.Cost > 0)
            {
                var price = state == ChoiceState.Unaffordable ? Palette.Refused : Palette.Energy;
                notes.Add($"[color=#{price.ToHtml(false)}]{choice.Cost} salvage[/color]");
            }

            string text = $"[b]{choice.Label}[/b]" + (notes.Count > 0 ? $"\n{string.Join($"[color=#{muted}]  ·  [/color]", notes)}" : "");
            var card = new OptionCard(Palette.ForNode(NodeType.Event), text, minHeight: 96, fontSize: 22);
            card.Button.Disabled = state == ChoiceState.Unaffordable;
            int index = i;
            card.Button.Pressed += () => Select(index);
            options.AddChild(card.Button);
            _cards.Add((card, index));
        }

        ShowSelection(session, ev);
        GD.Print($"EventScreen: {ev.Id}, choices shown {string.Join(", ", _cards.Select(c => c.Index))}.");
    }

    private void ShowResult(RunSession session, EventDef ev, EventVisit visit, EventResult result)
    {
        var choice = ev.Choices[visit.Choice!.Value];
        GetNode<Control>("%Options").Visible = false;
        var chosen = GetNode<Label>("%Chosen");
        chosen.Text = choice.Label;
        chosen.Visible = true;
        var resultLabel = GetNode<Label>("%Result");
        resultLabel.Text = result.Text;
        resultLabel.Visible = true;

        string changes = Changes(session, visit.Changes);
        var changesLabel = GetNode<RichTextLabel>("%Changes");
        changesLabel.Text = changes;
        changesLabel.Visible = changes.Length > 0;

        _action.Disabled = false;
        // A section is offered only when one can still attach somewhere.
        bool section = result.AllOutcomes.Any(o => o is SectionOutcome)
            && session.Content.Sections.Any(s => ShipyardEngine.Fits(session.State.Ship.Grid, s.Shape));
        _action.Text = result.AllOutcomes.OfType<FightOutcome>().FirstOrDefault() is { } fight
            ? $"Fight · {session.Content.Enemy(fight.Enemy).Name}"
            : section ? "Hull section" : "Continue";
        GD.Print($"EventScreen: {ev.Id}, chose {visit.Choice}, result {visit.Result}.");
    }

    /// <summary>What the result actually changed, as one line: numbers, then the pieces gained and upgraded, by name.</summary>
    private static string Changes(RunSession session, EventChanges? changes)
    {
        if (changes is null)
        {
            return "";
        }

        var run = session.State;
        var parts = new List<string>();
        void Number(int amount, string what, Color color)
        {
            if (amount != 0)
            {
                parts.Add($"[color=#{(amount > 0 ? color : Palette.Refused).ToHtml(false)}]{(amount > 0 ? "+" : "−")}{Math.Abs(amount)} {what}[/color]");
            }
        }

        Number(changes.Salvage, "salvage", Palette.Energy);
        Number(changes.Hull, "hull", Palette.GardenGlow);
        Number(changes.MaxHull, "max hull", Palette.GardenGlow);

        var owned = run.Ship.Fitted.Select(f => f.Piece).Concat(run.Cargo.Pieces).ToList();
        foreach (int id in changes.Gained)
        {
            if (owned.Find(p => p.Id == id) is { } piece)
            {
                var def = session.Content.Piece(piece.DefId);
                parts.Add($"[color=#{Palette.Label(Palette.ForPiece(def)).ToHtml(false)}]{CardText.PieceName(def, piece)}[/color]");
            }
        }

        foreach (int id in changes.Upgraded)
        {
            if (owned.Find(p => p.Id == id) is { } piece)
            {
                var def = session.Content.Piece(piece.DefId);
                parts.Add($"[color=#{Palette.Label(Palette.ForPiece(def)).ToHtml(false)}]{CardText.PieceName(def, upgraded: true)}[/color]");
            }
        }

        return string.Join("  ·  ", parts);
    }

    private void Select(int index)
    {
        _selected = index;
        if (Session is { } session && session.Event.Current(session.State) is { } ev)
        {
            ShowSelection(session, ev);
        }
    }

    private void ShowSelection(RunSession session, EventDef ev)
    {
        foreach (var (card, index) in _cards)
        {
            card.SetSelected(index == _selected);
        }

        bool open = _selected is int i && session.Event.CanChoose(session.State, i);
        _action.Disabled = !open;
        _action.Text = open ? ev.Choices[_selected!.Value].Label : "Choose";
    }

    private void Act()
    {
        if (Session is not { State: { Event: { } visit } })
        {
            return;
        }

        if (visit.Choice is null)
        {
            if (_selected is int index)
            {
                GameRoot.Instance.Act(s => s.Event.Choose(s.State, index));
            }
        }
        else
        {
            GameRoot.Instance.Act(s => s.Event.Continue(s.State));
        }
    }
}
