using System.Text.Json.Serialization;
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

/// <summary>Which fights of a sector an enemy can be met in.</summary>
public enum EnemyPool
{
    /// <summary>Skirmishes on the first rows of the map (<see cref="Rules.MapEarlyRows"/>).</summary>
    Early,

    /// <summary>Skirmishes on the later rows.</summary>
    Late,

    /// <summary>Hard fights. Their reward includes a rare piece.</summary>
    Hard,

    /// <summary>The fight that ends a sector.</summary>
    Boss,
}

/// <summary>
/// A part of an enemy, such as a boss's arm. It has its own hull and repeats its own pattern, acting every turn
/// after the body, until its hull reaches 0 and it is disabled. Its block goes into the enemy's one block.
/// </summary>
public sealed record EnemyPartDef(string Name, int Hull, IReadOnlyList<Intent> Pattern);

/// <summary>
/// An enemy as defined in enemies.json. Its body repeats <see cref="Pattern"/> in order, and each of its
/// <see cref="Parts"/> (a boss's) repeats its own. The fight is won when the body's hull reaches 0.
/// <see cref="Pool"/> is required in data; it is nullable only so the validator can report it missing.
/// </summary>
public sealed record EnemyDef(
    string Id,
    string Name,
    int Hull,
    IReadOnlyList<Intent> Pattern,
    EnemyPool? Pool = null,
    int Sector = 1,
    bool Placeholder = false,
    IReadOnlyList<EnemyPartDef>? Parts = null)
{
    /// <summary>The parts, or none when the data leaves them out.</summary>
    [JsonIgnore]
    public IReadOnlyList<EnemyPartDef> AllParts => Parts ?? [];

    /// <summary>Every intent the enemy can show: the body's pattern, then each part's.</summary>
    [JsonIgnore]
    public IEnumerable<Intent> AllIntents => Pattern.Concat(AllParts.SelectMany(p => p.Pattern));
}

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

    /// <summary>One for each of the enemy's parts, in the order of <see cref="EnemyDef.Parts"/>. Empty for an enemy without parts.</summary>
    public List<PartState> Parts { get; set; } = [];
}

/// <summary>A part of the enemy in a fight. It is disabled once its hull reaches 0, and then it no longer acts.</summary>
public sealed class PartState
{
    public int Hull { get; set; }

    public int MaxHull { get; set; }

    /// <summary>Index into the part's pattern; it wraps around. It moves on only while the part works.</summary>
    public int PatternIndex { get; set; }

    [JsonIgnore]
    public bool Disabled => Hull <= 0;
}

/// <summary>
/// Everything about the current fight. The ship's hull points live on the run, not here, because
/// hull damage lasts between fights. The top of the draw pile is the last element.
/// </summary>
public sealed class CombatState
{
    public required EnemyState Enemy { get; set; }

    /// <summary>
    /// What the player's attacks hit: the index of one of the enemy's working parts, or null for the body. It stays
    /// until the player changes it, and goes back to the body when its part is disabled.
    /// </summary>
    public int? Target { get; set; }

    public int Turn { get; set; }

    public int Energy { get; set; }

    public int Block { get; set; }

    /// <summary>Extra damage for the next attack card. Focus cards add to it; it lasts until an attack uses it or the fight ends.</summary>
    public int PendingFocus { get; set; }

    public List<CardInstance> DrawPile { get; set; } = [];

    public List<CardInstance> Hand { get; set; } = [];

    public List<CardInstance> DiscardPile { get; set; } = [];

    /// <summary>
    /// The cards played this turn, in the order they were played. They join the discard pile when the turn ends.
    /// Until then no shuffle reaches them, so a card that was played cannot be drawn again in the same turn.
    /// </summary>
    public List<CardInstance> PlayedPile { get; set; } = [];

    /// <summary>Cards of disabled pieces. They are out of the deck until the piece is repaired.</summary>
    public List<CardInstance> OfflineCards { get; set; } = [];

    public List<int> DisabledPieceIds { get; set; } = [];
}
