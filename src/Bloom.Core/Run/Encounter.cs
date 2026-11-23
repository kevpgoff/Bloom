namespace Bloom.Core.Run;

/// <summary>A fight the player has committed to by choosing its node. The enemy is rolled then and shown before the fight.</summary>
public sealed record Encounter(string EnemyId);

/// <summary>A visit to a grove that has not been used yet.</summary>
public sealed record GroveVisit;

/// <summary>
/// A visit to a dock, until the player leaves. <see cref="Pieces"/> are the piece def ids still for sale and
/// <see cref="Repairs"/> the hull repairs left, both rolled when the node was chosen. A bought piece leaves
/// the list, and a bought repair counts down.
/// </summary>
public sealed record DockVisit(List<string> Pieces, int Repairs);

/// <summary>
/// Hull sections on offer, as section def ids, rolled when the shipyard was entered (or the boss was beaten).
/// Attaching one ends the visit, so the list never changes.
/// </summary>
public sealed record ShipyardVisit(List<string> Sections);

/// <summary>
/// A visit to an event: which one, and once the player has chosen, which choice, which of its results came up
/// and what that changed. The visit lasts until the player continues past the result.
/// </summary>
public sealed record EventVisit(string EventId, int? Choice = null, int? Result = null, EventChanges? Changes = null);

/// <summary>
/// What an event's result changed, kept to show it: the actual salvage, hull and maximum hull differences
/// (after the limits, and with the choice's cost), and the instance ids of the pieces gained and upgraded.
/// </summary>
public sealed record EventChanges(int Salvage, int Hull, int MaxHull, List<int> Gained, List<int> Upgraded);
