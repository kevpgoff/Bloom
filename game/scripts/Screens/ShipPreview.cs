using Bloom.Core.Content;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// The hangar's hologram of a ship as a run would start with it: the lanes, the hull, the loadout's pieces with
/// their names, and the bonuses between them. A tap on a piece selects it and a tap elsewhere clears the
/// selection, reported through <see cref="PieceSelected"/>. Nothing here can be moved.
/// </summary>
public partial class ShipPreview : Control
{
    /// <summary>Room above the grid for the lane marks ("L1" to "L6") that <see cref="GridPainter.DrawLanes"/> draws there.</summary>
    private const float LaneMarkHeight = 30f;

    private ContentDb? _content;
    private ShipFitter? _fitter;
    private ShipLayout? _ship;

    /// <summary>Raised with the selected piece, or null when the selection is cleared.</summary>
    public event Action<FittedPiece?>? PieceSelected;

    public int? SelectedPieceId { get; private set; }

    public override void _Ready() => Resized += QueueRedraw;

    /// <summary>Shows the ship of a run that was just made, and clears the selection.</summary>
    public void SetShip(ContentDb content, RunState run)
    {
        _content = content;
        _fitter = new ShipFitter(content);
        _ship = run.Ship;
        SelectedPieceId = null;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } tap
            || _ship is not { } ship || _fitter is not { } fitter || Layout(ship) is not { } g)
        {
            return;
        }

        var cell = g.CellAt(tap.Position);
        var hit = g.Bounds.HasPoint(tap.Position) ? ship.Fitted.Find(f => fitter.CellsOf(f).Contains(cell)) : null;
        if (hit?.Piece.Id != SelectedPieceId)
        {
            SelectedPieceId = hit?.Piece.Id;
            QueueRedraw();
            PieceSelected?.Invoke(hit);
        }
    }

    public override void _Draw()
    {
        if (_content is not { } content || _fitter is not { } fitter || _ship is not { } ship || Layout(ship) is not { } g)
        {
            return;
        }

        GridPainter.DrawLanes(this, g);
        GridPainter.DrawHull(this, g, ship.Grid.Cells);

        var bonuses = fitter.ActiveBonuses(ship, []);
        var receivers = bonuses.Select(b => b.ReceiverId).ToHashSet();
        foreach (var fitted in ship.Fitted)
        {
            var def = content.Piece(fitted.Piece.DefId);
            var cells = fitter.CellsOf(fitted);
            var color = Palette.ForPiece(def);
            GridPainter.DrawPiece(this, g, cells, color);
            GridPainter.DrawPieceIcon(this, g, cells, ArtLibrary.PieceIcon(def), Palette.Glyph(color), GridPainter.NameLines(g, cells, def.Name));
            GridPainter.DrawLabel(this, g, cells, def.Name, Palette.Label(color));
            if (receivers.Contains(fitted.Piece.Id))
            {
                GridPainter.DrawBonusBadge(this, g, cells, Palette.Badge);
            }

            if (fitted.Piece.Id == SelectedPieceId)
            {
                GridPainter.DrawOutline(this, g, cells, Colors.White, 4);
            }
        }

        foreach (var bonus in bonuses)
        {
            if (ship.Find(bonus.GiverId) is { } giver && ship.Find(bonus.ReceiverId) is { } receiver)
            {
                GridPainter.DrawBonusLink(this, g, fitter.CellsOf(giver), fitter.CellsOf(receiver), Palette.Boosted);
            }
        }
    }

    private GridLayout? Layout(ShipLayout ship) => GridLayout.ForHull(new Rect2(Vector2.Zero, Size), ship.Grid.Cells, LaneMarkHeight);
}
