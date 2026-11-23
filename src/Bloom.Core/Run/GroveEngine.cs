namespace Bloom.Core.Run;

/// <summary>
/// A grove, where the spec lets the player either repair 30% of the hull or upgrade one piece. Upgrades
/// come with their own pull request; until then a grove repairs.
/// </summary>
public sealed class GroveEngine
{
    /// <summary>Hull points that <see cref="Repair"/> would restore: <see cref="Rules.GroveRepairPercent"/> of the maximum, rounded up, but not past it.</summary>
    public static int RepairAmount(RunState run) =>
        Math.Min(run.MaxHull - run.Hull, ((run.MaxHull * Rules.GroveRepairPercent) + 99) / 100);

    /// <summary>Repairs the hull and leaves the grove for the map.</summary>
    public void Repair(RunState run)
    {
        RequireGrove(run);
        run.Hull += RepairAmount(run);
        run.Grove = null;
        MapEngine.FinishNode(run);
    }

    private static void RequireGrove(RunState run)
    {
        if (run.Phase != RunPhase.Grove)
        {
            throw new InvalidOperationException("The ship is not at a grove.");
        }
    }
}
