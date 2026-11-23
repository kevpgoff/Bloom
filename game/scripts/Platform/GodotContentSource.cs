using Bloom.Core.Content;
using FileAccess = Godot.FileAccess;

namespace Bloom.Game.Platform;

/// <summary>Reads the content files through Godot, so that it works in exported builds too.</summary>
public static class GodotContentSource
{
    public static ContentDb Load(string dir) => ContentDb.FromJson(
        Read(dir, "pieces.json"),
        Read(dir, "cards.json"),
        Read(dir, "enemies.json"),
        Read(dir, "ships.json"),
        Read(dir, "sections.json"));

    private static string Read(string dir, string name)
    {
        string path = $"{dir}/{name}";
        return FileAccess.FileExists(path)
            ? FileAccess.GetFileAsString(path)
            : throw new ContentException([$"{name}: not found at {path}."]);
    }
}
