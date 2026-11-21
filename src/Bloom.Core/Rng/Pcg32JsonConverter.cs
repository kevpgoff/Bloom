using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bloom.Core.Rng;

/// <summary>Saves a <see cref="Pcg32"/> as <c>{"state": …, "increment": …}</c>.</summary>
public sealed class Pcg32JsonConverter : JsonConverter<Pcg32>
{
    public override Pcg32 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an object for the RNG state.");
        }

        ulong? state = null;
        ulong? increment = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a property name in the RNG state.");
            }

            string? name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "state":
                    state = reader.GetUInt64();
                    break;
                case "increment":
                    increment = reader.GetUInt64();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (state is null || increment is null)
        {
            throw new JsonException("The RNG state needs both 'state' and 'increment'.");
        }

        try
        {
            return Pcg32.FromState(new RngState(state.Value, increment.Value));
        }
        catch (ArgumentException e)
        {
            throw new JsonException(e.Message, e);
        }
    }

    public override void Write(Utf8JsonWriter writer, Pcg32 value, JsonSerializerOptions options)
    {
        var state = value.State;
        writer.WriteStartObject();
        writer.WriteNumber("state", state.State);
        writer.WriteNumber("increment", state.Increment);
        writer.WriteEndObject();
    }
}
