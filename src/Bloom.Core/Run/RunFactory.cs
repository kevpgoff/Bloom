using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Map;
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

    /// <summary>
    /// A run for testing that starts at a node, not before the first row: the same run as
    /// <see cref="NewRun(ContentDb, RunSetup)"/> makes, with the ship put on the map's first node of the wanted
    /// type and that node entered. <see cref="RunState.Path"/> holds a real way to the node, so the run carries on
    /// from there like any other. The ship is the starting ship, whatever the row.
    /// </summary>
    /// <remarks>
    /// Not every map has every type. When the wanted one is missing, a node of the middle row becomes that type:
    /// the first one there whose type the map does not need (<see cref="Rules.MapRequiredTypes"/>), or else the first.
    /// </remarks>
    public static RunState NewTestRun(ContentDb content, RunSetup setup, TestStart start)
    {
        var run = NewRun(content, setup);
        var map = run.Map!;
        var node = map.Nodes.FirstOrDefault(n => n.Type == start.Node);
        if (node is null)
        {
            var row = map.Nodes.Where(n => n.Row == Rules.MapRows / 2).ToList();
            var changed = row.FirstOrDefault(n => !Rules.MapRequiredTypes.Contains(n.Type)) ?? row[0];
            node = changed with { Type = start.Node };
            map.Nodes[node.Id] = node;
        }

        run.Path.AddRange(map.RouteTo(node.Id));
        new MapEngine(content).Enter(run, node, start.EventId);
        return run;
    }
}
