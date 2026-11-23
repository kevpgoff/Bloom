using System.Text.Json.Serialization;

namespace Bloom.Core.Map;

public enum NodeType
{
    Skirmish,
    HardFight,
    Dock,
    Shipyard,
    Grove,
    Event,
    Boss,
}

/// <summary>
/// A node on the sector map. <see cref="Row"/> 0 is the first row of choices and the boss sits alone on
/// the last row. <see cref="Column"/> counts from 0 on the left; the boss's column means nothing, since it
/// is drawn centered.
/// </summary>
public sealed record MapNode(int Id, NodeType Type, int Row, int Column);

public sealed record MapEdge(int From, int To);

/// <summary>
/// A branching map of nodes that ends in a boss. The player sees all of it and moves along its edges,
/// one row at a time. Made by <see cref="MapGenerator"/>.
/// </summary>
public sealed class SectorMap
{
    public int Sector { get; set; }

    /// <summary>Nodes in row order, then left to right; a node's id is its index.</summary>
    public List<MapNode> Nodes { get; set; } = [];

    /// <summary>Edges from a row to the row after it, in the order of <see cref="Nodes"/>.</summary>
    public List<MapEdge> Edges { get; set; } = [];

    [JsonIgnore]
    public MapNode Boss => Nodes[^1];

    public MapNode Node(int id) =>
        id >= 0 && id < Nodes.Count ? Nodes[id] : throw new InvalidOperationException($"The map has no node {id}.");

    /// <summary>The nodes of the first row, where a sector starts.</summary>
    public IEnumerable<MapNode> StartNodes() => Nodes.Where(n => n.Row == 0);

    public IEnumerable<MapNode> NextNodes(int nodeId) =>
        Edges.Where(e => e.From == nodeId).Select(e => Node(e.To));

    public IEnumerable<MapNode> PreviousNodes(int nodeId) =>
        Edges.Where(e => e.To == nodeId).Select(e => Node(e.From));

    /// <summary>
    /// A way from the first row to a node, as the ids of the nodes before it in travel order. Where several
    /// nodes lead to one, the first in the order of <see cref="Edges"/> is taken. Empty for a node of the first row.
    /// </summary>
    public List<int> RouteTo(int nodeId)
    {
        var route = new List<int>();
        for (var before = PreviousNodes(nodeId).FirstOrDefault(); before is not null; before = PreviousNodes(before.Id).FirstOrDefault())
        {
            route.Add(before.Id);
        }

        route.Reverse();
        return route;
    }
}
