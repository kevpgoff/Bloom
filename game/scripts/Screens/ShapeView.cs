using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Draws one piece's shape, centered and scaled to fit, with its vector emblem; or a hull section's shape,
/// drawn as hull.
/// </summary>
public partial class ShapeView : Control
{
    /// <summary>Room around a hull shape for the brackets <see cref="GridPainter.DrawHull"/> draws outside it.</summary>
    private const float HullMargin = 10f;

    private PieceDef? _piece;
    private Shape? _hull;

    public PieceDef? Piece
    {
        get => _piece;
        set
        {
            _piece = value;
            QueueRedraw();
        }
    }

    /// <summary>A hull section's shape, drawn when no <see cref="Piece"/> is set.</summary>
    public Shape? Hull
    {
        get => _hull;
        set
        {
            _hull = value;
            QueueRedraw();
        }
    }

    public override void _Ready() => Resized += QueueRedraw;

    public override void _Draw()
    {
        if (Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        if (_piece is null && _hull is { } hull)
        {
            float size = Mathf.Floor(Mathf.Min(48, Mathf.Min((Size.X - (2 * HullMargin)) / hull.Width, (Size.Y - (2 * HullMargin)) / hull.Height)));
            var at = (Size - (new Vector2(hull.Width, hull.Height) * size)) / 2;
            GridPainter.DrawHull(this, new GridLayout(size, at, 0, hull.Height), hull.Cells);
            return;
        }

        if (_piece is not { } piece)
        {
            return;
        }

        var shape = piece.Shape;
        float cell = Mathf.Floor(Mathf.Min(48, Mathf.Min(Size.X / shape.Width, Size.Y / shape.Height)));
        var origin = (Size - (new Vector2(shape.Width, shape.Height) * cell)) / 2;
        var layout = new GridLayout(cell, origin, 0, shape.Height);
        var color = Palette.ForPiece(piece);
        GridPainter.DrawPiece(this, layout, shape.Cells, color);
        GridPainter.DrawPieceIcon(this, layout, shape.Cells, ArtLibrary.PieceIcon(piece), Palette.Glyph(color), labelLines: 0);
        if (piece.Passive is not null)
        {
            GridPainter.DrawPassiveMark(this, layout, shape.Cells);
        }
    }
}
