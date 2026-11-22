using Bloom.Core.Pieces;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>Draws one piece's shape, centered and scaled to fit, with its vector emblem.</summary>
public partial class ShapeView : Control
{
    private PieceDef? _piece;

    public PieceDef? Piece
    {
        get => _piece;
        set
        {
            _piece = value;
            QueueRedraw();
        }
    }

    public override void _Ready() => Resized += QueueRedraw;

    public override void _Draw()
    {
        if (_piece is not { } piece || Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        var shape = piece.Shape;
        float cell = Mathf.Floor(Mathf.Min(48, Mathf.Min(Size.X / shape.Width, Size.Y / shape.Height)));
        var origin = (Size - (new Vector2(shape.Width, shape.Height) * cell)) / 2;
        var layout = new GridLayout(cell, origin, 0, shape.Height);
        var color = Palette.ForPiece(piece);
        GridPainter.DrawPiece(this, layout, shape.Cells, color);
        GridPainter.DrawPieceIcon(this, layout, shape.Cells, ArtLibrary.PieceIcon(piece), Palette.Glyph(color), reserveLabelSpace: false);
    }
}
