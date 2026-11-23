using System.Text.Json;
using Bloom.Core.Save;

namespace Bloom.Core.Profile;

/// <summary>Reads and writes the profile, as <see cref="SaveSerializer"/> does a run. It goes through an <see cref="ISaveStore"/> of its own.</summary>
public static class ProfileSerializer
{
    public static string Serialize(ProfileState profile) =>
        JsonSerializer.Serialize(profile, BloomJsonContext.Default.ProfileState);

    public static ProfileState Deserialize(string json)
    {
        ProfileState? profile;
        try
        {
            profile = JsonSerializer.Deserialize(json, BloomJsonContext.Default.ProfileState);
        }
        catch (JsonException e)
        {
            throw new SaveException($"The profile could not be read: {e.Message}", e);
        }

        if (profile is null)
        {
            throw new SaveException("The profile is empty.");
        }

        if (profile.SchemaVersion != ProfileState.CurrentSchemaVersion)
        {
            throw new SaveException($"Profile schema {profile.SchemaVersion} is not supported; this build reads schema {ProfileState.CurrentSchemaVersion}.");
        }

        if (profile.Records is null || profile.Records.Any(r => r?.ShipClassId is null || r.LoadoutId is null))
        {
            throw new SaveException("The profile has a record without a ship class or a loadout.");
        }

        return profile;
    }

    /// <summary>The stored profile, or a new one when the store is empty.</summary>
    public static ProfileState Load(ISaveStore store) => store.Load() is { } json ? Deserialize(json) : new ProfileState();
}
