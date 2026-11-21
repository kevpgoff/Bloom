using Bloom.Core.Cards;
using Bloom.Core.Content;
using Bloom.Core.Pieces;
using Bloom.Core.Run;

namespace Bloom.Core.Combat;

/// <summary>
/// The turn cycle of a fight. Each public method is one player action and leaves the run in a state
/// that can be saved.
/// </summary>
/// <remarks>
/// Implemented: energy, draw and reshuffle, damage, block, gain energy, draw effects, enemy intents,
/// lanes and disabling. Not yet: repair, focus, grow, and applying adjacency bonuses in combat; cards
/// with those effects throw <see cref="NotSupportedException"/> when played.
/// </remarks>
public sealed class CombatEngine(ContentDb content)
{
    private readonly ShipFitter _fitter = new(content);

    public CombatState StartFight(RunState run, string enemyId)
    {
        if (run.Combat is not null)
        {
            throw new InvalidOperationException("A fight is already in progress.");
        }

        var enemy = content.Enemy(enemyId);
        var combat = new CombatState
        {
            Enemy = new EnemyState { DefId = enemy.Id, Hull = enemy.Hull, MaxHull = enemy.Hull },
        };
        combat.DrawPile.AddRange(DeckBuilder.Build(run.Ship, content));
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

    public bool CanPlay(RunState run, int cardId) =>
        run.Combat is { } combat
        && combat.Hand.Find(c => c.Id == cardId) is { } card
        && content.Card(card.CardId).Cost <= combat.Energy
        && content.Card(card.CardId).Effects.All(IsSupported);

    public CombatOutcome PlayCard(RunState run, int cardId)
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

        // Check every effect before changing anything, so a failed play leaves the state untouched.
        if (def.Effects.FirstOrDefault(e => !IsSupported(e)) is { } unsupported)
        {
            throw new NotSupportedException($"Effect '{unsupported.GetType().Name}' on card '{def.Id}' is not implemented yet.");
        }

        combat.Energy -= def.Cost;
        combat.Hand.RemoveAt(index);
        foreach (var effect in def.Effects)
        {
            Apply(run, combat, effect);
        }

        combat.DiscardPile.Add(card);
        return combat.Enemy.Hull <= 0 ? EndFight(run, won: true) : CombatOutcome.Ongoing;
    }

    /// <summary>The enemy acts, unplayed cards go to the discard pile, block returns to 0, and the next turn starts.</summary>
    public CombatOutcome EndTurn(RunState run)
    {
        var combat = RequireCombat(run);
        EnemyActs(run, combat);
        if (run.Hull <= 0)
        {
            return EndFight(run, won: false);
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

    private static bool IsSupported(Effect effect) =>
        effect is DamageEffect or BlockEffect or GainEnergyEffect or DrawEffect;

    private void StartTurn(RunState run, CombatState combat)
    {
        combat.Turn++;
        combat.Energy = Rules.EnergyPerTurn;
        Draw(run, combat, CardsToDraw(run, combat));
    }

    private void Apply(RunState run, CombatState combat, Effect effect)
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
            default:
                throw new NotSupportedException($"Effect '{effect.GetType().Name}' is not implemented yet.");
        }
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

    /// <summary>Disabled pieces come back at no cost: the next fight rebuilds the deck from the ship.</summary>
    private static CombatOutcome EndFight(RunState run, bool won)
    {
        run.Combat = null;
        if (!won)
        {
            run.Result = RunResult.Lost;
        }

        return won ? CombatOutcome.Won : CombatOutcome.Lost;
    }

    private static CombatState RequireCombat(RunState run) =>
        run.Combat ?? throw new InvalidOperationException("No fight is in progress.");
}
