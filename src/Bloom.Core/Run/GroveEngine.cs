using Bloom.Core.Content;

namespace Bloom.Core.Run;

/// <summary>A grove, where the spec lets the player either repair 30% of the hull or upgrade one piece. Either one ends the visit.</summary>
public sealed class GroveEngine(ContentDb content)
{
    /// <summary>Hull points that <see cref="Repair"/> would restore: <see cref="Rules.GroveRepairPercent"/> of the maximum, rounded up, but not past it.</summary>
    public static int RepairAmount(RunState run) =>
        Math.Min(run.MaxHull - run.Hull, ((run.MaxHull * Rules.GroveRepairPercent) + 99) / 100);

    /// <summary>Repairs the hull and leaves the grove for the map.</summary>
    public void Repair(RunState run)
    {
        RequireGrove(run);
        run.Hull += RepairAmount(run);
        Leave(run);
    }

    /// <summary>Upgrades one fitted or held piece (see <see cref="PieceUpgrades"/>) and leaves the grove for the map.</summary>
    public void Upgrade(RunState run, int pieceId)
    {
        RequireGrove(run);
        PieceUpgrades.Upgrade(content, run, pieceId);
        Leave(run);
    }

    private static void Leave(RunState run)
    {
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
