using System.Diagnostics;
using Bloom.Core.Cards;
using Bloom.Core.Content;
using Bloom.Core.Pieces;
using Bloom.Core.Run;

namespace Bloom.Core.Combat;

/// <summary>What a card would do if it were played now, with every bonus applied. The UI shows this.</summary>
/// <param name="Effects">
/// The card's effects with final numbers: damage includes adjacency bonuses and pending focus, and
/// block includes growth. Compare with <see cref="CardDef.Effects"/> to see what was boosted.
/// </param>
/// <param name="Playable">Whether the card is in the hand and can be played now.</param>
/// <param name="RepairTargets">How many disabled pieces the player must pick to play it; 0 for cards that do not repair.</param>
public sealed record CardPreview(CardInstance Card, CardDef Def, IReadOnlyList<Effect> Effects, bool Playable, int RepairTargets);

/// <summary>
/// The turn cycle of a fight. Each public method that changes state is one player action and leaves
/// the run in a state that can be saved. Every method validates before it changes anything.
/// </summary>
/// <remarks>
/// Adjacency bonuses: <see cref="DamageBonusEffect"/> is worked out each time a card is played, from the
/// pieces that work at that moment. <see cref="StartGrownEffect"/> is given once, when the fight starts.
/// </remarks>
public sealed class CombatEngine(ContentDb content)
{
    private readonly ShipFitter _fitter = new(content);
    private readonly RewardEngine _rewards = new(content);

    /// <summary>Starts a fight against an enemy. Players start fights through <see cref="RefitEngine.StartNextFight"/>.</summary>
    public CombatState StartFight(RunState run, string enemyId)
    {
        switch (run.Phase)
        {
            case RunPhase.Fight:
                throw new InvalidOperationException("A fight is already in progress.");
            case RunPhase.Reward:
                throw new InvalidOperationException("Take or skip the reward first.");
            case RunPhase.Won or RunPhase.Lost:
                throw new InvalidOperationException("The run is over.");
        }

        var enemy = content.Enemy(enemyId);
        var combat = new CombatState
        {
            Enemy = new EnemyState { DefId = enemy.Id, Hull = enemy.Hull, MaxHull = enemy.Hull },
        };
        var deck = DeckBuilder.Build(run.Ship, content);
        GiveStartingGrowth(run, deck);
        combat.DrawPile.AddRange(deck);
        run.Rng.Shuffle(combat.DrawPile);
        run.Combat = combat;
        StartTurn(run, combat);
        return combat;
    }

    public Intent CurrentIntent(CombatState combat)
    {
        var pattern = content.Enemy(combat.Enemy.DefId).Pattern;
        return pattern[combat.Enemy.PatternIndex % pattern.Count];
    }

    /// <summary>
    /// The piece the enemy's next action would disable if its damage gets past block, or null when the
    /// next action is not a heavy attack or its lane has no working piece.
    /// </summary>
    public FittedPiece? HeavyAttackTarget(RunState run)
    {
        var combat = RequireCombat(run);
        return CurrentIntent(combat) is { Kind: IntentKind.HeavyAttack, Lane: int lane }
            ? _fitter.FrontmostInLane(run.Ship, lane, combat.DisabledPieceIds)
            : null;
    }

    public bool CanPlay(RunState run, int cardId) =>
        run.Combat is { } combat
        && combat.Hand.Find(c => c.Id == cardId) is { } card
        && CanPlay(combat, content.Card(card.CardId));

    /// <summary>How many disabled pieces the player must pick to play this card from the hand.</summary>
    public int RepairTargetsNeeded(RunState run, int cardId)
    {
        var combat = RequireCombat(run);
        var card = combat.Hand.Find(c => c.Id == cardId)
                   ?? throw new InvalidOperationException($"Card {cardId} is not in the hand.");
        return RepairTargetsNeeded(combat, content.Card(card.CardId));
    }

    public CardPreview Preview(RunState run, CardInstance card)
    {
        var combat = RequireCombat(run);
        var def = content.Card(card.CardId);
        bool playable = combat.Hand.Exists(c => c.Id == card.Id) && CanPlay(combat, def);
        return new CardPreview(card, def, ResolveEffects(run, combat, card, def), playable, RepairTargetsNeeded(combat, def));
    }

    /// <summary>
    /// Plays a card from the hand. A card that repairs needs <paramref name="repairTargets"/>: exactly
    /// <see cref="RepairTargetsNeeded"/> different disabled pieces, which come back in that order.
    /// </summary>
    public CombatOutcome PlayCard(RunState run, int cardId, IReadOnlyList<int>? repairTargets = null)
    {
        var combat = RequireCombat(run);
        int index = combat.Hand.FindIndex(c => c.Id == cardId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Card {cardId} is not in the hand.");
        }

        var card = combat.Hand[index];
        var def = content.Card(card.CardId);
        if (def.Cost > combat.Energy)
        {
            throw new InvalidOperationException($"'{def.Id}' costs {def.Cost} energy but only {combat.Energy} is left.");
        }

        if (HasRepair(def) && combat.DisabledPieceIds.Count == 0)
        {
            throw new InvalidOperationException($"'{def.Id}' repairs, but no piece is disabled.");
        }

        repairTargets ??= [];
        ValidateRepairTargets(combat, def, repairTargets);

        // Everything is checked. From here on the play cannot fail.
        var effects = ResolveEffects(run, combat, card, def);
        combat.Energy -= def.Cost;
        combat.Hand.RemoveAt(index);
        if (IsAttack(def))
        {
            // The resolved damage already includes the focus, so it is used up even if block absorbs it all.
            combat.PendingFocus = 0;
        }

        int nextTarget = 0;
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case DamageEffect damage:
                    DamageEnemy(combat.Enemy, damage.Amount);
                    break;
                case BlockEffect block:
                    combat.Block += block.Amount;
                    break;
                case GainEnergyEffect energy:
                    combat.Energy += energy.Amount;
                    break;
                case DrawEffect draw:
                    Draw(run, combat, draw.Count);
                    break;
                case FocusEffect focus:
                    combat.PendingFocus += focus.Amount;
                    break;
                case RepairEffect repair:
                    for (int i = 0; i < repair.Count && nextTarget < repairTargets.Count; i++)
                    {
                        Repair(combat, repairTargets[nextTarget++]);
                    }

                    break;
                case GrowEffect:
                    // Counted once per play, below.
                    break;
                default:
                    // The content validator rejects bonus effects on cards.
                    throw new UnreachableException($"Effect '{effect.GetType().Name}' cannot be on a card.");
            }
        }

        if (def.Effects.Any(e => e is GrowEffect))
        {
            card.Growth++;
        }

        combat.DiscardPile.Add(card);
        return combat.Enemy.Hull <= 0 ? EndFight(run, combat, CombatOutcome.Won) : CombatOutcome.Ongoing;
    }

    /// <summary>The enemy acts, unplayed cards go to the discard pile, block returns to 0, and the next turn starts.</summary>
    public CombatOutcome EndTurn(RunState run)
    {
        var combat = RequireCombat(run);
        EnemyActs(run, combat);
        if (run.Hull <= 0)
        {
            return EndFight(run, combat, CombatOutcome.Lost);
        }

        combat.DiscardPile.AddRange(combat.Hand);
        combat.Hand.Clear();
        combat.Block = 0;
        StartTurn(run, combat);
        return CombatOutcome.Ongoing;
    }

    /// <summary>Cards drawn at the start of a turn: one fewer while the bridge is disabled.</summary>
    internal int CardsToDraw(RunState run, CombatState combat)
    {
        bool bridgeDisabled = combat.DisabledPieceIds.Any(id =>
            run.Ship.Find(id) is { } fitted && content.Piece(fitted.Piece.DefId).IsBridge);
        return Rules.CardsDrawnPerTurn - (bridgeDisabled ? 1 : 0);
    }

    private static bool IsAttack(CardDef def) => def.Effects.Any(e => e is DamageEffect);

    private static bool HasRepair(CardDef def) => def.Effects.Any(e => e is RepairEffect);

    private static bool CanPlay(CombatState combat, CardDef def) =>
        def.Cost <= combat.Energy && (!HasRepair(def) || combat.DisabledPieceIds.Count > 0);

    private static int RepairTargetsNeeded(CombatState combat, CardDef def) =>
        Math.Min(def.Effects.OfType<RepairEffect>().Sum(e => e.Count), combat.DisabledPieceIds.Count);

    private static void ValidateRepairTargets(CombatState combat, CardDef def, IReadOnlyList<int> targets)
    {
        int needed = RepairTargetsNeeded(combat, def);
        if (targets.Count != needed)
        {
            throw new InvalidOperationException(needed == 0
                ? $"'{def.Id}' does not repair, so it takes no pieces to repair."
                : $"'{def.Id}' needs {needed} disabled piece(s) to repair, but {targets.Count} were picked.");
        }

        if (targets.Distinct().Count() != targets.Count)
        {
            throw new InvalidOperationException("A piece can be picked for repair only once.");
        }

        foreach (int pieceId in targets)
        {
            if (!combat.DisabledPieceIds.Contains(pieceId))
            {
                throw new InvalidOperationException($"Piece {pieceId} is not disabled.");
            }
        }
    }

    /// <summary>The card's effects with adjacency bonuses, pending focus and growth applied.</summary>
    private List<Effect> ResolveEffects(RunState run, CombatState combat, CardInstance card, CardDef def)
    {
        int extraDamage = BonusesReceived(run, combat.DisabledPieceIds, card.SourcePieceId)
            .OfType<DamageBonusEffect>().Sum(e => e.Amount) + combat.PendingFocus;
        int extraBlock = def.Effects.OfType<GrowEffect>().Sum(e => e.Amount) * card.Growth;
        return def.Effects.Select(effect => effect switch
        {
            DamageEffect damage => damage with { Amount = damage.Amount + extraDamage },
            BlockEffect block => block with { Amount = block.Amount + extraBlock },
            _ => effect,
        }).ToList();
    }

    private IEnumerable<BonusEffect> BonusesReceived(RunState run, IReadOnlyCollection<int> disabledPieceIds, int pieceId) =>
        _fitter.ActiveBonuses(run.Ship, disabledPieceIds)
            .Where(b => b.ReceiverId == pieceId)
            .Select(b => b.Bonus.Effect);

    /// <summary>No piece is disabled when a fight starts, so every bonus applies.</summary>
    private void GiveStartingGrowth(RunState run, List<CardInstance> deck)
    {
        foreach (var card in deck)
        {
            card.Growth = BonusesReceived(run, [], card.SourcePieceId).OfType<StartGrownEffect>().Sum(e => e.Times);
        }
    }

    private void StartTurn(RunState run, CombatState combat)
    {
        combat.Turn++;
        combat.Energy = Rules.EnergyPerTurn;
        Draw(run, combat, CardsToDraw(run, combat));
    }

    private void EnemyActs(RunState run, CombatState combat)
    {
        var enemy = combat.Enemy;
        var intent = CurrentIntent(combat);

        // Enemy block lasts until the enemy's next action.
        enemy.Block = 0;
        switch (intent.Kind)
        {
            case IntentKind.Attack:
                DamageShip(run, combat, intent.Amount);
                break;
            case IntentKind.HeavyAttack:
                int hullDamage = DamageShip(run, combat, intent.Amount);
                if (hullDamage > 0 && intent.Lane is int lane)
                {
                    DisableFrontmost(run, combat, lane);
                }

                break;
            case IntentKind.Block:
                enemy.Block += intent.Amount;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(combat), intent.Kind, "Unknown intent.");
        }

        enemy.PatternIndex++;
    }

    /// <summary>Block absorbs damage first; the rest reduces hull points. Returns the hull damage.</summary>
    private static int DamageShip(RunState run, CombatState combat, int amount)
    {
        int absorbed = Math.Min(combat.Block, amount);
        combat.Block -= absorbed;
        int hullDamage = amount - absorbed;
        run.Hull = Math.Max(0, run.Hull - hullDamage);
        return hullDamage;
    }

    private static void DamageEnemy(EnemyState enemy, int amount)
    {
        int absorbed = Math.Min(enemy.Block, amount);
        enemy.Block -= absorbed;
        enemy.Hull = Math.Max(0, enemy.Hull - (amount - absorbed));
    }

    private void DisableFrontmost(RunState run, CombatState combat, int lane)
    {
        if (_fitter.FrontmostInLane(run.Ship, lane, combat.DisabledPieceIds) is not { } target)
        {
            return;
        }

        int pieceId = target.Piece.Id;
        combat.DisabledPieceIds.Add(pieceId);
        foreach (var pile in new[] { combat.DrawPile, combat.Hand, combat.DiscardPile })
        {
            combat.OfflineCards.AddRange(pile.Where(c => c.SourcePieceId == pieceId));
            pile.RemoveAll(c => c.SourcePieceId == pieceId);
        }
    }

    /// <summary>The piece works again and its cards, with their growth, go to the discard pile.</summary>
    private static void Repair(CombatState combat, int pieceId)
    {
        combat.DisabledPieceIds.Remove(pieceId);
        combat.DiscardPile.AddRange(combat.OfflineCards.Where(c => c.SourcePieceId == pieceId));
        combat.OfflineCards.RemoveAll(c => c.SourcePieceId == pieceId);
    }

    private static void Draw(RunState run, CombatState combat, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (combat.DrawPile.Count == 0)
            {
                if (combat.DiscardPile.Count == 0)
                {
                    return;
                }

                combat.DrawPile.AddRange(combat.DiscardPile);
                combat.DiscardPile.Clear();
                run.Rng.Shuffle(combat.DrawPile);
            }

            var top = combat.DrawPile[^1];
            combat.DrawPile.RemoveAt(combat.DrawPile.Count - 1);
            combat.Hand.Add(top);
        }
    }

    /// <summary>
    /// Records the result on the run. Disabled pieces come back at no cost: the next fight rebuilds the
    /// deck from the ship. A win counts toward the run: after the last encounter the run is won, and
    /// otherwise a reward is offered. All of it happens in the same action, so it is saved together.
    /// </summary>
    private CombatOutcome EndFight(RunState run, CombatState combat, CombatOutcome outcome)
    {
        run.Combat = null;
        run.LastFight = new FightRecord(combat.Enemy.DefId, outcome, combat.Turn);
        if (outcome == CombatOutcome.Lost)
        {
            run.Result = RunResult.Lost;
            return outcome;
        }

        run.FightsWon++;
        if (run.FightsWon >= run.Encounters.Count)
        {
            run.Result = RunResult.Won;
        }
        else
        {
            run.Reward = _rewards.Roll(run);
        }

        return outcome;
    }

    private static CombatState RequireCombat(RunState run) =>
        run.Combat ?? throw new InvalidOperationException("No fight is in progress.");
}
