using Bloom.Core;
using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Refitting: the ship and cargo hold on a <see cref="FittingBoard"/>, an info strip for the selected or
/// dragged piece, Undo, and one main button. Opened from the map, that button goes back to the map; after
/// choosing a fight node it names the enemy and starts the fight. Whether the fight can start, and what a
/// placement would do, come from <see cref="RefitEngine"/>.
/// </summary>
public partial class FittingScreen : Control
{
    private Label _status = null!;
    private Label _next = null!;
    private FittingBoard _board = null!;
    private RichTextLabel _info = null!;
    private Button _compost = null!;
    private Button _undo = null!;
    private Button _fight = null!;

    private static RunSession? Session => GameRoot.Instance.Session;

    public override void _Ready()
    {
        _status = GetNode<Label>("%StatusLabel");
        _next = GetNode<Label>("%NextLabel");
        _board = GetNode<FittingBoard>("%Board");
        _info = GetNode<RichTextLabel>("%InfoText");
        _compost = GetNode<Button>("%CompostButton");
        _undo = GetNode<Button>("%UndoButton");
        _fight = GetNode<Button>("%FightButton");
        BloomTheme.AttachPanelFrame(GetNode<Control>("Layout/InfoPanel"));

        _undo.Pressed += () => GameRoot.Instance.Act(s => s.Refit.Undo(s.State));
        _fight.Pressed += OnMainButton;
        _compost.Text = $"Compost +{Rules.CompostSalvage}";
        _compost.Pressed += Compost;
        _board.StateChanged += Refresh;
        GameRoot.Instance.Changed += Refresh;

        Refresh();
        if (Session?.State is { } run)
        {
            GD.Print($"FittingScreen: hull {run.Hull}/{run.MaxHull}, salvage {run.Salvage}, " +
                     $"next {Session.Refit.NextEnemy(run)?.Name ?? "none chosen"}, cargo {run.Cargo.Pieces.Count}/{Rules.CargoCapacity}.");
        }
    }

    public override void _ExitTree() => GameRoot.Instance.Changed -= Refresh;

    private void Refresh()
    {
        if (Session is not { } session)
        {
            return;
        }

        var run = session.State;
        int deck = DeckBuilder.Build(run.Ship, session.Content).Count;
        _status.Text = $"Hull {run.Hull} / {run.MaxHull}  ·  Salvage {run.Salvage}  ·  Deck {deck}";

        var enemy = session.Refit.NextEnemy(run);
        _next.Visible = enemy is not null;
        _next.Text = enemy is null ? "" : $"Next: {enemy.Name}{PoolNote(enemy.Pool)}  ·  Hull {enemy.Hull}";

        _undo.Disabled = !session.Refit.CanUndo(run);
        bool prepare = run.Phase == RunPhase.Prepare;
        _fight.Text = prepare ? "Start fight" : "Back to map";
        _fight.Disabled = prepare && !session.Refit.CanStartFight(run);

        ShowInfo(session, run);
    }

    private static string PoolNote(EnemyPool? pool) => pool switch
    {
        EnemyPool.Hard => "  ·  Hard fight",
        EnemyPool.Boss => "  ·  Boss",
        _ => "",
    };

    private static void OnMainButton()
    {
        if (Session?.State.Phase == RunPhase.Prepare)
        {
            GameRoot.Instance.Act(s => s.Refit.StartFight(s.State));
        }
        else
        {
            GameRoot.Instance.ShowShip(false);
        }
    }

    private void ShowInfo(RunSession session, RunState run)
    {
        int? selected = _board.SelectedPieceId;
        bool held = selected is int heldId && run.Cargo.Pieces.Exists(p => p.Id == heldId);
        _compost.Visible = held && _board.Dragging is null;

        if (_board.Dragging is { } drag)
        {
            _info.Text = DragText(session, run, drag);
            return;
        }

        _info.Text = selected is int id && PieceDef(session, run, id) is { } def
            ? CardText.PieceDetails(def, session.Content)
            : "";
    }

    private static string DragText(RunSession session, RunState run, DragInfo drag)
    {
        var def = PieceDef(session, run, drag.PieceId)!;
        if (drag.OverCargo)
        {
            return drag.FromCargo || session.Fitter.CanRemove(run.Ship, drag.PieceId)
                ? $"[b]{def.Name}[/b]"
                : $"[b]{def.Name}[/b]\n[color=#{Palette.Refused.ToHtml(false)}]The bridge stays on the ship.[/color]";
        }

        if (drag.Preview is not { } preview)
        {
            return $"[b]{def.Name}[/b]";
        }

        if (!preview.Fits)
        {
            string why = preview.Error switch
            {
                FitError.OutOfBounds => "off the grid",
                FitError.OffHull => "off the hull",
                FitError.Overlaps => "overlaps a piece",
                _ => preview.Error.ToString(),
            };
            return $"[b]{def.Name}[/b]\n[color=#{Palette.Refused.ToHtml(false)}]Doesn't fit: {why}.[/color]";
        }

        if (preview.Bonuses.Count == 0)
        {
            return $"[b]{def.Name}[/b]";
        }

        string NameOf(int pieceId) => PieceDef(session, run, pieceId)?.Name ?? "?";
        var bonuses = preview.Bonuses.Select(b => $"{NameOf(b.GiverId)} → {NameOf(b.ReceiverId)} ({CardText.BonusShort(b.Bonus.Effect)})");
        return $"[b]{def.Name}[/b]\n[color=#{Palette.Boosted.ToHtml(false)}]{string.Join("; ", bonuses)}[/color]";
    }

    private static PieceDef? PieceDef(RunSession session, RunState run, int pieceId)
    {
        var piece = run.Ship.Find(pieceId)?.Piece ?? run.Cargo.Pieces.Find(p => p.Id == pieceId);
        return piece is null ? null : session.Content.Piece(piece.DefId);
    }

    private void Compost()
    {
        if (_board.SelectedPieceId is int id)
        {
            GameRoot.Instance.Act(s => s.Refit.Compost(s.State, id));
        }
    }
}
