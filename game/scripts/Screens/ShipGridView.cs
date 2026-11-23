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

        int? heavyLane = combat is not null && _session.Combat.CurrentIntent(combat) is { Kind: IntentKind.HeavyAttack, Lane: int lane }
            ? lane
            : null;
        GridPainter.DrawLanes(this, g, heavyLane is int marked ? [marked] : []);
        GridPainter.DrawHull(this, g, run.Ship.Grid.Cells);
        if (heavyLane is int heavy)
        {
            GridPainter.DrawHeavyLane(this, g, heavy);
        }

        var bonuses = _session.Fitter.ActiveBonuses(run.Ship, disabled);
        var bonusReceivers = bonuses.Select(b => b.ReceiverId).ToHashSet();
        int? targetId = combat is null ? null : _session.Combat.HeavyAttackTarget(run)?.Piece.Id;
        foreach (var fitted in run.Ship.Fitted)
        {
            int id = fitted.Piece.Id;
            var def = _session.Content.Piece(fitted.Piece.DefId);
            var cells = _session.Fitter.CellsOf(fitted);
            bool off = disabled.Contains(id);
            var color = Palette.ForPiece(def);
            GridPainter.DrawPiece(this, g, cells, color, offline: off);
            GridPainter.DrawPieceIcon(this, g, cells, ArtLibrary.PieceIcon(def), off ? Palette.Refused with { A = 0.5f } : Palette.Glyph(color), GridPainter.NameLines(g, cells, def.Name));
            GridPainter.DrawLabel(this, g, cells, def.Name, off ? Palette.Label(Palette.Refused) : Palette.Label(color));
            if (off)
            {
                GridPainter.DrawOffTag(this, g, cells);
            }

            if (bonusReceivers.Contains(id))
            {
                GridPainter.DrawBonusBadge(this, g, cells, Palette.Badge);
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

        foreach (var bonus in bonuses)
        {
            if (run.Ship.Find(bonus.GiverId) is { } giver && run.Ship.Find(bonus.ReceiverId) is { } receiver)
            {
                GridPainter.DrawBonusLink(this, g, _session.Fitter.CellsOf(giver), _session.Fitter.CellsOf(receiver), Palette.Boosted);
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

    /// <summary>The cells of a fitted piece, in local coordinates. Empty when the piece is not fitted or nothing is drawn yet.</summary>
    public IReadOnlyList<Rect2> PieceCellRects(int pieceId) =>
        _session is not null && ComputeLayout() is { } g && _session.State.Ship.Find(pieceId) is { } fitted
            ? _session.Fitter.CellsOf(fitted).Select(g.CellRect).ToList()
            : [];

    /// <summary>Where a piece's glyph sits, in local coordinates: the point effects start from and land on.</summary>
    public Vector2? PieceCenter(int pieceId) =>
        _session is not null && ComputeLayout() is { } g && _session.State.Ship.Find(pieceId) is { } fitted
            ? GridPainter.IconAnchor(g, _session.Fitter.CellsOf(fitted)).Center
            : null;

    /// <summary>A lane's column over the hull's rows, in local coordinates.</summary>
    public Rect2? LaneRect(int lane) => ComputeLayout()?.LaneRect(lane);

    /// <summary>The box around the hull's cells, in local coordinates; the ship's outline for shield effects.</summary>
    public Rect2? HullBounds()
    {
        if (_session is null || ComputeLayout() is not { } g)
        {
            return null;
        }

        var cells = _session.State.Ship.Grid.Cells;
        var first = g.CellRect(cells.First());
        return cells.Aggregate(first, (box, cell) => box.Merge(g.CellRect(cell)));
    }

    /// <summary>All lanes are drawn, but only the rows the hull covers, so the cells stay large.</summary>
    private GridLayout? ComputeLayout() =>
        _session is null ? null : GridLayout.ForHull(new Rect2(Vector2.Zero, Size), _session.State.Ship.Grid.Cells, MarkerHeight);
}
