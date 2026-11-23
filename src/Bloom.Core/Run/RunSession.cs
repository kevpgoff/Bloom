using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Pieces;
using Bloom.Core.Save;

namespace Bloom.Core.Run;

/// <summary>
/// A run plus the services that act on it. Every player action goes through <see cref="Do"/>, which
/// saves right after it, so the player can stop at any moment and come back to the exact same state.
/// </summary>
public sealed class RunSession
{
    private readonly ISaveStore _store;
    private readonly List<CombatEvent> _pending = [];

    public RunSession(ContentDb content, RunState state, ISaveStore store)
    {
        Content = content;
        State = state;
        _store = store;
        Fitter = new ShipFitter(content);
        Combat = new CombatEngine(content, _pending.Add);
        Rewards = new RewardEngine(content);
        Refit = new RefitEngine(content, Combat);
        Map = new MapEngine(content);
        Grove = new GroveEngine(content);
        Dock = new DockEngine(content);
        Shipyard = new ShipyardEngine(content);
    }

    public ContentDb Content { get; }

    public RunState State { get; }

    public ShipFitter Fitter { get; }

    public CombatEngine Combat { get; }

    public RewardEngine Rewards { get; }

    public RefitEngine Refit { get; }

    public MapEngine Map { get; }

    public GroveEngine Grove { get; }

    public DockEngine Dock { get; }

    public ShipyardEngine Shipyard { get; }

    /// <summary>
    /// What happened in the fight during the last <see cref="Do(Action{RunSession})"/>, in order, for the game to
    /// show. Empty after an action that was rejected or changed nothing in a fight. Not saved, so empty after a resume.
    /// </summary>
    public IReadOnlyList<CombatEvent> LastEvents { get; private set; } = [];

    public static RunSession StartNew(ContentDb content, ISaveStore store, string shipClassId, ulong seed)
    {
        var session = new RunSession(content, RunFactory.NewRun(content, shipClassId, seed), store);
        session.Save();
        return session;
    }

    /// <summary>Returns the saved run, or null when there is none.</summary>
    public static RunSession? TryResume(ContentDb content, ISaveStore store) =>
        store.Load() is { } json ? new RunSession(content, SaveSerializer.Deserialize(json), store) : null;

    /// <summary>
    /// Runs one player action, then saves. If the action throws, nothing is saved; rule methods
    /// validate before they change state, so the state in memory stays valid too.
    /// </summary>
    public T Do<T>(Func<RunSession, T> action)
    {
        BeginAction();
        var result = action(this);
        EndAction();
        return result;
    }

    public void Do(Action<RunSession> action)
    {
        BeginAction();
        action(this);
        EndAction();
    }

    public void Save() => _store.Save(SaveSerializer.Serialize(State));

    private void BeginAction()
    {
        _pending.Clear();
        LastEvents = [];
    }

    private void EndAction()
    {
        LastEvents = _pending.ToArray();
        _pending.Clear();
        Save();
    }
}
