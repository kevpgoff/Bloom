using Bloom.Core.Cards;

namespace Bloom.Core.Combat;

public enum IntentKind
{
    Attack,
    /// <summary>Also disables the frontmost working piece in <see cref="Intent.Lane"/> if any damage gets past block.</summary>
    HeavyAttack,
    Block,
}

/// <summary>What an enemy will do on its next action. <see cref="Lane"/> is a grid column (0-based), for heavy attacks only.</summary>
public sealed record Intent(IntentKind Kind, int Amount, int? Lane = null);

/// <summary>An enemy as defined in enemies.json. It repeats <see cref="Pattern"/> in order.</summary>
public sealed record EnemyDef(string Id, string Name, int Hull, IReadOnlyList<Intent> Pattern, bool Placeholder = false);

public enum CombatOutcome
{
    Ongoing,
    Won,
    Lost,
}

/// <summary>How the most recent fight ended. It stays on the run after the fight, so the result can be shown again after a restart.</summary>
public sealed record FightRecord(string EnemyId, CombatOutcome Outcome, int Turns);

public sealed class EnemyState
{
    public required string DefId { get; set; }

    public int Hull { get; set; }

    public int MaxHull { get; set; }

    public int Block { get; set; }

    /// <summary>Index into the enemy's pattern; it wraps around.</summary>
    public int PatternIndex { get; set; }
}

/// <summary>
/// Everything about the current fight. The ship's hull points live on the run, not here, because
/// hull damage lasts between fights. The top of the draw pile is the last element.
/// </summary>
public sealed class CombatState
{
    public required EnemyState Enemy { get; set; }

    public int Turn { get; set; }

    public int Energy { get; set; }

    public int Block { get; set; }

    /// <summary>Extra damage for the next attack card. Focus cards add to it; it lasts until an attack uses it or the fight ends.</summary>
    public int PendingFocus { get; set; }

    public List<CardInstance> DrawPile { get; set; } = [];

    public List<CardInstance> Hand { get; set; } = [];

    public List<CardInstance> DiscardPile { get; set; } = [];

    /// <summary>Cards of disabled pieces. They are out of the deck until the piece is repaired.</summary>
    public List<CardInstance> OfflineCards { get; set; } = [];

    public List<int> DisabledPieceIds { get; set; } = [];
}
