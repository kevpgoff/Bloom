using Bloom.Core.Grid;

namespace Bloom.Core.Content;

/// <summary>
/// A hull section as defined in sections.json: a shape of new hull cells that a shipyard offers and the
/// player attaches to an edge of the ship. <see cref="Placeholder"/> marks sections invented for testing.
/// </summary>
public sealed record HullSectionDef(string Id, string Name, Shape Shape, bool Placeholder = false);
