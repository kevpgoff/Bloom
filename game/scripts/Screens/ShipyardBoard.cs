using Bloom.Core;
using Bloom.Core.Grid;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Visuals;
using Godot;
using GridRotation = Bloom.Core.Grid.Rotation;

namespace Bloom.Game.Screens;

/// <summary>
/// The whole grid at a shipyard: every lane and row, the hull and its pieces, the free cells around it, and
/// the chosen hull section as a ghost, green where it can attach and red where it cannot.
/// <list type="bullet">
/// <item>Dragging the ghost makes it float above the finger, as pieces do on the fitting board, and shows
/// where it would land.</item>
/// <item>A tap on the ghost turns it clockwise, to the nearest place it fits that way round.</item>
/// <item>A tap anywhere else moves it to the nearest place it fits.</item>
/// </list>
/// Where the ghost is, is kept here only and is not saved; the screen attaches it through <see cref="GameRoot.Act"/>.
/// Fit checks come from <see cref="ShipyardEngine"/>.
/// </summary>
public partial class ShipyardBoard : Control
{
    private const float MarkerHeight = 40f;
    private const float DragThreshold = 16f;

    /// <summary>The gap between the finger and the bottom of the dragged section, in cells.</summary>
    private const float FloatCells = 0.4f;

    /// <summary>How far, in cells, a turn may shift the section to find a place it fits.</summary>
    private const float TurnReach = 1.5f;

    private Shape? _section;
    private Vector2? _pressStart;
    private bool _pressOnGhost;
    private Vector2? _dragPointer;

    /// <summary>The ghost moved, turned or changed.</summary>
    public event Action? GhostChanged;

    /// <summary>The top-left corner of the ghost's rotated shape on the grid.</summary>
    public Cell GhostOrigin { get; private set; }

    public GridRotation GhostRotation { get; private set; }

    /// <summary>Whether the ghost can attach where it is now.</summary>
    public bool Fits => _section is { } shape && Grid is { } grid && grid.CanAttach(shape.CellsAt(GhostOrigin, GhostRotation)) == AttachError.None;

    private static ShipGrid? Grid => GameRoot.Instance.Session?.State.Ship.Grid;

    /// <summary>Shows a section as the ghost, where it shares the most edges with the hull; null hides the ghost.</summary>
    public void ShowSection(Shape? section)
    {
        _section = section;
        _dragPointer = null;
        if (section is not null && Grid is { } grid)
        {
            // Snug against the hull, front row first on a tie.
            var placements = ShipyardEngine.Placements(grid, section).ToList();
            if (placements.Count > 0)
            {
                (GhostOrigin, GhostRotation) = placements.MaxBy(p => SharedEdges(grid, section.CellsAt(p.Origin, p.Rotation)));
            }
            else
            {
                (GhostOrigin, GhostRotation) = (new Cell(0, 0), GridRotation.R0);
            }
        }

        QueueRedraw();
        GhostChanged?.Invoke();
    }

    public override void _Ready() => Resized += QueueRedraw;

    public override void _GuiInput(InputEvent @event)
    {
        if (_section is null || Layout() is not { } g)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                _pressStart = down.Position;
                _pressOnGhost = _section.CellsAt(GhostOrigin, GhostRotation).Contains(g.CellAt(down.Position));
                AcceptEvent();
                break;

            case InputEventMouseMotion motion when _pressStart is { } start:
                if ((motion.ButtonMask & MouseButtonMask.Left) == 0)
                {
                    _pressStart = null;
                    _dragPointer = null;
                    QueueRedraw();
                }
                else if (_pressOnGhost && (_dragPointer is not null || motion.Position.DistanceTo(start) > DragThreshold))
                {
                    _dragPointer = motion.Position;
                    QueueRedraw();
                }

                AcceptEvent();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up when _pressStart is not null:
                if (_dragPointer is not null)
                {
                    GhostOrigin = Landing(_section, g, up.Position);
                }
                else if (_pressOnGhost)
                {
                    Turn();
                }
                else
                {
                    MoveNear(g.CellAt(up.Position));
                }

                _pressStart = null;
                _dragPointer = null;
                AcceptEvent();
                QueueRedraw();
                GhostChanged?.Invoke();
                break;
        }
    }

    public override void _Draw()
    {
        if (GameRoot.Instance.Session is not { } session || Layout() is not { } g)
        {
            return;
        }

        var run = session.State;
        GridPainter.DrawLanes(this, g);

        // The free cells, where the hull can grow.
        for (int y = 0; y < Rules.GridRows; y++)
        {
            for (int x = 0; x < Rules.GridColumns; x++)
            {
                if (!run.Ship.Grid.Contains(new Cell(x, y)))
                {
                    var r = g.CellRect(new Cell(x, y)).Grow(-g.CellSize * 0.12f);
                    DrawRect(r, Palette.LineMuted with { A = 0.18f });
                    DrawRect(r, Palette.LineMuted, filled: false, width: 1f);
                }
            }
        }

        GridPainter.DrawHull(this, g, run.Ship.Grid.Cells);
        foreach (var fitted in run.Ship.Fitted)
        {
            var def = session.Content.Piece(fitted.Piece.DefId);
            var cells = session.Fitter.CellsOf(fitted);
            var color = Palette.ForPiece(def) with { A = 0.6f };
            GridPainter.DrawPiece(this, g, cells, color);
            GridPainter.DrawPieceIcon(this, g, cells, ArtLibrary.PieceIcon(def), Palette.Glyph(color), labelLines: 0);
        }

        if (_section is not { } section)
        {
            return;
        }

        if (_dragPointer is { } pointer)
        {
            var landing = section.CellsAt(Landing(section, g, pointer), GhostRotation);
            GridPainter.DrawPiece(this, g, landing, run.Ship.Grid.CanAttach(landing) == AttachError.None ? Palette.FitOk : Palette.FitBad);

            var shape = section.Rotated(GhostRotation);
            var floating = new GridLayout(g.CellSize, FloatingTopLeft(shape, g, pointer), 0, shape.Height);
            GridPainter.DrawHull(this, floating, shape.Cells);
            GridPainter.DrawOutline(this, floating, shape.Cells, Palette.Selectable, 3);
            return;
        }

        var ghost = section.CellsAt(GhostOrigin, GhostRotation);
        GridPainter.DrawPiece(this, g, ghost, Fits ? Palette.FitOk : Palette.FitBad);
        GridPainter.DrawOutline(this, g, ghost, Palette.Selectable, 3);
    }

    /// <summary>Turns the ghost clockwise, shifting it a little to the nearest place it fits that way round, if any.</summary>
    private void Turn()
    {
        if (_section is not { } section || Grid is not { } grid)
        {
            return;
        }

        var center = CenterOf(section, GhostOrigin, GhostRotation);
        var turned = GhostRotation.Clockwise();
        var near = ShipyardEngine.Placements(grid, section)
            .Where(p => p.Rotation == turned)
            .Select(p => (p.Origin, Distance: CenterOf(section, p.Origin, turned).DistanceTo(center)))
            .Where(p => p.Distance <= TurnReach)
            .OrderBy(p => p.Distance)
            .Select(p => (Cell?)p.Origin)
            .FirstOrDefault();

        // With nowhere to fit nearby, it turns in place and shows red.
        var shape = section.Rotated(turned);
        GhostRotation = turned;
        GhostOrigin = near ?? Clamp(shape, new Cell(Mathf.RoundToInt(center.X - (shape.Width / 2f)), Mathf.RoundToInt(center.Y - (shape.Height / 2f))));
    }

    /// <summary>Moves the ghost to the place it fits nearest the tapped cell, keeping its turn when it can.</summary>
    private void MoveNear(Cell tapped)
    {
        if (_section is not { } section || Grid is not { } grid)
        {
            return;
        }

        var target = new Vector2(tapped.X + 0.5f, tapped.Y + 0.5f);
        var best = ShipyardEngine.Placements(grid, section)
            .OrderBy(p => p.Rotation == GhostRotation ? 0 : 1)
            .ThenBy(p => CenterOf(section, p.Origin, p.Rotation).DistanceSquaredTo(target))
            .Select(p => ((Cell, GridRotation)?)p)
            .FirstOrDefault();
        if (best is { } place)
        {
            (GhostOrigin, GhostRotation) = place;
        }
    }

    /// <summary>The origin under a dragged section, kept on the grid so the ghost never leaves it.</summary>
    private Cell Landing(Shape section, GridLayout g, Vector2 pointer)
    {
        var shape = section.Rotated(GhostRotation);
        var topLeft = FloatingTopLeft(shape, g, pointer) - g.Origin;
        return Clamp(shape, new Cell(Mathf.RoundToInt(topLeft.X / g.CellSize), Mathf.RoundToInt(topLeft.Y / g.CellSize)));
    }

    private GridLayout? Layout() => GridLayout.ForGrid(new Rect2(Vector2.Zero, Size), MarkerHeight);

    private static Cell Clamp(Shape rotated, Cell origin) =>
        new(Math.Clamp(origin.X, 0, Rules.GridColumns - rotated.Width), Math.Clamp(origin.Y, 0, Rules.GridRows - rotated.Height));

    /// <summary>The middle of a placed shape, in cells.</summary>
    private static Vector2 CenterOf(Shape section, Cell origin, GridRotation rotation)
    {
        var shape = section.Rotated(rotation);
        return new Vector2(origin.X + (shape.Width / 2f), origin.Y + (shape.Height / 2f));
    }

    private static int SharedEdges(ShipGrid grid, IReadOnlyList<Cell> cells) =>
        cells.Sum(c => c.EdgeNeighbors().Count(grid.Contains));

    /// <summary>The top-left corner of a floating section: centered on the finger, with its bottom just above it.</summary>
    private static Vector2 FloatingTopLeft(Shape shape, GridLayout g, Vector2 pointer) =>
        pointer - new Vector2(shape.Width * g.CellSize / 2, (shape.Height + FloatCells) * g.CellSize);
}
