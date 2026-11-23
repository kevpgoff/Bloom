using Bloom.Core.Grid;

namespace Bloom.Core.Tests;

public class GridTests
{
    /// <summary>Rows separated by '|': "###|.#." is a T.</summary>
    private static Shape S(string rows) => Shape.Parse(rows.Split('|'));

    [Fact]
    public void AHeavyAttackOnALaneWithoutHullHitsTheNearestLaneWithHull()
    {
        var grid = new ShipGrid(S("###").CellsAt(new Cell(1, 3), Rotation.R0));
        Assert.Equal(1, grid.LaneHit(0));
        Assert.Equal(2, grid.LaneHit(2));
        Assert.Equal(3, grid.LaneHit(5));

        grid.Attach([new Cell(0, 3)]);
        Assert.Equal(0, grid.LaneHit(0));
    }

    [Theory]
    [InlineData("###|.#.")]
    [InlineData("#.|##")]
    [InlineData(".##|##.")]
    [InlineData("##|##")]
    [InlineData("###")]
    public void FourQuarterTurnsGiveTheSameShape(string rows)
    {
        var shape = S(rows);
        var turned = shape;
        for (int i = 0; i < 4; i++)
        {
            turned = turned.Rotated(Rotation.R90);
        }

        Assert.Equal(shape, turned);
    }

    [Fact]
    public void RotationsCompose()
    {
        var t = S("###|.#.");
        Assert.Equal(t.Rotated(Rotation.R180), t.Rotated(Rotation.R90).Rotated(Rotation.R90));
        Assert.Equal(t.Rotated(Rotation.R270), t.Rotated(Rotation.R90).Rotated(Rotation.R90).Rotated(Rotation.R90));
    }

    [Fact]
    public void ATurnsClockwise()
    {
        // A T with its stem pointing down, turned clockwise, has its stem pointing left.
        Assert.Equal([".#", "##", ".#"], S("###|.#.").Rotated(Rotation.R90).ToRows());
        Assert.Equal(["#", "#", "#"], S("###").Rotated(Rotation.R90).ToRows());
    }

    [Fact]
    public void AnSAndAZStayDifferentPieces()
    {
        // Pieces never mirror, so no rotation of an S is a Z.
        var s = S(".##|##.");
        var z = S("##.|.##");
        foreach (var rotation in Enum.GetValues<Rotation>())
        {
            Assert.NotEqual(z, s.Rotated(rotation));
        }
    }

    [Fact]
    public void ParsingIgnoresEmptyBorders() => Assert.Equal(S("##"), S("....|.##.|...."));

    [Fact]
    public void ParsingRejectsUnknownCharacters() => Assert.Throws<FormatException>(() => S("#x"));

    [Fact]
    public void ShapesJoinedOnlyAtCornersAreNotConnected()
    {
        Assert.True(S("#.|##").IsConnected());
        Assert.False(S("#.#").IsConnected());
        Assert.False(S("#.|.#").IsConnected());
    }

    [Fact]
    public void CellsAtPlacesTheRotatedShapeAtItsTopLeftCorner()
    {
        var cells = S("##").CellsAt(new Cell(3, 4), Rotation.R90);
        Assert.Equal([new Cell(3, 4), new Cell(3, 5)], cells);
    }

    private static ShipGrid SquareHull() => ShipGrid.FromShape(S("##|##"), new Cell(2, 2));

    [Fact]
    public void AHullSectionThatSharesAnEdgeAttaches()
    {
        var grid = SquareHull();
        Cell[] section = [new(4, 2), new(5, 2)];
        Assert.Equal(AttachError.None, grid.CanAttach(section));
        grid.Attach(section);
        Assert.Equal(6, grid.CellCount);
        Assert.True(grid.Contains(new Cell(5, 2)));
    }

    [Fact]
    public void AHullSectionTouchingOnlyAtACornerIsRejected() =>
        Assert.Equal(AttachError.NotTouching, SquareHull().CanAttach([new Cell(4, 4), new Cell(5, 4)]));

    [Fact]
    public void AHullSectionOverlappingTheHullIsRejected() =>
        Assert.Equal(AttachError.Overlaps, SquareHull().CanAttach([new Cell(3, 3), new Cell(4, 3)]));

    [Fact]
    public void AHullSectionOutsideTheGridIsRejected()
    {
        var grid = SquareHull();
        Assert.Equal(AttachError.OutOfBounds, grid.CanAttach([new Cell(5, 2), new Cell(6, 2)]));
        Assert.Throws<InvalidOperationException>(() => grid.Attach([new Cell(5, 2), new Cell(6, 2)]));
        Assert.Equal(4, grid.CellCount);
    }

    [Fact]
    public void AHullCannotStartOutsideTheGrid() =>
        Assert.Throws<ArgumentException>(() => new ShipGrid([new Cell(Rules.GridColumns, 0)]));
}
