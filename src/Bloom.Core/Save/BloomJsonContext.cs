using System.Text.Json;
using System.Text.Json.Serialization;
using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Pieces;
using Bloom.Core.Run;

namespace Bloom.Core.Save;

/// <summary>
/// Source-generated JSON metadata for saves and content. Source generation needs no reflection at run
/// time, so it keeps working under trimming and NativeAOT, which iOS builds use. Add any new type that
/// is read or written as a root here.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(RunState))]
[JsonSerializable(typeof(List<PieceDef>))]
[JsonSerializable(typeof(List<CardDef>))]
[JsonSerializable(typeof(List<EnemyDef>))]
[JsonSerializable(typeof(List<ShipClassDef>))]
internal sealed partial class BloomJsonContext : JsonSerializerContext
{
}
