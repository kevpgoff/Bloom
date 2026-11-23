using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Core.Rng;

namespace Bloom.Core.Run;

public static class RunFactory
{
    /// <summary>
    /// Every run starts from the ship class's basic ship with the chosen loadout, on the map of the first sector,
    /// so the player can refit before choosing the first fight. The same setup always gives the same run.
    /// </summary>
    public static RunState NewRun(ContentDb content, RunSetup setup)
    {
        var ship = content.Ship(setup.ShipClassId);
        var loadout = ship.Loadout(setup.LoadoutId);
        var run = new RunState
        {
            Seed = setup.Seed,
            SeedText = setup.SeedText,
            Rng = new Pcg32(setup.Seed),
            ShipClassId = ship.Id,
            LoadoutId = loadout.Id,
            Hull = ship.Hull,
            MaxHull = ship.Hull,
            Ship = new ShipLayout { Grid = ShipGrid.FromShape(ship.HullShape, ship.HullOrigin) },
        };

        var fitter = new ShipFitter(content);
        foreach (var start in loadout.StartingPieces)
        {
            fitter.Place(run.Ship, run.NewPiece(start.Piece), start.Origin, start.Rotation);
        }

        MapEngine.EnterSector(run, 1);
        return run;
    }

    /// <summary>A run with the ship class's first loadout and a seed that is only a number.</summary>
    public static RunState NewRun(ContentDb content, string shipClassId, ulong seed) =>
        NewRun(content, new RunSetup(shipClassId, seed));
}
