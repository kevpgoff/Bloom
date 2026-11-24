using Bloom.Core.Run;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Draws the ship during a fight: every lane, the hull's rows, the fitted pieces, which ones are
/// disabled or receive a bonus, and the lane and target of each coming heavy attack. Reports taps on
/// pieces through <see cref="PieceTapped"/>. All of it is read from the run; nothing is decided here.
/// </summary>
/// <remarks>
/// It also shows the <see cref="HullWarning"/>: as the hull wears, the open deck darkens toward red, and once
/// the warning is past <see cref="FlickerFrom"/> the whole hologram flickers now and then, more often as it
/// rises. The flicker only changes <see cref="CanvasItem.Modulate"/>, so it needs no redraw.
/// </remarks>
public partial class ShipGridView : Control
{
    /// <summary>Room above the grid for the heavy-attack marker.</summary>
    private const float MarkerHeight = 40f;

    /// <summary>The warning level at which the hologram starts to flicker.</summary>
    private const float FlickerFrom = 0.45f;

    /// <summary>Seconds between flickers when they start, and at full warning; each gap varies by 40% either way.</summary>
    private const float FlickerGapLong = 9f;
    private const float FlickerGapShort = 4f;

    /// <summary>How far a flicker dims the hologram at full warning: 0.3 leaves it at 70% for a moment.</summary>
    private const float FlickerDepth = 0.3f;

    private readonly RandomNumberGenerator _rng = new();
    private float _warning;
    private float _warningTarget;
    private bool _warningSet;
    private float _untilFlicker;
    private float _flickerAge = -1f;
    private RunSession? _session;
    private int? _highlightPieceId;
    private IReadOnlyCollection<int> _selectable = [];
    private IReadOnlyCollection<int> _chosen = [];

    private int? _inspectedPieceId;

    /// <summary>A piece was tapped; the argument is its piece id.</summary>
    public event Action<int>? PieceTapped;

    /// <summary>The view was tapped where no piece is.</summary>
    public event Action? BackgroundTapped;

    /// <summary>The piece whose text the screen is showing, outlined in white. It stays until the screen clears it.</summary>
    public int? InspectedPieceId
    {
        get => _inspectedPieceId;
        set
        {
            if (_inspectedPieceId != value)
            {
                _inspectedPieceId = value;
                QueueRedraw();
            }
        }
    }

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
        _warningTarget = HullWarning.Level(session.State);
        if (!_warningSet)
        {
            // The level the screen opens with shows at once.
            _warningSet = true;
            _warning = _warningTarget;
            _untilFlicker = _rng.RandfRange(1f, 3f);
        }

        SetProcess(_warning > 0f || _warningTarget > 0f);
        QueueRedraw();
    }

    public override void _Ready()
    {
        Resized += QueueRedraw;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_warning != _warningTarget)
        {
            _warning = Mathf.MoveToward(_warning, _warningTarget, dt / HullWarning.EaseTime);
            QueueRedraw();
        }

        float dim = 0f;
        if (_flickerAge >= 0f)
        {
            _flickerAge += dt;
            dim = FlickerShape(_flickerAge);
            if (dim < 0f)
            {
                _flickerAge = -1f;
                dim = 0f;
            }
        }
        else if (_warning >= FlickerFrom)
        {
            _untilFlicker -= dt;
            if (_untilFlicker <= 0f)
            {
                _flickerAge = 0f;
                float strength = (_warning - FlickerFrom) / (1f - FlickerFrom);
                _untilFlicker = Mathf.Lerp(FlickerGapLong, FlickerGapShort, strength) * _rng.RandfRange(0.6f, 1.4f);
            }
        }

        Modulate = Modulate with { A = 1f - (dim * FlickerDepth * _warning) };
        if (_warning <= 0f && _warningTarget <= 0f && _flickerAge < 0f)
        {
            SetProcess(false);
        }
    }

    /// <summary>
    /// How dim the hologram is <paramref name="age"/> seconds into a flicker, from 0 to 1: a short drop, a gap and
    /// a smaller drop, like a projector that loses power for a moment. Below 0 once the flicker is over.
    /// </summary>
    private static float FlickerShape(float age) => age switch
    {
        < 0.05f => 1f,
        < 0.11f => 0f,
        < 0.15f => 0.55f,
        _ => -1f,
    };

    public override void _Draw()
    {
        if (_session is null || ComputeLayout() is not { } g)
        {
            return;
        }

        var run = _session.State;
        var combat = run.Combat;
        IReadOnlyCollection<int> disabled = combat?.DisabledPieceIds ?? [];

        // A boss's body and parts can each make a heavy attack in the same turn.
        var heavies = combat is null ? [] : _session.Combat.NextActions(run).Where(a => a.Lane is not null).ToList();
        var heavyLanes = heavies.Select(a => a.Lane!.Value).Distinct().ToList();
        GridPainter.DrawLanes(this, g, heavyLanes);
        GridPainter.DrawHull(this, g, run.Ship.Grid.Cells, HullWarning.Deck(_warning));
        foreach (int lane in heavyLanes)
        {
            // When two heavy attacks land on one lane, its tag shows the harder one.
            var hardest = heavies.Where(a => a.Lane == lane).MaxBy(a => a.Intent.Amount)!;
            GridPainter.DrawHeavyLane(this, g, lane, hardest.Intent.Amount, hardest.Armor > 0);
        }

        var counted = _session.Fitter.ActivePassives(run.Ship, disabled).Select(p => p.Fitted.Piece.Id).ToHashSet();
        var bonuses = _session.Fitter.ActiveBonuses(run.Ship, disabled);
        var bonusReceivers = bonuses.Select(b => b.ReceiverId).ToHashSet();
        var targetIds = heavies.Where(a => a.Target is not null).Select(a => a.Target!.Piece.Id).ToHashSet();
        foreach (var fitted in run.Ship.Fitted)
        {
            int id = fitted.Piece.Id;
            var def = _session.Content.Piece(fitted.Piece.DefId);
            string name = CardText.PieceName(def, fitted.Piece);
            var cells = _session.Fitter.CellsOf(fitted);
            bool off = disabled.Contains(id);
            var color = Palette.ForPiece(def);
            GridPainter.DrawPiece(this, g, cells, color, offline: off);
            GridPainter.DrawPieceIcon(this, g, cells, ArtLibrary.PieceIcon(def), off ? Palette.Refused with { A = 0.5f } : Palette.Glyph(color), GridPainter.NameLines(g, cells, name));
            GridPainter.DrawLabel(this, g, cells, name, off ? Palette.Label(Palette.Refused) : Palette.Label(color));
            if (off)
            {
                GridPainter.DrawOffTag(this, g, cells);
            }
            else if (def.Passive is not null)
            {
                GridPainter.DrawPassiveMark(this, g, cells, counted.Contains(id));
            }

            if (bonusReceivers.Contains(id))
            {
                GridPainter.DrawBonusBadge(this, g, cells, Palette.Badge);
            }

            if (targetIds.Contains(id))
            {
                GridPainter.DrawOutline(this, g, cells, Palette.Heavy, 5);
            }

            if (_selectable.Contains(id))
            {
                bool chosen = _chosen.Contains(id);
                GridPainter.DrawOutline(this, g, cells, chosen ? Palette.Chosen : Palette.Selectable, chosen ? 8 : 5);
            }

            if (id == _highlightPieceId || id == _inspectedPieceId)
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
        else
        {
            BackgroundTapped?.Invoke();
        }
    }

    /// <summary>Where a piece's passive mark sits, in local coordinates, or null when the piece is not fitted or nothing is drawn yet.</summary>
    public Vector2? PassiveMarkCenter(int pieceId) =>
        _session is not null && ComputeLayout() is { } g && _session.State.Ship.Find(pieceId) is { } fitted
            ? GridPainter.PassiveMark(g, _session.Fitter.CellsOf(fitted)).Center
            : null;

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
