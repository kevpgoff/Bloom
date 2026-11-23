using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Run;
using Bloom.Core.Save;
using Bloom.Game.Dev;
using Bloom.Game.Platform;
using Godot;

namespace Bloom.Game.Autoload;

/// <summary>
/// Autoloaded singleton that owns the content and the current run. Screens read from it and send
/// every player action through <see cref="Act"/>, which runs it in <see cref="RunSession.Do(Action{RunSession})"/>
/// (so it is saved) and then raises <see cref="CombatEvents"/> and <see cref="Changed"/>.
/// </summary>
/// <remarks>
/// <para>
/// There is one voyage at a time. A launch of the game resumes the saved run. Without one there is no
/// <see cref="Session"/>, and the hangar shows: there the player chooses a ship, a loadout and a seed, and
/// <see cref="Launch"/> starts the run. <see cref="LeaveRun"/> ends a run, finished or not, and goes back to the hangar.
/// </para>
/// <para>
/// A debug build also has test runs (<see cref="TestTools"/>). A test run is saved in a file of its own, so it
/// never replaces the voyage. The map's Hangar button sets the voyage aside (<see cref="ParkVoyage"/>) without
/// ending it; while it waits, the hangar launches only test runs (<see cref="LaunchTest"/>), and
/// <see cref="ResumeVoyage"/> goes back to it. A launch of the game resumes a test run before the voyage.
/// </para>
/// <para>
/// With <c>-- --fx-demo</c> in a debug build, the run is kept in memory with a fixed seed and an
/// <see cref="FxDemo"/> plays it, so the saved run is never touched. With <c>--ship=&lt;id&gt;</c> as well, the
/// demo plays that ship class.
/// </para>
/// </remarks>
public partial class GameRoot : Node
{
    public const string DataDir = "res://data";

    /// <summary>The ship class the FX demo plays when <c>--ship=</c> names none.</summary>
    public const string DemoShip = "seedling";

    private const string UnreadableSaveCopy = "user://run.unreadable.json";
    private const string TestSave = "user://run.test.json";
    private const string ShipFlag = "--ship=";

    private const ulong DemoSeed = 7;

    private readonly GodotSaveStore _fileStore = new();
    private readonly GodotSaveStore _testStore = new(TestSave);
    private ISaveStore _store = null!;

    /// <summary>The voyage that waits while the hangar's test tools are in use, or null.</summary>
    private RunSession? _parked;

    public static GameRoot Instance { get; private set; } = null!;

    public ContentDb? Content { get; private set; }

    /// <summary>
    /// Whether this build has the test tools: the hangar's Test tab and the map's Hangar button. Debug builds
    /// only, and not while the FX demo plays.
    /// </summary>
    public static bool TestTools => OS.IsDebugBuild() && !FxDemo.IsRequested;

    /// <summary>The run in progress or just finished, or null while the hangar shows.</summary>
    public RunSession? Session { get; private set; }

    /// <summary>Whether <see cref="Session"/> is a test run, which is saved apart from the voyage.</summary>
    public bool IsTest { get; private set; }

    /// <summary>Whether a voyage waits behind the hangar or a test run (<see cref="ParkVoyage"/>).</summary>
    public bool VoyageParked => _parked is not null;

    /// <summary>One line for the status label, shown when no run could be started.</summary>
    public string Status { get; private set; } = "";

    /// <summary>
    /// Whether the Fitting screen is open over the sector map or a dock. It is only which screen shows, not
    /// part of the run, so it is not saved: a resumed run opens on the map or the dock.
    /// </summary>
    public bool ShipOpen { get; private set; }

    /// <summary>Raised after every action, after a run starts and after one is left. Screens redraw on it.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised after an action that changed a fight, with what happened in order (<see cref="RunSession.LastEvents"/>),
    /// before <see cref="Changed"/>. The combat screen plays them as feedback.
    /// </summary>
    public event Action<IReadOnlyList<CombatEvent>>? CombatEvents;

    public override void _Ready()
    {
        Instance = this;
        UserSettings.Load();
        bool demo = FxDemo.IsRequested;
        _store = demo ? new MemorySaveStore() : _fileStore;
        try
        {
            Content = GodotContentSource.Load(DataDir);
        }
        catch (ContentException e)
        {
            Status = "Content failed to load. See the output log.";
            GD.PushError(e.Message);
            return;
        }

        Status = $"Content loaded: pieces {Content.Pieces.Count}, cards {Content.Cards.Count}, " +
                 $"enemies {Content.Enemies.Count}, ship classes {Content.Ships.Count}, hull sections {Content.Sections.Count}, " +
                 $"events {Content.Events.Count}.";
        GD.Print($"GameRoot: {Status}");

        if (demo)
        {
            string ship = RequestedShip(Content) ?? DemoShip;
            Session = RunSession.StartNew(Content, _store, new RunSetup(ship, DemoSeed));
            AddChild(new FxDemo());
            GD.Print($"GameRoot: FX demo ({ship}); the run is kept in memory and not saved.");
            return;
        }

        try
        {
            Session = RunSession.TryResume(Content, _store);
        }
        catch (SaveException e)
        {
            // The next launch from the hangar overwrites the file, so keep a copy of it first.
            _fileStore.CopyTo(UnreadableSaveCopy);
            GD.PushWarning($"GameRoot: the saved run could not be read; a copy is at {UnreadableSaveCopy}. {e.Message}");
        }

        if (TestTools && ResumeTest(Content) is { } test)
        {
            _parked = Session;
            Session = test;
            IsTest = true;
        }

        GD.Print(Session is { } resumed
            ? $"GameRoot: resumed the saved {(IsTest ? "test run" : "run")} ({resumed.State.ShipClassId}, loadout {resumed.State.LoadoutId}, " +
              $"seed {SeedOf(resumed.State)}){(VoyageParked ? "; a voyage waits behind it" : "")}."
            : "GameRoot: no saved run; the hangar is open.");
    }

    /// <summary>The saved test run, or null. One that cannot be read is deleted: it was only a test.</summary>
    private RunSession? ResumeTest(ContentDb content)
    {
        try
        {
            return RunSession.TryResume(content, _testStore);
        }
        catch (SaveException e)
        {
            _testStore.Delete();
            GD.PushWarning($"GameRoot: the saved test run could not be read and was deleted. {e.Message}");
            return null;
        }
    }

    /// <summary>A run's seed as the player reads it: its code or typed text, or the number for a run started without one.</summary>
    public static string SeedOf(RunState run) => run.SeedText ?? run.Seed.ToString();

    /// <summary>
    /// A new seed code for the hangar to offer. This is the only non-deterministic step: everything after it
    /// comes from the run's own RNG.
    /// </summary>
    public static string NewSeedCode() => RunSeed.CodeFrom(((ulong)GD.Randi() << 32) | GD.Randi());

    /// <summary>Starts the run chosen in the hangar and saves it. It opens on the map of its first sector.</summary>
    public RunSession Launch(RunSetup setup)
    {
        var content = Content ?? throw new InvalidOperationException("Content is not loaded.");
        if (VoyageParked)
        {
            // Saving a new voyage would replace the one that waits.
            throw new InvalidOperationException("A voyage is waiting. Return to it and abandon it before launching another.");
        }

        ShipOpen = false;
        IsTest = false;
        Session = RunSession.StartNew(content, _store, setup);
        GD.Print($"GameRoot: launched a new run ({Session.State.ShipClassId}, loadout {Session.State.LoadoutId}, seed {SeedOf(Session.State)}).");
        Changed?.Invoke();
        return Session;
    }

    /// <summary>
    /// Starts a test run and saves it apart from the voyage: at a node of the map (<see cref="TestStart"/>), or
    /// before the first row when <paramref name="start"/> is null. Only with <see cref="TestTools"/>.
    /// </summary>
    public RunSession LaunchTest(RunSetup setup, TestStart? start)
    {
        var content = Content ?? throw new InvalidOperationException("Content is not loaded.");
        if (!TestTools)
        {
            throw new InvalidOperationException("Test runs are for debug builds.");
        }

        var session = start is null ? RunSession.StartNew(content, _testStore, setup) : RunSession.StartTest(content, _testStore, setup, start);
        ShipOpen = false;
        IsTest = true;
        Session = session;
        GD.Print($"GameRoot: launched a test run ({session.State.ShipClassId}, loadout {session.State.LoadoutId}, seed {SeedOf(session.State)}), " +
                 $"{(start is null ? "on the map" : $"at the first {start.Node} node{(start.EventId is { } ev ? $" with the event '{ev}'" : "")}")}: {session.State.Phase}.");
        Changed?.Invoke();
        return session;
    }

    /// <summary>
    /// Ends the run, whether it is finished or abandoned on the way, and goes back to the hangar. Its save is
    /// deleted, so the next launch of the game opens on the hangar too, unless a voyage waits behind a test run.
    /// </summary>
    public void LeaveRun()
    {
        if (Session is not { } session)
        {
            return;
        }

        GD.Print($"GameRoot: left the {(IsTest ? "test run" : "run")} ({session.State.ShipClassId}, {session.State.Phase}); the hangar is open.");
        (IsTest ? _testStore : _store).Delete();
        Session = null;
        IsTest = false;
        ShipOpen = false;
        Changed?.Invoke();
    }

    /// <summary>
    /// Sets the voyage aside and opens the hangar for testing. The voyage and its save stay as they are until
    /// <see cref="ResumeVoyage"/>. Only with <see cref="TestTools"/>, and only for a voyage that is not over.
    /// </summary>
    public void ParkVoyage()
    {
        if (!TestTools || IsTest || Session is not { State.Result: RunResult.InProgress } session)
        {
            return;
        }

        GD.Print($"GameRoot: the voyage ({session.State.ShipClassId}, seed {SeedOf(session.State)}) waits; the hangar is open for testing.");
        _parked = session;
        Session = null;
        ShipOpen = false;
        Changed?.Invoke();
    }

    /// <summary>Goes back from the hangar to the voyage that <see cref="ParkVoyage"/> set aside.</summary>
    public void ResumeVoyage()
    {
        if (Session is not null || _parked is not { } voyage)
        {
            return;
        }

        GD.Print($"GameRoot: back to the voyage ({voyage.State.ShipClassId}, seed {SeedOf(voyage.State)}).");
        _parked = null;
        Session = voyage;
        Changed?.Invoke();
    }

    /// <summary>
    /// Debug launch option <c>-- --ship=&lt;id&gt;</c> chooses the ship class that the FX demo plays. Returns null
    /// when omitted or when the id is unknown (after logging an error).
    /// </summary>
    private static string? RequestedShip(ContentDb content)
    {
        if (!OS.IsDebugBuild())
        {
            return null;
        }

        string? arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(ShipFlag, StringComparison.Ordinal));
        if (arg is null)
        {
            return null;
        }

        string id = arg[ShipFlag.Length..];
        if (content.Ships.Any(s => s.Id == id))
        {
            return id;
        }

        GD.PushError($"GameRoot: unknown ship class '{id}' in {ShipFlag}; using '{DemoShip}'.");
        return null;
    }

    /// <summary>Opens or closes the Fitting screen over the map or a dock. Changes no state, so nothing is saved.</summary>
    public void ShowShip(bool open)
    {
        if (ShipOpen != open)
        {
            ShipOpen = open;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Runs one player action through the session, which saves after it, then raises <see cref="Changed"/>.
    /// Returns false when the rules reject the action; they reject it before changing anything, so the
    /// state is still valid.
    /// </summary>
    public bool Act(Action<RunSession> action)
    {
        if (Session is not { } session)
        {
            return false;
        }

        bool done = true;
        try
        {
            session.Do(action);
        }
        catch (InvalidOperationException e)
        {
            GD.PushWarning($"GameRoot: action rejected: {e.Message}");
            done = false;
        }

        if (done && session.LastEvents.Count > 0)
        {
            CombatEvents?.Invoke(session.LastEvents);
        }

        Changed?.Invoke();
        return done;
    }
}
