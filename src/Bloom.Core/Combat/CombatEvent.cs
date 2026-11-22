namespace Bloom.Core.Combat;

/// <summary>
/// Something that happened in a fight, reported by <see cref="CombatEngine"/> in the order it happened,
/// so the game can show it (particles, shake, haptics). Events are not state: they are never saved, and
/// the rules never read them. <see cref="Run.RunSession.LastEvents"/> holds the events of the last action.
/// </summary>
/// <remarks>
/// Card and piece ids are <see cref="Cards.CardInstance.Id"/> and <see cref="Pieces.PieceInstance.Id"/>.
/// A rejected action reports nothing, because the rules validate before they change anything.
/// </remarks>
public abstract record CombatEvent;

public sealed record FightStarted(string EnemyId) : CombatEvent;

/// <summary>A turn started: energy was refilled. The draws follow as <see cref="CardDrawn"/>.</summary>
public sealed record TurnStarted(int Turn) : CombatEvent;

public sealed record CardDrawn(int CardId) : CombatEvent;

/// <summary>The draw pile ran out, so the discard pile (<paramref name="Count"/> cards) was shuffled into it.</summary>
public sealed record DiscardShuffled(int Count) : CombatEvent;

/// <summary>The cards left in the hand went to the discard pile at the end of the turn.</summary>
public sealed record HandDiscarded(int Count) : CombatEvent;

/// <summary>A card left the hand and its energy was paid. Its effects follow, in the card's order.</summary>
public sealed record CardPlayed(int CardId, string CardDefId, int SourcePieceId) : CombatEvent;

/// <summary>The attack being played used up the pending focus; its damage already includes it.</summary>
public sealed record FocusSpent(int Amount) : CombatEvent;

/// <summary>
/// A damage effect hit the enemy. <paramref name="Blocked"/> went into its block and <paramref name="HullDamage"/>
/// got past it; a killing blow's hull damage can be more than the hull that was left.
/// </summary>
public sealed record EnemyHit(int Amount, int Blocked, int HullDamage, int HullLeft) : CombatEvent;

public sealed record BlockGained(int Amount, int Total) : CombatEvent;

public sealed record EnergyGained(int Amount, int Total) : CombatEvent;

public sealed record FocusGained(int Amount, int Total) : CombatEvent;

public sealed record PieceRepaired(int PieceId) : CombatEvent;

/// <summary>A card with grow was played and has now grown <paramref name="Growth"/> times this fight.</summary>
public sealed record CardGrew(int CardId, int Growth) : CombatEvent;

/// <summary>
/// The enemy attacked the ship. <paramref name="Blocked"/> went into the ship's block and <paramref name="HullDamage"/>
/// got past it. <paramref name="HeavyLane"/> is the lane of a heavy attack, or null for a normal one.
/// </summary>
public sealed record ShipHit(int Amount, int Blocked, int HullDamage, int HullLeft, int? HeavyLane) : CombatEvent;

/// <summary>A heavy attack disabled a piece; its cards went offline.</summary>
public sealed record PieceDisabled(int PieceId, int Lane) : CombatEvent;

public sealed record EnemyBlocked(int Amount, int Total) : CombatEvent;

/// <summary>The fight is over. Always the last event of its action.</summary>
public sealed record FightEnded(CombatOutcome Outcome) : CombatEvent;
