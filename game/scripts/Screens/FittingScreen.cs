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
    private Label _fightHint = null!;
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
        _fightHint = GetNode<Label>("%FightHint");
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
        _next.Text = enemy is null
            ? "Refit the ship here, then choose the next node on the map."
            : $"Next: {enemy.Name}{PoolNote(enemy.Pool)}, hull {enemy.Hull}. Its heavy attacks hit the marked lanes.";

        _undo.Disabled = !session.Refit.CanUndo(run);
        bool prepare = run.Phase == RunPhase.Prepare;
        _fight.Text = prepare ? "Start fight" : "Back to map";
        _fight.Disabled = prepare && !session.Refit.CanStartFight(run);
        int excess = session.Refit.CargoExcess(run);
        _fightHint.Visible = excess > 0;
        _fightHint.Text = excess == 1
            ? $"The cargo hold holds {Rules.CargoCapacity}. Fit or compost 1 piece before the next fight."
            : $"The cargo hold holds {Rules.CargoCapacity}. Fit or compost {excess} pieces before the next fight.";

        ShowInfo(session, run);
    }

    private static string PoolNote(EnemyPool? pool) => pool switch
    {
        EnemyPool.Hard => " (hard fight)",
        EnemyPool.Boss => " (the sector's boss)",
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

        if (selected is int id && PieceDef(session, run, id) is { } def)
        {
            string hint = held
                ? "Drag it onto the ship to fit it. Tap it again to turn it."
                : "Drag it to move it, or onto the cargo hold to unfit it. Tap it again to turn it.";
            _info.Text = $"{CardText.PieceDetails(def, session.Content)}\n[color=#{Palette.Muted.ToHtml(false)}]{hint}[/color]";
            return;
        }

        _info.Text = "Tap a piece to see its cards. Drag a piece to move it; the ship's pieces are its deck.\n" +
                     $"[color=#{Palette.Muted.ToHtml(false)}]Lanes are columns: a heavy attack disables the frontmost piece in its lane.[/color]";
    }

    private static string DragText(RunSession session, RunState run, DragInfo drag)
    {
        var def = PieceDef(session, run, drag.PieceId)!;
        if (drag.OverCargo)
        {
            if (drag.FromCargo)
            {
                return $"[b]{def.Name}[/b]\nDrop it on the ship to fit it.";
            }

            return session.Fitter.CanRemove(run.Ship, drag.PieceId)
                ? $"[b]{def.Name}[/b]\nDrop it here to move it to the cargo hold. Its cards leave the deck."
                : $"[b]{def.Name}[/b]\nThe bridge can be moved but never removed.";
        }

        if (drag.Preview is not { } preview)
        {
            return $"[b]{def.Name}[/b]";
        }

        if (!preview.Fits)
        {
            string why = preview.Error switch
            {
                FitError.OutOfBounds => "it would stick out of the grid",
                FitError.OffHull => "part of it would be off the hull",
                FitError.Overlaps => "it would overlap another piece",
                _ => preview.Error.ToString(),
            };
            return $"[b]{def.Name}[/b]\n[color=#{Palette.Refused.ToHtml(false)}]Doesn't fit here: {why}.[/color]";
        }

        if (preview.Bonuses.Count == 0)
        {
            return $"[b]{def.Name}[/b]\n[color=#{Palette.Boosted.ToHtml(false)}]Fits here.[/color]";
        }

        string NameOf(int pieceId) => PieceDef(session, run, pieceId)?.Name ?? "?";
        var bonuses = preview.Bonuses.Select(b => $"{NameOf(b.GiverId)} → {NameOf(b.ReceiverId)} ({CardText.BonusShort(b.Bonus.Effect)})");
        return $"[b]{def.Name}[/b]\n[color=#{Palette.Boosted.ToHtml(false)}]Fits here and turns on: {string.Join("; ", bonuses)}.[/color]";
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
