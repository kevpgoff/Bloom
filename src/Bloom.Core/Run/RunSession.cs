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

    public RunSession(ContentDb content, RunState state, ISaveStore store)
    {
        Content = content;
        State = state;
        _store = store;
        Fitter = new ShipFitter(content);
        Combat = new CombatEngine(content);
        Rewards = new RewardEngine(content);
        Refit = new RefitEngine(content);
    }

    public ContentDb Content { get; }

    public RunState State { get; }

    public ShipFitter Fitter { get; }

    public CombatEngine Combat { get; }

    public RewardEngine Rewards { get; }

    public RefitEngine Refit { get; }

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
        var result = action(this);
        Save();
        return result;
    }

    public void Do(Action<RunSession> action)
    {
        action(this);
        Save();
    }

    public void Save() => _store.Save(SaveSerializer.Serialize(State));
}
