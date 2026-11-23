using Bloom.Core.Content;
using Bloom.Core.Grid;

namespace Bloom.Core.Run;

/// <summary>
/// A shipyard: the spec's node where the player adds one hull section to the ship and chooses where it
/// attaches. The section's cells join the hull, so more pieces fit; they bring no hull points.
/// </summary>
/// <remarks>
/// Decided by the owner on 2026-11-22: a shipyard is free, offers <see cref="Rules.ShipyardChoices"/> shapes,
/// and attaches one per visit. Defaults taken on 2026-11-23: the shapes are rolled when the node is chosen,
/// all equally likely, from the sections that can still attach somewhere; the player may leave without one;
/// and a section adds cells only, since the spec gives sections no hull points. A hard fight or a boss offers a
/// free section the same way once its reward is taken (see <see cref="RewardEngine"/>).
/// </remarks>
public sealed class ShipyardEngine(ContentDb content)
{
    /// <summary>
    /// Every way a shape can attach to the hull: rotations in order, then origins front row first and left to
    /// right. <c>Origin</c> is the top-left corner of the rotated shape, as for pieces.
    /// </summary>
    public static IEnumerable<(Cell Origin, Rotation Rotation)> Placements(ShipGrid grid, Shape shape)
    {
        foreach (var rotation in Enum.GetValues<Rotation>())
        {
            var rotated = shape.Rotated(rotation);
            for (int y = 0; y <= Rules.GridRows - rotated.Height; y++)
            {
                for (int x = 0; x <= Rules.GridColumns - rotated.Width; x++)
                {
                    var origin = new Cell(x, y);
                    if (grid.CanAttach(shape.CellsAt(origin, rotation)) == AttachError.None)
                    {
                        yield return (origin, rotation);
                    }
                }
            }
        }
    }

    /// <summary>Whether the shape can attach anywhere on the hull, in any rotation.</summary>
    public static bool Fits(ShipGrid grid, Shape shape) => Placements(grid, shape).Any();

    /// <summary>The section offered at <paramref name="index"/>, or null when there is none.</summary>
    public HullSectionDef? Offered(RunState run, int index) =>
        run.Phase == RunPhase.Shipyard && run.Shipyard is { } yard && index >= 0 && index < yard.Sections.Count
            ? content.Section(yard.Sections[index])
            : null;

    /// <summary>Whether the offered section at <paramref name="index"/> can attach at this origin and rotation now.</summary>
    public bool CanAttach(RunState run, int index, Cell origin, Rotation rotation) =>
        Offered(run, index) is { } section
        && run.Ship.Grid.CanAttach(section.Shape.CellsAt(origin, rotation)) == AttachError.None;

    /// <summary>Attaches the offered section at <paramref name="index"/> to the hull, which ends the visit.</summary>
    public void Attach(RunState run, int index, Cell origin, Rotation rotation)
    {
        var yard = RequireShipyard(run);
        if (index < 0 || index >= yard.Sections.Count)
        {
            throw new InvalidOperationException($"There is no section {index}; the shipyard offers {yard.Sections.Count}.");
        }

        var section = content.Section(yard.Sections[index]);
        var cells = section.Shape.CellsAt(origin, rotation);
        var error = run.Ship.Grid.CanAttach(cells);
        if (error != AttachError.None)
        {
            throw new InvalidOperationException(error switch
            {
                AttachError.OutOfBounds => $"'{section.Id}' would leave the {Rules.GridColumns}x{Rules.GridRows} grid there.",
                AttachError.Overlaps => $"'{section.Id}' would cover hull that is already there.",
                AttachError.NotTouching => $"'{section.Id}' must share an edge with the hull.",
                _ => $"'{section.Id}' cannot attach there: {error}.",
            });
        }

        run.Ship.Grid.Attach(cells);
        Finish(run);
    }

    /// <summary>Leaves without attaching a section.</summary>
    public void Leave(RunState run)
    {
        RequireShipyard(run);
        Finish(run);
    }

    /// <summary>
    /// Rolls the sections to offer from the run's random numbers: up to <see cref="Rules.ShipyardChoices"/>
    /// different ones that can attach somewhere, considered in content order. Empty when none fits.
    /// </summary>
    internal static List<string> RollSections(ContentDb content, RunState run)
    {
        var candidates = content.Sections.Where(s => Fits(run.Ship.Grid, s.Shape)).Select(s => s.Id).ToList();
        var offered = new List<string>();
        while (offered.Count < Rules.ShipyardChoices && candidates.Count > 0)
        {
            int pick = run.Rng.Next(candidates.Count);
            offered.Add(candidates[pick]);
            candidates.RemoveAt(pick);
        }

        return offered;
    }

    /// <summary>Offers sections away from a shipyard, after a hard fight or a boss. Nothing is offered when none fits.</summary>
    internal static void OfferSections(ContentDb content, RunState run)
    {
        var sections = RollSections(content, run);
        if (sections.Count > 0)
        {
            run.Shipyard = new ShipyardVisit(sections);
        }
    }

    private static void Finish(RunState run)
    {
        run.Shipyard = null;
        MapEngine.FinishNode(run);
    }

    private static ShipyardVisit RequireShipyard(RunState run) =>
        run.Phase == RunPhase.Shipyard && run.Shipyard is { } yard
            ? yard
            : throw new InvalidOperationException("No hull sections are on offer.");
}
