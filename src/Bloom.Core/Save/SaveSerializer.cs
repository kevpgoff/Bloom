using System.Text.Json;
using Bloom.Core.Run;

namespace Bloom.Core.Save;

/// <summary>Where a run is saved. The Godot layer writes to user://; tests use <see cref="MemorySaveStore"/>.</summary>
public interface ISaveStore
{
    string? Load();

    void Save(string json);

    void Delete();
}

public sealed class MemorySaveStore : ISaveStore
{
    public string? Json { get; private set; }

    public int SaveCount { get; private set; }

    public string? Load() => Json;

    public void Save(string json)
    {
        Json = json;
        SaveCount++;
    }

    public void Delete() => Json = null;
}

public sealed class SaveException(string message, Exception? inner = null) : Exception(message, inner);

public static class SaveSerializer
{
    public static string Serialize(RunState state) =>
        JsonSerializer.Serialize(state, BloomJsonContext.Default.RunState);

    public static RunState Deserialize(string json)
    {
        RunState? state;
        try
        {
            state = JsonSerializer.Deserialize(json, BloomJsonContext.Default.RunState);
        }
        catch (JsonException e)
        {
            throw new SaveException($"The save file could not be read: {e.Message}", e);
        }

        if (state is null)
        {
            throw new SaveException("The save file is empty.");
        }

        if (state.SchemaVersion != RunState.CurrentSchemaVersion)
        {
            throw new SaveException($"Save schema {state.SchemaVersion} is not supported; this build reads schema {RunState.CurrentSchemaVersion}.");
        }

        return state;
    }
}
