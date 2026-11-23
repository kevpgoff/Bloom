using Bloom.Core.Map;

namespace Bloom.Core.Run;

/// <summary>
/// Where a test run starts: at the map's first node of type <see cref="Node"/>, entered as if the ship had
/// travelled there. With <see cref="EventId"/>, an event node holds that event and not a rolled one. Used by the
/// hangar's test panel in debug builds (<see cref="RunFactory.NewTestRun"/>).
/// </summary>
public sealed record TestStart(NodeType Node, string? EventId = null);
