using Bloom.Core.Content;
using Bloom.Core.Run;

namespace Bloom.Core.Profile;

/// <summary>
/// The rules of the profile: a recorded voyage is opened by <see cref="Launch"/> and closed by
/// <see cref="Finish"/>, and <see cref="IsUnlocked"/> works out from the records which ship classes are open.
/// The caller decides which voyages are recorded and stores the profile after each change. Every method checks
/// before it changes anything, so one that throws leaves the profile as it was.
/// </summary>
public sealed class ProfileEngine(ContentDb content)
{
    /// <summary>Whether the player may launch this ship class: it has no <see cref="ShipClassDef.Unlock"/>, or the records meet it.</summary>
    public static bool IsUnlocked(ProfileState profile, ShipClassDef ship) => ship.Unlock switch
    {
        null => true,
        WinWithUnlock win => profile.Records.Any(r => r.ShipClassId == win.Ship && r.Won > 0),
        _ => throw new InvalidOperationException($"Unlock rule {ship.Unlock.GetType().Name} has no check."),
    };

    /// <summary>Whether a run is the recorded voyage in progress.</summary>
    public static bool IsOpen(ProfileState profile, RunState run) =>
        profile.Open is { } open && open.ShipClassId == run.ShipClassId && open.LoadoutId == run.LoadoutId && open.Seed == run.Seed;

    /// <summary>The end of a run in the profile, or null when the run is not the recorded voyage that ended last.</summary>
    public static ClosedVoyage? EndOf(ProfileState profile, RunState run) =>
        profile.Last is { } last && last.ShipClassId == run.ShipClassId && last.LoadoutId == run.LoadoutId && last.Seed == run.Seed
        && run.Result != RunResult.InProgress
            ? last
            : null;

    /// <summary>How far a run has come.</summary>
    public static VoyageProgress ProgressOf(RunState run) => new(run.Sector, run.Path.Count);

    /// <summary>Opens a recorded voyage for a run that was just started, and counts it as launched.</summary>
    public void Launch(ProfileState profile, RunState run)
    {
        if (profile.Open is not null)
        {
            throw new InvalidOperationException("A recorded voyage is still in progress.");
        }

        if (run.Result != RunResult.InProgress)
        {
            throw new InvalidOperationException("The run is already over.");
        }

        content.Ship(run.ShipClassId).Loadout(run.LoadoutId); // throws for a ship or a loadout that the content does not have
        RecordOf(profile, run.ShipClassId, run.LoadoutId).Launched++;
        profile.Open = new OpenVoyage(run.ShipClassId, run.LoadoutId, run.Seed);
    }

    /// <summary>
    /// Closes the recorded voyage with how its run ended: won, lost, or abandoned when the run is still in
    /// progress. Returns the end, which names the ship classes that it opened.
    /// </summary>
    public ClosedVoyage Finish(ProfileState profile, RunState run)
    {
        if (!IsOpen(profile, run))
        {
            throw new InvalidOperationException("The run is not the recorded voyage in progress.");
        }

        var outcome = run.Result switch
        {
            RunResult.Won => VoyageOutcome.Won,
            RunResult.Lost => VoyageOutcome.Lost,
            _ => VoyageOutcome.Abandoned,
        };
        return Close(profile, profile.Open!, outcome, ProgressOf(run));
    }

    /// <summary>
    /// Brings the profile in line with the saved voyage when the game starts, in case it stopped between the two
    /// files being written. A recorded voyage whose run is over is closed with that end. One whose save is gone,
    /// or holds another run, is closed as abandoned. Returns whether the profile changed.
    /// </summary>
    public bool Reconcile(ProfileState profile, RunState? saved)
    {
        if (profile.Open is not { } open)
        {
            return false;
        }

        if (saved is null || !IsOpen(profile, saved))
        {
            Close(profile, open, VoyageOutcome.Abandoned, progress: null);
            return true;
        }

        if (saved.Result == RunResult.InProgress)
        {
            return false;
        }

        Finish(profile, saved);
        return true;
    }

    private ClosedVoyage Close(ProfileState profile, OpenVoyage open, VoyageOutcome outcome, VoyageProgress? progress)
    {
        var locked = content.Ships.Where(s => !IsUnlocked(profile, s)).ToList();
        var record = RecordOf(profile, open.ShipClassId, open.LoadoutId);
        switch (outcome)
        {
            case VoyageOutcome.Won:
                record.Won++;
                break;
            case VoyageOutcome.Lost:
                record.Lost++;
                break;
            default:
                record.Abandoned++;
                break;
        }

        if (progress is not null && progress.CompareTo(record.Furthest) > 0)
        {
            record.Furthest = progress;
        }

        var opened = locked.Where(s => IsUnlocked(profile, s)).Select(s => s.Id).ToList();
        profile.Open = null;
        profile.Last = new ClosedVoyage(open.ShipClassId, open.LoadoutId, open.Seed, outcome, opened);
        return profile.Last;
    }

    private static LoadoutRecord RecordOf(ProfileState profile, string shipClassId, string loadoutId)
    {
        if (profile.Record(shipClassId, loadoutId) is { } record)
        {
            return record;
        }

        record = new LoadoutRecord { ShipClassId = shipClassId, LoadoutId = loadoutId };
        profile.Records.Add(record);
        return record;
    }
}
