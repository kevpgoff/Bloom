using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Map;

namespace Bloom.Core.Run;

/// <summary>
/// Moving across the sector map: which nodes can be chosen next, and entering one. The player sees the
/// whole map, with each node's type but not its enemy. Choosing a fight node commits to it and rolls its
/// enemy, which the Fitting screen then shows, so the player can refit before the fight (owner, 2026-11-22).
/// </summary>
public sealed class MapEngine(ContentDb content)
{
    private readonly RewardEngine _rewards = new(content);

    /// <summary>The nodes the player can choose now: the first row of a new sector, or the nodes after the current one. Empty outside the map.</summary>
    public IReadOnlyList<MapNode> Reachable(RunState run)
    {
        if (run.Phase != RunPhase.Map || run.Map is not { } map)
        {
            return [];
        }

        return (run.Path.Count == 0 ? map.StartNodes() : map.NextNodes(run.Path[^1])).ToList();
    }

    /// <summary>Which enemies a fight on this node draws from, or null for a node that is not a fight.</summary>
    public static EnemyPool? PoolFor(MapNode node) => node.Type switch
    {
        NodeType.Skirmish => node.Row < Rules.MapEarlyRows ? EnemyPool.Early : EnemyPool.Late,
        NodeType.HardFight => EnemyPool.Hard,
        NodeType.Boss => EnemyPool.Boss,
        _ => null,
    };

    /// <summary>
    /// Moves the ship to a reachable node and starts what happens there: a fight node commits to a fight
    /// against an enemy rolled from its pool, a dock rolls the pieces it sells, and a grove waits for the
    /// player's choice. Refit steps can no longer be undone after this.
    /// </summary>
    public void Choose(RunState run, int nodeId)
    {
        if (run.Phase != RunPhase.Map)
        {
            throw new InvalidOperationException(run.Phase switch
            {
                RunPhase.Won or RunPhase.Lost => "The run is over.",
                _ => "Finish this node before choosing the next one.",
            });
        }

        var node = Reachable(run).FirstOrDefault(n => n.Id == nodeId)
                   ?? throw new InvalidOperationException($"Node {nodeId} cannot be reached from here.");
        var pool = PoolFor(node);
        var candidates = pool is { } p ? EnemiesIn(run, p) : [];
        if (pool is not null && candidates.Count == 0)
        {
            throw new InvalidOperationException($"Sector {run.Sector} has no enemy in the {pool} pool.");
        }

        if (pool is null && node.Type is not (NodeType.Grove or NodeType.Dock))
        {
            throw new InvalidOperationException($"{node.Type} nodes are not built yet.");
        }

        run.Path.Add(node.Id);
        run.UndoStack.Clear();
        if (pool is not null)
        {
            // Enemies not met yet come first, so a pool repeats only once it runs out.
            var fresh = candidates.Where(e => !run.EnemiesMet.Contains(e.Id)).ToList();
            var choices = fresh.Count > 0 ? fresh : candidates;
            var enemy = choices[run.Rng.Next(choices.Count)];
            run.EnemiesMet.Add(enemy.Id);
            run.Encounter = new Encounter(enemy.Id);
        }
        else if (node.Type == NodeType.Dock)
        {
            var pieces = _rewards.RollPieces(run, Rules.DockPieces);
            run.Dock = new DockVisit(pieces, run.Rng.Next(Rules.DockRepairs.Min, Rules.DockRepairs.Max + 1));
        }
        else
        {
            run.Grove = new GroveVisit();
        }
    }

    /// <summary>Starts a sector: its map is made from the run's random numbers, and the ship waits before its first row.</summary>
    internal static void EnterSector(RunState run, int sector)
    {
        run.Sector = sector;
        run.Map = MapGenerator.Generate(run.Rng, sector);
        run.Path.Clear();
    }

    /// <summary>
    /// Called when a node is done. After the boss of a sector that is not the last, the next sector
    /// starts; the last boss wins the run when the fight ends, so it never gets here.
    /// </summary>
    internal static void FinishNode(RunState run)
    {
        if (run.CurrentNode is { Type: NodeType.Boss } && run.Sector < run.Sectors)
        {
            EnterSector(run, run.Sector + 1);
        }
    }

    /// <summary>The enemies of a pool in the run's sector, in content order.</summary>
    private List<EnemyDef> EnemiesIn(RunState run, EnemyPool pool) =>
        content.Enemies.Where(e => e.Pool == pool && e.Sector == run.Sector).ToList();
}
