using Bloom.Core.Combat;
using Bloom.Core.Run;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Draws the ship during a fight: every lane, the hull's rows, the fitted pieces, which ones are
/// disabled or receive a bonus, and the lane and target of a coming heavy attack. Reports taps on
/// pieces through <see cref="PieceTapped"/>. All of it is read from the run; nothing is decided here.
/// </summary>
public partial class ShipGridView : Control
{
    /// <summary>Room above the grid for the heavy-attack marker.</summary>
    private const float MarkerHeight = 40f;

    private RunSession? _session;
    private int? _highlightPieceId;
    private IReadOnlyCollection<int> _selectable = [];
    private IReadOnlyCollection<int> _chosen = [];

    /// <summary>A piece was tapped; the argument is its piece id.</summary>
    public event Action<int>? PieceTapped;

    /// <summary>The piece behind the card under the finger or the mouse, outlined in white.</summary>
    public int? HighlightPieceId
    {
        get => _highlightPieceId;
        set
        {
            if (_highlightPieceId != value)
            {
                _highlightPieceId = value;
                QueueRedraw();
            }
        }
    }

    /// <summary>Sets what to draw. <paramref name="selectable"/> and <paramref name="chosen"/> are for picking pieces to repair.</summary>
    public void SetState(RunSession session, IReadOnlyCollection<int> selectable, IReadOnlyCollection<int> chosen)
    {
        _session = session;
        _selectable = selectable;
        _chosen = chosen;
        _highlightPieceId = null;
        QueueRedraw();
    }

    public override void _Ready() => Resized += QueueRedraw;

    public override void _Draw()
    {
        if (_session is null || ComputeLayout() is not { } g)
        {
            return;
        }

        var run = _session.State;
        var combat = run.Combat;
        IReadOnlyCollection<int> disabled = combat?.DisabledPieceIds ?? [];
        var font = GetThemeDefaultFont();

        GridPainter.DrawLanes(this, g);
        if (combat is not null && _session.Combat.CurrentIntent(combat) is { Kind: IntentKind.HeavyAttack, Lane: int lane })
        {
            GridPainter.DrawHeavyLane(this, g, lane);
        }

        GridPainter.DrawHull(this, g, run.Ship.Grid.Cells);
        var bonusReceivers = _session.Fitter.ActiveBonuses(run.Ship, disabled).Select(b => b.ReceiverId).ToHashSet();
        int? targetId = combat is null ? null : _session.Combat.HeavyAttackTarget(run)?.Piece.Id;
        foreach (var fitted in run.Ship.Fitted)
        {
            int id = fitted.Piece.Id;
            var def = _session.Content.Piece(fitted.Piece.DefId);
            var cells = _session.Fitter.CellsOf(fitted);
            bool off = disabled.Contains(id);
            GridPainter.DrawPiece(this, g, cells, off ? Palette.ForPiece(def).Darkened(0.65f) : Palette.ForPiece(def));
            if (off)
            {
                GridPainter.DrawDisabledOverlay(this, g, cells);
            }

            GridPainter.DrawPieceIcon(this, g, cells, ArtLibrary.PieceIcon(def), off ? Palette.DisabledText with { A = 0.55f } : Palette.Ink);
            GridPainter.DrawLabel(this, font, g, cells, def.Name, off ? Palette.DisabledText : Palette.PieceText);
            if (off)
            {
                GridPainter.DrawOffTag(this, font, g, cells);
            }

            if (bonusReceivers.Contains(id))
            {
                GridPainter.DrawBonusBadge(this, font, g, cells, Palette.Badge);
            }

            if (id == targetId)
            {
                GridPainter.DrawOutline(this, g, cells, Palette.Heavy, 5);
            }

            if (_selectable.Contains(id))
            {
                bool chosen = _chosen.Contains(id);
                GridPainter.DrawOutline(this, g, cells, chosen ? Palette.Chosen : Palette.Selectable, chosen ? 8 : 5);
            }

            if (id == _highlightPieceId)
            {
                GridPainter.DrawOutline(this, g, cells, Colors.White, 4);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click
            || _session is null
            || ComputeLayout() is not { } g)
        {
            return;
        }

        var cell = g.CellAt(click.Position);
        if (_session.State.Ship.Fitted.FirstOrDefault(f => _session.Fitter.CellsOf(f).Contains(cell)) is { } fitted)
        {
            AcceptEvent();
            PieceTapped?.Invoke(fitted.Piece.Id);
        }
    }

    /// <summary>All lanes are drawn, but only the rows the hull covers, so the cells stay large.</summary>
    private GridLayout? ComputeLayout() =>
        _session is null ? null : GridLayout.ForHull(new Rect2(Vector2.Zero, Size), _session.State.Ship.Grid.Cells, MarkerHeight);
}
