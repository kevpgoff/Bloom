using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bloom.Core.Grid;

/// <summary>Reads and writes a <see cref="Shape"/> as an array of row strings.</summary>
public sealed class ShapeJsonConverter : JsonConverter<Shape>
{
    public override Shape Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("A shape must be an array of row strings, such as [\"###\", \".#.\"].");
        }

        var rows = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            rows.Add(reader.GetString() ?? throw new JsonException("Shape rows cannot be null."));
        }

        try
        {
            return Shape.Parse(rows);
        }
        catch (FormatException e)
        {
            throw new JsonException(e.Message, e);
        }
    }

    public override void Write(Utf8JsonWriter writer, Shape value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string row in value.ToRows())
        {
            writer.WriteStringValue(row);
        }

        writer.WriteEndArray();
    }
}
