using System.Diagnostics;
using Bloom.Core.Cards;
using Bloom.Core.Content;
using Bloom.Core.Pieces;
using Bloom.Core.Run;

namespace Bloom.Core.Combat;

/// <summary>What a card would do if it were played now, with every bonus applied. The UI shows this.</summary>
/// <param name="Def">The card as it plays: upgraded when its piece is (see <see cref="ContentDb.Card(CardInstance)"/>).</param>
/// <param name="Effects">
/// The card's effects with final numbers: damage includes adjacency bonuses and pending focus, and
/// block includes growth. Compare with <see cref="CardDef.Effects"/> to see what was boosted.
/// </param>
/// <param name="Playable">Whether the card is in the hand and can be played now.</param>
/// <param name="RepairTargets">How many disabled pieces the player must pick to play it; 0 for cards that do not repair.</param>
public sealed record CardPreview(CardInstance Card, CardDef Def, IReadOnlyList<Effect> Effects, bool Playable, int RepairTargets);

/// <summary>One thing the enemy will do at the end of the turn, with its body (<paramref name="Part"/> null) or one of its working parts.</summary>
/// <param name="Lane">
/// For a heavy attack, the lane it hits: the lane it aims at, or the nearest one the hull reaches
/// (<see cref="Grid.ShipGrid.LaneHit"/>). Null for any other intent.
/// </param>
/// <param name="Target">
/// For a heavy attack, the piece it disables if its damage gets past block: the frontmost working piece in its lane,
/// behind any that an earlier heavy attack of the same turn would disable. Null when the lane has none left.
/// </param>
public sealed record EnemyAction(int? Part, Intent Intent, int? Lane = null, FittedPiece? Target = null);

/// <summary>
/// The turn cycle of a fight. Each public method that changes state is one player action and leaves
/// the run in a state that can be saved. Every method validates before it changes anything.
/// </summary>
/// <remarks>
/// <para>
/// Adjacency bonuses: <see cref="DamageBonusEffect"/> is worked out each time a card is played, from the
/// pieces that work at that moment. <see cref="StartGrownEffect"/> is given once, when the fight starts.
/// </para>
/// <para>
/// An enemy with parts (a boss) acts with its body and then with each working part, every turn. The player's
/// attacks hit <see cref="CombatState.Target"/>: the body, or a part chosen with <see cref="SetTarget"/>. A part
/// whose hull reaches 0 is disabled, and the aim goes back to the body. The fight is won when the body's hull reaches 0.
/// </para>
/// <para>
/// Each change is reported to <paramref name="emit"/> as a <see cref="CombatEvent"/>, in the order it
/// happens, so the game can show it. Reporting happens only after validation, so a rejected action reports nothing.
/// </para>
/// </remarks>
public sealed class CombatEngine(ContentDb content, Action<CombatEvent>? emit = null)
{
    private readonly ShipFitter _fitter = new(content);
    private readonly RewardEngine _rewards = new(content);

    /// <summary>
    /// Starts a fight against an enemy. Players start fights through <see cref="RefitEngine.StartFight"/>,
    /// against the enemy of the node they chose; tests may start one from the map.
    /// </summary>
    public CombatState StartFight(RunState run, string enemyId)
    {
        switch (run.Phase)
        {
            case RunPhase.Fight:
                throw new InvalidOperationException("A fight is already in progress.");
            case RunPhase.Reward:
                throw new InvalidOperationException("Take or skip the reward first.");
            case RunPhase.Grove:
                throw new InvalidOperationException("Finish at the grove first.");
            case RunPhase.Dock:
                throw new InvalidOperationException("Leave the dock first.");
            case RunPhase.Shipyard:
                throw new InvalidOperationException("Finish at the shipyard first.");
            case RunPhase.Event:
                throw new InvalidOperationException("Finish the event first.");
            case RunPhase.Won or RunPhase.Lost:
                throw new InvalidOperationException("The run is over.");
        }

        var enemy = content.Enemy(enemyId);
        var combat = new CombatState
        {
            Enemy = new EnemyState
            {
                DefId = enemy.Id,
                Hull = enemy.Hull,
                MaxHull = enemy.Hull,
                Parts = enemy.AllParts.Select(p => new PartState { Hull = p.Hull, MaxHull = p.Hull }).ToList(),
            },
        };
        var deck = DeckBuilder.Build(run.Ship, content);
        GiveStartingGrowth(run, deck);
        combat.DrawPile.AddRange(deck);
        run.Rng.Shuffle(combat.DrawPile);
        run.Combat = combat;
        Emit(new FightStarted(enemy.Id));
        StartTurn(run, combat);
        return combat;
    }

    /// <summary>What the enemy will do at the end of this turn, in order: its body, then each working part.</summary>
    public IReadOnlyList<EnemyAction> NextActions(RunState run)
    {
        var combat = RequireCombat(run);
        var enemy = combat.Enemy;
        var def = content.Enemy(enemy.DefId);
        var disabled = combat.DisabledPieceIds.ToList();
        var actions = new List<EnemyAction>();
        void Add(int? part, Intent intent)
        {
            if (intent is not { Kind: IntentKind.HeavyAttack, Lane: int aimed })
            {
                actions.Add(new EnemyAction(part, intent));
                return;
            }

            int lane = run.Ship.Grid.LaneHit(aimed);
            var target = _fitter.FrontmostInLane(run.Ship, lane, disabled);
            if (target is not null)
            {
                disabled.Add(target.Piece.Id);
            }

            actions.Add(new EnemyAction(part, intent, lane, target));
        }

        Add(null, Next(def.Pattern, enemy.PatternIndex));
        for (int i = 0; i < enemy.Parts.Count; i++)
        {
            if (!enemy.Parts[i].Disabled)
            {
                Add(i, Next(def.AllParts[i].Pattern, enemy.Parts[i].PatternIndex));
            }
        }

        return actions;
    }

    /// <summary>Whether the player's attacks can be aimed at this part (or the body, for null) now.</summary>
    public bool CanTarget(RunState run, int? part) =>
        run.Combat is { } combat
        && (part is not int index || (index >= 0 && index < combat.Enemy.Parts.Count && !combat.Enemy.Parts[index].Disabled));

    /// <summary>
    /// Aims the player's attacks at one of the enemy's working parts, or at its body (<paramref name="part"/> null).
    /// The aim stays until it is changed or its part is disabled. Aiming where it already points changes nothing.
    /// </summary>
    public void SetTarget(RunState run, int? part)
    {
        var combat = RequireCombat(run);
        if (part is int index)
        {
            var def = content.Enemy(combat.Enemy.DefId);
            if (index < 0 || index >= combat.Enemy.Parts.Count)
            {
                throw new InvalidOperationException($"The {def.Name} has no part {index}.");
            }

            if (combat.Enemy.Parts[index].Disabled)
            {
                throw new InvalidOperationException($"The {def.AllParts[index].Name} is disabled.");
            }
        }

        if (combat.Target == part)
        {
            return;
        }

        combat.Target = part;
        Emit(new TargetChanged(part));
    }

    public bool CanPlay(RunState run, int cardId) =>
        run.Combat is { } combat
        && combat.Hand.Find(c => c.Id == cardId) is { } card
        && CanPlay(combat, content.Card(card));

    /// <summary>How many disabled pieces the player must pick to play this card from the hand.</summary>
    public int RepairTargetsNeeded(RunState run, int cardId)
    {
        var combat = RequireCombat(run);
        var card = combat.Hand.Find(c => c.Id == cardId)
                   ?? throw new InvalidOperationException($"Card {cardId} is not in the hand.");
        return RepairTargetsNeeded(combat, content.Card(card));
    }

    public CardPreview Preview(RunState run, CardInstance card)
    {
        var combat = RequireCombat(run);
        var def = content.Card(card);
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
        var def = content.Card(card);
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
        Emit(new CardPlayed(card.Id, def.Id, card.SourcePieceId, card.Upgraded));
        if (IsAttack(def))
        {
            // The resolved damage already includes the focus, so it is used up even if block absorbs it all.
            if (combat.PendingFocus > 0)
            {
                Emit(new FocusSpent(combat.PendingFocus));
            }

            combat.PendingFocus = 0;
        }

        int nextTarget = 0;
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case DamageEffect damage:
                    DamageEnemy(combat, damage.Amount);
                    break;
                case BlockEffect block:
                    combat.Block += block.Amount;
                    Emit(new BlockGained(block.Amount, combat.Block));
                    break;
                case GainEnergyEffect energy:
                    combat.Energy += energy.Amount;
                    Emit(new EnergyGained(energy.Amount, combat.Energy));
                    break;
                case DrawEffect draw:
                    Draw(run, combat, draw.Count);
                    break;
                case FocusEffect focus:
                    combat.PendingFocus += focus.Amount;
                    Emit(new FocusGained(focus.Amount, combat.PendingFocus));
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
            Emit(new CardGrew(card.Id, card.Growth));
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

        if (combat.Hand.Count > 0)
        {
            Emit(new HandDiscarded(combat.Hand.Count));
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
        Emit(new TurnStarted(combat.Turn));
        Draw(run, combat, CardsToDraw(run, combat));
    }

    private static Intent Next(IReadOnlyList<Intent> pattern, int index) => pattern[index % pattern.Count];

    /// <summary>
    /// The body acts, then each working part in order. They stop once the ship's hull is gone. The enemy's block,
    /// which the body and parts share, lasts until the enemy's next turn.
    /// </summary>
    private void EnemyActs(RunState run, CombatState combat)
    {
        var enemy = combat.Enemy;
        var def = content.Enemy(enemy.DefId);
        enemy.Block = 0;
        Act(run, combat, null, Next(def.Pattern, enemy.PatternIndex++));
        for (int i = 0; i < enemy.Parts.Count && run.Hull > 0; i++)
        {
            var part = enemy.Parts[i];
            if (!part.Disabled)
            {
                Act(run, combat, i, Next(def.AllParts[i].Pattern, part.PatternIndex++));
            }
        }
    }

    private void Act(RunState run, CombatState combat, int? part, Intent intent)
    {
        switch (intent.Kind)
        {
            case IntentKind.Attack:
                DamageShip(run, combat, intent.Amount, heavyLane: null, part);
                break;
            case IntentKind.HeavyAttack:
                int lane = run.Ship.Grid.LaneHit(intent.Lane!.Value);
                if (DamageShip(run, combat, intent.Amount, lane, part) > 0)
                {
                    DisableFrontmost(run, combat, lane);
                }

                break;
            case IntentKind.Block:
                combat.Enemy.Block += intent.Amount;
                Emit(new EnemyBlocked(intent.Amount, combat.Enemy.Block, part));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "Unknown intent.");
        }
    }

    /// <summary>Block absorbs damage first; the rest reduces hull points. Returns the hull damage.</summary>
    private int DamageShip(RunState run, CombatState combat, int amount, int? heavyLane, int? part)
    {
        int absorbed = Math.Min(combat.Block, amount);
        combat.Block -= absorbed;
        int hullDamage = amount - absorbed;
        run.Hull = Math.Max(0, run.Hull - hullDamage);
        Emit(new ShipHit(amount, absorbed, hullDamage, run.Hull, heavyLane, part));
        return hullDamage;
    }

    /// <summary>
    /// The enemy's block absorbs damage first; the rest reduces the hull of the target, the body or a part. A part
    /// whose hull reaches 0 is disabled and the aim goes back to the body. Damage past a part's hull is lost.
    /// </summary>
    private void DamageEnemy(CombatState combat, int amount)
    {
        var enemy = combat.Enemy;
        int absorbed = Math.Min(enemy.Block, amount);
        enemy.Block -= absorbed;
        int hullDamage = amount - absorbed;
        if (combat.Target is not int index)
        {
            enemy.Hull = Math.Max(0, enemy.Hull - hullDamage);
            Emit(new EnemyHit(amount, absorbed, hullDamage, enemy.Hull));
            return;
        }

        var part = enemy.Parts[index];
        part.Hull = Math.Max(0, part.Hull - hullDamage);
        Emit(new EnemyHit(amount, absorbed, hullDamage, part.Hull, index));
        if (part.Disabled)
        {
            combat.Target = null;
            Emit(new PartDisabled(index));
        }
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

        Emit(new PieceDisabled(pieceId, lane));
    }

    /// <summary>The piece works again and its cards, with their growth, go to the discard pile.</summary>
    private void Repair(CombatState combat, int pieceId)
    {
        combat.DisabledPieceIds.Remove(pieceId);
        combat.DiscardPile.AddRange(combat.OfflineCards.Where(c => c.SourcePieceId == pieceId));
        combat.OfflineCards.RemoveAll(c => c.SourcePieceId == pieceId);
        Emit(new PieceRepaired(pieceId));
    }

    private void Draw(RunState run, CombatState combat, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (combat.DrawPile.Count == 0)
            {
                if (combat.DiscardPile.Count == 0)
                {
                    return;
                }

                Emit(new DiscardShuffled(combat.DiscardPile.Count));
                combat.DrawPile.AddRange(combat.DiscardPile);
                combat.DiscardPile.Clear();
                run.Rng.Shuffle(combat.DrawPile);
            }

            var top = combat.DrawPile[^1];
            combat.DrawPile.RemoveAt(combat.DrawPile.Count - 1);
            combat.Hand.Add(top);
            Emit(new CardDrawn(top.Id));
        }
    }

    /// <summary>
    /// Records the result on the run. Disabled pieces come back at no cost: the next fight rebuilds the
    /// deck from the ship. Beating the boss of the last sector wins the run; any other win pays salvage
    /// and offers a reward. All of it happens in the same action, so it is saved together.
    /// </summary>
    private CombatOutcome EndFight(RunState run, CombatState combat, CombatOutcome outcome)
    {
        run.Combat = null;
        run.Encounter = null;
        run.LastFight = new FightRecord(combat.Enemy.DefId, outcome, combat.Turn);
        if (outcome == CombatOutcome.Lost)
        {
            run.Result = RunResult.Lost;
        }
        else
        {
            run.FightsWon++;
            var pool = content.Enemy(combat.Enemy.DefId).Pool ?? EnemyPool.Early;
            if (pool == EnemyPool.Boss && run.Sector >= run.Sectors)
            {
                run.Result = RunResult.Won;
            }
            else
            {
                var offer = _rewards.Roll(run, pool);
                run.Salvage += offer.Salvage;
                run.Reward = offer;
            }
        }

        Emit(new FightEnded(outcome));
        return outcome;
    }

    private void Emit(CombatEvent e) => emit?.Invoke(e);

    private static CombatState RequireCombat(RunState run) =>
        run.Combat ?? throw new InvalidOperationException("No fight is in progress.");
}
