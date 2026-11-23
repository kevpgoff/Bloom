namespace Bloom.Core.Run;

/// <summary>A fight the player has committed to by choosing its node. The enemy is rolled then and shown before the fight.</summary>
public sealed record Encounter(string EnemyId);

/// <summary>A visit to a grove that has not been used yet.</summary>
public sealed record GroveVisit;

/// <summary>
/// A visit to a dock, until the player leaves. <see cref="Pieces"/> are the piece def ids still for sale,
/// rolled when the node was chosen; a bought piece leaves the list.
/// </summary>
public sealed record DockVisit(List<string> Pieces);
