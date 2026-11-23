using Bloom.Core;
using Bloom.Core.Combat;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>What the player is dragging, for the info strip.</summary>
/// <param name="Preview">Where it would land on the ship and the bonuses it would turn on; null while it is over the cargo hold.</param>
public sealed record DragInfo(int PieceId, bool FromCargo, bool OverCargo, PlacementPreview? Preview);

/// <summary>
/// The ship and the cargo hold on one surface, so a piece can be dragged between them. It follows the
/// spec's fitting controls:
/// <list type="bullet">
/// <item>Dragging a piece makes it float above the finger, so the thumb doesn't hide it. Where it would
/// land is shown on the grid in green or red, and the pieces it would share a bonus with are outlined.</item>
/// <item>Dropping on the ship fits or moves it; dropping on the cargo hold unfits it.</item>
/// <item>A tap selects a piece (owner, 2026-11-21); a tap on the selected piece turns it clockwise.</item>
/// </list>
/// Every change goes through <see cref="GameRoot.Act"/>; fit checks and bonuses come from <see cref="RefitEngine"/>.
/// </summary>
/// <remarks>
/// A held piece has no position, so its turn is kept here only and is not saved.
/// </remarks>
public partial class FittingBoard : Control
{
    private const float MarkerHeight = 40f;
    private const float CargoHeight = 200f;
    private const float CargoHeader = 34f;
    private const float CargoGap = 16f;
    private const float SlotGap = 12f;
    private const float DragThreshold = 16f;
    private const ulong FlashMs = 450;

    /// <summary>The gap between the finger and the bottom of the dragged piece, in cells.</summary>
    private const float FloatCells = 0.4f;

    private readonly Dictionary<int, Rotation> _cargoRotation = [];
    private Press? _press;
    private Drag? _drag;
    private int? _flashPieceId;
    private ulong _flashUntil;

    /// <summary>The selection or the drag changed.</summary>
    public event Action? StateChanged;

    public int? SelectedPieceId { get; private set; }

    public DragInfo? Dragging =>
        _drag is null ? null : new DragInfo(_drag.PieceId, _drag.FromCargo, _drag.OverCargo, _drag.OverCargo ? null : _drag.Preview);

    private static RunSession? Session => GameRoot.Instance.Session;

    private Rect2 CargoArea => new(0, Size.Y - CargoHeight, Size.X, CargoHeight);

    public override void _Ready()
    {
        Resized += QueueRedraw;
        GameRoot.Instance.Changed += OnRunChanged;
        SetProcess(false);
    }

    public override void _ExitTree() => GameRoot.Instance.Changed -= OnRunChanged;

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() >= _flashUntil)
        {
            _flashPieceId = null;
            SetProcess(false);
            QueueRedraw();
        }
    }

    /// <summary>The rotation the board would use for a piece: its fitted rotation, or its on-screen one in the hold.</summary>
    public Rotation RotationOf(RunState run, int pieceId) =>
        run.Ship.Find(pieceId)?.Rotation ?? _cargoRotation.GetValueOrDefault(pieceId);

    public override void _GuiInput(InputEvent @event)
    {
        if (Session is not { } session)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                var hit = PieceAt(session, down.Position);
                _press = new Press(hit?.PieceId, hit?.FromCargo ?? false, down.Position);
                AcceptEvent();
                break;

            case InputEventMouseMotion motion when _press is not null:
                if ((motion.ButtonMask & MouseButtonMask.Left) == 0)
                {
                    CancelPointer();
                }
                else
                {
                    if (_drag is null && _press.PieceId is int id && RefitEngine.CanRefit(session.State)
                        && motion.Position.DistanceTo(_press.Start) > DragThreshold)
                    {
                        _drag = new Drag(id, _press.FromCargo, RotationOf(session.State, id));
                        SelectedPieceId = id;
                    }

                    if (_drag is not null)
                    {
                        UpdateDrag(session, motion.Position);
                    }
                }

                AcceptEvent();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up when _press is not null:
                var press = _press;
                var drag = _drag;
                _press = null;
                _drag = null;
                if (drag is not null)
                {
                    UpdateDrag(session, up.Position, drag);
                    Drop(session, drag, up.Position);
                }
                else
                {
                    Tap(session, press);
                }

                AcceptEvent();
                QueueRedraw();
                StateChanged?.Invoke();
                break;
        }
    }

    public override void _Draw()
    {
        if (Session is not { } session)
        {
            return;
        }

        var run = session.State;
        var font = GetThemeDefaultFont();
        if (GridLayoutFor(session) is { } g)
        {
            DrawShip(session, run, g);
        }

        DrawCargo(session, run, font);
        if (_drag is not null && GridLayoutFor(session) is { } layout)
        {
            DrawFloatingPiece(session, _drag, layout);
        }
    }

    private void DrawShip(RunSession session, RunState run, GridLayout g)
    {
        var heavyLanes = session.Refit.NextEnemy(run) is { } enemy
            ? enemy.Pattern.Where(i => i.Kind == IntentKind.HeavyAttack).Select(i => run.Ship.Grid.LaneHit(i.Lane!.Value)).Distinct().ToList()
            : [];
        GridPainter.DrawLanes(this, g, heavyLanes);
        GridPainter.DrawHull(this, g, run.Ship.Grid.Cells);
        foreach (int lane in heavyLanes)
        {
            GridPainter.DrawHeavyLane(this, g, lane);
        }

        var bonuses = session.Fitter.ActiveBonuses(run.Ship, []);
        var receivers = bonuses.Select(b => b.ReceiverId).ToHashSet();
        foreach (var fitted in run.Ship.Fitted)
        {
            int id = fitted.Piece.Id;
            var def = session.Content.Piece(fitted.Piece.DefId);
            string name = CardText.PieceName(def, fitted.Piece);
            var cells = session.Fitter.CellsOf(fitted);
            bool lifted = _drag?.PieceId == id;
            var color = Palette.ForPiece(def);
            GridPainter.DrawPiece(this, g, cells, lifted ? color with { A = 0.25f } : color);
            GridPainter.DrawPieceIcon(this, g, cells, ArtLibrary.PieceIcon(def), lifted ? Palette.Muted with { A = 0.35f } : Palette.Glyph(color), GridPainter.NameLines(g, cells, name));
            GridPainter.DrawLabel(this, g, cells, name, lifted ? Palette.Muted with { A = 0.5f } : Palette.Label(color));
            if (!lifted && receivers.Contains(id))
            {
                GridPainter.DrawBonusBadge(this, g, cells, Palette.Badge);
            }

            if (!lifted && id == SelectedPieceId)
            {
                GridPainter.DrawOutline(this, g, cells, Colors.White, 4);
            }

            if (id == _flashPieceId)
            {
                GridPainter.DrawOutline(this, g, cells, Palette.Refused, 6);
            }
        }

        foreach (var bonus in bonuses)
        {
            if (bonus.GiverId != _drag?.PieceId && bonus.ReceiverId != _drag?.PieceId
                && run.Ship.Find(bonus.GiverId) is { } giver && run.Ship.Find(bonus.ReceiverId) is { } receiver)
            {
                GridPainter.DrawBonusLink(this, g, session.Fitter.CellsOf(giver), session.Fitter.CellsOf(receiver), Palette.Boosted);
            }
        }

        // Where the dragged piece would land, and the pieces it would share a bonus with there.
        if (_drag is { OverCargo: false, Origin: Cell origin, Preview: { } preview })
        {
            var landing = PieceShape(session, _drag.PieceId).CellsAt(origin, Bloom.Core.Grid.Rotation.R0)
                .Where(ShipGrid.InBounds)
                .ToList();
            GridPainter.DrawPiece(this, g, landing, preview.Fits ? Palette.FitOk : Palette.FitBad);
            foreach (var bonus in preview.Bonuses)
            {
                int other = bonus.GiverId == _drag.PieceId ? bonus.ReceiverId : bonus.GiverId;
                if (run.Ship.Find(other) is { } partner)
                {
                    GridPainter.DrawOutline(this, g, session.Fitter.CellsOf(partner), Palette.Boosted, 5);
                }
            }
        }
    }

    private void DrawCargo(RunSession session, RunState run, Font font)
    {
        var area = CargoArea;
        DrawRect(area, Palette.CargoBack);
        DrawRect(area, Palette.HoloLine with { A = 0.35f }, filled: false, width: 1.2f);
        if (_drag is { FromCargo: false, OverCargo: true })
        {
            DrawRect(area, Palette.CargoDrop);
        }

        var pieces = run.Cargo.Pieces;
        string header = $"Cargo hold {pieces.Count} / {Rules.CargoCapacity}";
        DrawString(font, area.Position + new Vector2(10, 26), header, HorizontalAlignment.Left, area.Size.X - 20, 22,
            run.Cargo.Excess > 0 ? Palette.Heavy : Palette.Muted);

        var slots = CargoSlots(pieces.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            DrawRect(slot, i >= Rules.CargoCapacity ? Palette.CargoOver : Palette.CargoSlot);
            DrawRect(slot, Palette.LineMuted, filled: false, width: 1.5f);
            if (i >= pieces.Count)
            {
                continue;
            }

            var piece = pieces[i];
            var def = session.Content.Piece(piece.DefId);
            var shape = def.Shape.Rotated(_cargoRotation.GetValueOrDefault(piece.Id));
            const float labelHeight = 34;
            float cell = Mathf.Floor(Mathf.Min(36, Mathf.Min((slot.Size.X - 16) / shape.Width, (slot.Size.Y - 16 - labelHeight) / shape.Height)));
            var origin = slot.Position + new Vector2((slot.Size.X - (shape.Width * cell)) / 2, 8 + ((slot.Size.Y - 16 - labelHeight - (shape.Height * cell)) / 2));
            var layout = new GridLayout(cell, origin, 0, shape.Height);
            bool lifted = _drag?.PieceId == piece.Id;
            var color = Palette.ForPiece(def);
            GridPainter.DrawPiece(this, layout, shape.Cells, lifted ? color with { A = 0.25f } : color);
            GridPainter.DrawPieceIcon(this, layout, shape.Cells, ArtLibrary.PieceIcon(def), lifted ? Palette.Muted with { A = 0.35f } : Palette.Glyph(color), labelLines: 0);
            DrawString(BloomTheme.Hud, new Vector2(slot.Position.X + 4, slot.End.Y - 10), CardText.PieceName(def, piece).ToUpperInvariant(), HorizontalAlignment.Center, slot.Size.X - 8, 19,
                lifted ? Palette.Muted with { A = 0.5f } : Palette.Label(color));
            if (!lifted && piece.Id == SelectedPieceId)
            {
                DrawRect(slot.Grow(-2), Palette.Selectable, filled: false, width: 3);
            }
        }
    }

    /// <summary>The dragged piece, floating above the finger at full grid size.</summary>
    private void DrawFloatingPiece(RunSession session, Drag drag, GridLayout g)
    {
        var shape = PieceShape(session, drag.PieceId, drag.Rotation);
        var layout = new GridLayout(g.CellSize, FloatingTopLeft(shape, g, drag.Pointer), 0, shape.Height);
        var def = session.Content.Piece(PieceInstanceOf(session.State, drag.PieceId).DefId);
        GridPainter.DrawPiece(this, layout, shape.Cells, Palette.ForPiece(def) with { A = 0.9f });
        GridPainter.DrawPieceIcon(this, layout, shape.Cells, ArtLibrary.PieceIcon(def), Palette.Glyph(Palette.ForPiece(def)), labelLines: 0);
        GridPainter.DrawOutline(this, layout, shape.Cells, Palette.Selectable, 3);
    }

    private void UpdateDrag(RunSession session, Vector2 pointer, Drag? drag = null)
    {
        drag ??= _drag!;
        drag.Pointer = pointer;
        drag.OverCargo = CargoArea.HasPoint(pointer);
        if (GridLayoutFor(session) is { } g)
        {
            var shape = PieceShape(session, drag.PieceId, drag.Rotation);
            var topLeft = FloatingTopLeft(shape, g, pointer) - g.Origin;
            var origin = new Cell(Mathf.RoundToInt(topLeft.X / g.CellSize), Mathf.RoundToInt(topLeft.Y / g.CellSize) + g.MinRow);
            if (origin != drag.Origin || drag.Preview is null)
            {
                drag.Origin = origin;
                drag.Preview = session.Refit.PreviewPlacement(session.State, drag.PieceId, origin, drag.Rotation);
            }
        }

        QueueRedraw();
        StateChanged?.Invoke();
    }

    private void Drop(RunSession session, Drag drag, Vector2 pointer)
    {
        var run = session.State;
        int id = drag.PieceId;
        if (CargoArea.HasPoint(pointer))
        {
            if (!drag.FromCargo)
            {
                if (session.Fitter.CanRemove(run.Ship, id))
                {
                    GameRoot.Instance.Act(s => s.Refit.Unfit(s.State, id));
                }
                else
                {
                    Flash(id);
                }
            }

            return;
        }

        if (drag is not { Origin: Cell origin, Preview.Fits: true })
        {
            Flash(id);
            return;
        }

        var rotation = drag.Rotation;
        if (drag.FromCargo)
        {
            if (GameRoot.Instance.Act(s => s.Refit.FitFromCargo(s.State, id, origin, rotation)))
            {
                _cargoRotation.Remove(id);
            }
        }
        else if (run.Ship.Find(id) is { } fitted && (fitted.Origin != origin || fitted.Rotation != rotation))
        {
            GameRoot.Instance.Act(s => s.Refit.Move(s.State, id, origin, rotation));
        }
    }

    private void Tap(RunSession session, Press press)
    {
        if (press.PieceId is not int id)
        {
            SelectedPieceId = null;
            return;
        }

        if (SelectedPieceId != id)
        {
            SelectedPieceId = id;
            return;
        }

        // A tap on the selected piece turns it clockwise.
        if (press.FromCargo)
        {
            _cargoRotation[id] = _cargoRotation.GetValueOrDefault(id).Clockwise();
        }
        else if (session.Refit.CanRotate(session.State, id))
        {
            GameRoot.Instance.Act(s => s.Refit.Rotate(s.State, id));
        }
        else
        {
            Flash(id);
        }
    }

    private void CancelPointer()
    {
        _press = null;
        _drag = null;
        QueueRedraw();
        StateChanged?.Invoke();
    }

    private void Flash(int pieceId)
    {
        _flashPieceId = pieceId;
        _flashUntil = Time.GetTicksMsec() + FlashMs;
        SetProcess(true);
        QueueRedraw();
    }

    /// <summary>Drops the selection and on-screen turns of pieces that are gone (composted or undone).</summary>
    private void OnRunChanged()
    {
        if (Session?.State is not { } run)
        {
            return;
        }

        bool Owned(int id) => run.Ship.Find(id) is not null || run.Cargo.Pieces.Exists(p => p.Id == id);
        if (SelectedPieceId is int selected && !Owned(selected))
        {
            SelectedPieceId = null;
        }

        foreach (int id in _cargoRotation.Keys.Where(id => !run.Cargo.Pieces.Exists(p => p.Id == id)).ToList())
        {
            _cargoRotation.Remove(id);
        }

        QueueRedraw();
        StateChanged?.Invoke();
    }

    private (int PieceId, bool FromCargo)? PieceAt(RunSession session, Vector2 point)
    {
        var run = session.State;
        if (GridLayoutFor(session) is { } g && g.Bounds.HasPoint(point))
        {
            var cell = g.CellAt(point);
            if (run.Ship.Fitted.FirstOrDefault(f => session.Fitter.CellsOf(f).Contains(cell)) is { } fitted)
            {
                return (fitted.Piece.Id, false);
            }
        }

        var slots = CargoSlots(run.Cargo.Pieces.Count);
        for (int i = 0; i < run.Cargo.Pieces.Count; i++)
        {
            if (slots[i].HasPoint(point))
            {
                return (run.Cargo.Pieces[i].Id, true);
            }
        }

        return null;
    }

    /// <summary>One slot per held piece, and never fewer than the capacity.</summary>
    private List<Rect2> CargoSlots(int held)
    {
        var area = CargoArea;
        int count = Math.Max(Rules.CargoCapacity, held);
        float width = (area.Size.X - (SlotGap * (count - 1))) / count;
        float top = area.Position.Y + CargoHeader;
        float height = area.Size.Y - CargoHeader - 8;
        return Enumerable.Range(0, count)
            .Select(i => new Rect2(area.Position.X + (i * (width + SlotGap)), top, width, height))
            .ToList();
    }

    private GridLayout? GridLayoutFor(RunSession? session) =>
        session is null
            ? null
            : GridLayout.ForHull(new Rect2(0, 0, Size.X, Size.Y - CargoHeight - CargoGap), session.State.Ship.Grid.Cells, MarkerHeight);

    /// <summary>The top-left corner of a floating piece: centered on the finger, with its bottom just above it.</summary>
    private static Vector2 FloatingTopLeft(Shape shape, GridLayout g, Vector2 pointer) =>
        pointer - new Vector2(shape.Width * g.CellSize / 2, (shape.Height + FloatCells) * g.CellSize);

    private Shape PieceShape(RunSession session, int pieceId, Rotation? rotation = null) =>
        session.Content.Piece(PieceInstanceOf(session.State, pieceId).DefId).Shape
            .Rotated(rotation ?? _drag?.Rotation ?? RotationOf(session.State, pieceId));

    private static PieceInstance PieceInstanceOf(RunState run, int pieceId) =>
        run.Ship.Find(pieceId)?.Piece
        ?? run.Cargo.Pieces.Find(p => p.Id == pieceId)
        ?? throw new InvalidOperationException($"Piece {pieceId} is neither fitted nor held.");

    private sealed record Press(int? PieceId, bool FromCargo, Vector2 Start);

    private sealed class Drag(int pieceId, bool fromCargo, Rotation rotation)
    {
        public int PieceId { get; } = pieceId;

        public bool FromCargo { get; } = fromCargo;

        public Rotation Rotation { get; } = rotation;

        public Vector2 Pointer { get; set; }

        public bool OverCargo { get; set; }

        public Cell? Origin { get; set; }

        public PlacementPreview? Preview { get; set; }
    }
}
