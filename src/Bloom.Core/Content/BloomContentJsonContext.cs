using System.Text.Json.Serialization;
using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Pieces;

namespace Bloom.Core.Content;

/// <summary>
/// Source-generated JSON metadata for the content files. It is stricter than the context that reads saves
/// (<see cref="Save.BloomJsonContext"/>): a property that no definition has is an error, and so are comments
/// and trailing commas. A misspelled property would otherwise be ignored, and the value it meant to set would
/// silently keep its default. Add any new content type that is read as a root here.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(List<PieceDef>))]
[JsonSerializable(typeof(List<CardDef>))]
[JsonSerializable(typeof(List<EnemyDef>))]
[JsonSerializable(typeof(List<ShipClassDef>))]
[JsonSerializable(typeof(List<HullSectionDef>))]
[JsonSerializable(typeof(List<EventDef>))]
internal sealed partial class BloomContentJsonContext : JsonSerializerContext
{
}
