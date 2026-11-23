namespace Bloom.Core.Profile;

/// <summary>How a voyage ended. Leaving one that was still in progress abandons it.</summary>
public enum VoyageOutcome
{
    Won,
    Lost,
    Abandoned,
}

/// <summary>
/// What is kept between runs: the voyages made with each ship class and loadout. Which ships are open is worked
/// out from these records (<see cref="ProfileEngine.IsUnlocked"/>), so it is not stored. This is what the profile
/// file holds. Bump <see cref="CurrentSchemaVersion"/> when the shape changes.
/// </summary>
/// <remarks>
/// Only recorded voyages are here. A voyage from a seed that the player typed is practice, and a test run is a
/// test: neither is ever passed to <see cref="ProfileEngine"/>.
/// </remarks>
public sealed class ProfileState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>One record for each ship class and loadout that a voyage was launched with, in the order of the first launch.</summary>
    public List<LoadoutRecord> Records { get; set; } = [];

    /// <summary>The recorded voyage in progress, or null. It is launched and not yet won, lost or abandoned.</summary>
    public OpenVoyage? Open { get; set; }

    /// <summary>The recorded voyage that ended last, so that its end can still be shown after the game restarts.</summary>
    public ClosedVoyage? Last { get; set; }

    /// <summary>The record for a ship class and loadout, or null when no voyage was launched with them.</summary>
    public LoadoutRecord? Record(string shipClassId, string loadoutId) =>
        Records.Find(r => r.ShipClassId == shipClassId && r.LoadoutId == loadoutId);
}

/// <summary>The voyages made with one loadout of one ship class. <see cref="Launched"/> also counts the one in progress.</summary>
public sealed class LoadoutRecord
{
    public required string ShipClassId { get; set; }

    public required string LoadoutId { get; set; }

    public int Launched { get; set; }

    public int Won { get; set; }

    public int Lost { get; set; }

    public int Abandoned { get; set; }

    /// <summary>The furthest that any of these voyages came by its end, or null before the first one ends.</summary>
    public VoyageProgress? Furthest { get; set; }
}

/// <summary>
/// How far a voyage came: its sector, and the rows of that sector's map that the ship entered (0 before the
/// first node, <see cref="Rules.MapRows"/> + 1 at the boss).
/// </summary>
public sealed record VoyageProgress(int Sector, int Rows) : IComparable<VoyageProgress>
{
    public int CompareTo(VoyageProgress? other) =>
        other is null ? 1 : Sector != other.Sector ? Sector.CompareTo(other.Sector) : Rows.CompareTo(other.Rows);
}

/// <summary>The recorded voyage in progress. The seed tells it from any other run with the same ship and loadout.</summary>
public sealed record OpenVoyage(string ShipClassId, string LoadoutId, ulong Seed);

/// <summary>A recorded voyage that ended, with the ship classes that its end opened (their ids, in content order).</summary>
public sealed record ClosedVoyage(string ShipClassId, string LoadoutId, ulong Seed, VoyageOutcome Outcome, IReadOnlyList<string> Unlocked);
