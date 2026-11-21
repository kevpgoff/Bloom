using Bloom.Core.Save;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Bloom.Game.Platform;

/// <summary>
/// Saves the run under user://. Each save writes a temporary file and then renames it over the old
/// one, so a crash or a killed app in the middle of a save never leaves a half-written file.
/// </summary>
public sealed class GodotSaveStore(string path = "user://run.json") : ISaveStore
{
    public string? Load() => FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : null;

    public void Save(string json)
    {
        string temp = path + ".tmp";
        using (var file = FileAccess.Open(temp, FileAccess.ModeFlags.Write)
                          ?? throw new IOException($"Cannot write {temp}: {FileAccess.GetOpenError()}"))
        {
            file.StoreString(json);
            file.Flush();
        }

        var error = DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(temp), ProjectSettings.GlobalizePath(path));
        if (error != Error.Ok)
        {
            throw new IOException($"Cannot replace {path}: {error}");
        }
    }

    public void Delete()
    {
        if (FileAccess.FileExists(path))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
    }
}
