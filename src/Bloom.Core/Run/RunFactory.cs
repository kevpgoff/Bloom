using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Core.Rng;

namespace Bloom.Core.Run;

public static class RunFactory
{
    /// <summary>Every run starts from the ship class's basic ship. The same seed always gives the same run.</summary>
    public static RunState NewRun(ContentDb content, string shipClassId, ulong seed)
    {
        var ship = content.Ship(shipClassId);
        var run = new RunState
        {
            Seed = seed,
            Rng = new Pcg32(seed),
            ShipClassId = ship.Id,
            Hull = ship.Hull,
            MaxHull = ship.Hull,
            Ship = new ShipLayout { Grid = ShipGrid.FromShape(ship.HullShape, ship.HullOrigin) },
        };

        var fitter = new ShipFitter(content);
        foreach (var start in ship.StartingPieces)
        {
            fitter.Place(run.Ship, run.NewPiece(start.Piece), start.Origin, start.Rotation);
        }

        return run;
    }
}
