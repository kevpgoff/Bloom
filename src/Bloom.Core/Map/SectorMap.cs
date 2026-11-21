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

/// <summary>A node on the sector map. <see cref="Row"/> 0 is the start; the boss sits on the last row.</summary>
public sealed record MapNode(int Id, NodeType Type, int Row, int Column);

public sealed record MapEdge(int From, int To);

/// <summary>A branching map of nodes that ends in a boss. Generation is not written yet.</summary>
public sealed class SectorMap
{
    public int Sector { get; set; }

    public List<MapNode> Nodes { get; set; } = [];

    public List<MapEdge> Edges { get; set; } = [];

    public IEnumerable<MapNode> NextNodes(int nodeId) =>
        Edges.Where(e => e.From == nodeId).Select(e => Nodes.First(n => n.Id == e.To));
}
