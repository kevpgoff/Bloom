using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Pieces;

namespace Bloom.Core.Run;

/// <summary>
/// The pieces offered after a won fight, as piece def ids, and the salvage the fight paid. The salvage is
/// already added to the run; it is kept here only to show it.
/// </summary>
public sealed record RewardOffer(IReadOnlyList<string> Pieces, int Salvage = 0);

/// <summary>
/// The reward after a won fight: a choice of pieces, of which the player takes one or none.
/// </summary>
/// <remarks>
/// Rulings by the owner on 2026-11-21: each offered piece rolls a rarity from
/// <see cref="Rules.RewardRarityOdds"/>, then a piece of that rarity. A piece that shares a tag with a
/// fitted piece is <see cref="Rules.RewardTagWeight"/> times as likely; crew count the tag their bonus
/// targets. A piece is offered at most once per offer, and the bridge never. A taken piece goes to the
/// cargo hold, even past its capacity. Defaults taken on 2026-11-22: a fight also pays salvage
/// (<see cref="Rules.SkirmishSalvage"/> and the like), and after a hard fight or a boss the first offered
/// piece is always rare, as the spec's node table says.
/// </remarks>
public sealed class RewardEngine(ContentDb content)
{
    /// <summary>
    /// Rolls an offer and its salvage from the run's random numbers, for a fight against an enemy of
    /// <paramref name="pool"/>. Pieces are considered in content order, so the same run state always gives
    /// the same offer. The offer can be empty if no piece is left to offer.
    /// </summary>
    public RewardOffer Roll(RunState run, EnemyPool pool = EnemyPool.Early)
    {
        var shipTags = run.Ship.Fitted
            .SelectMany(f => content.Piece(f.Piece.DefId).Tags)
            .ToHashSet();
        int Weight(PieceDef def) => Affinity(def).Any(shipTags.Contains) ? Rules.RewardTagWeight : 1;

        var (min, max) = pool switch
        {
            EnemyPool.Hard => Rules.HardFightSalvage,
            EnemyPool.Boss => Rules.BossSalvage,
            _ => Rules.SkirmishSalvage,
        };
        int salvage = run.Rng.Next(min, max + 1);
        bool rareFirst = pool is EnemyPool.Hard or EnemyPool.Boss;

        var remaining = content.Pieces.Where(p => !p.IsBridge).ToList();
        var offered = new List<string>();
        while (offered.Count < Rules.RewardChoices && remaining.Count > 0)
        {
            var rarity = rareFirst && offered.Count == 0 ? Rarity.Rare : RollRarity(run);
            var candidates = remaining.Where(p => p.Rarity == rarity).ToList();
            if (candidates.Count == 0)
            {
                candidates = remaining;
            }

            var pick = PickWeighted(run, candidates, Weight);
            offered.Add(pick.Id);
            remaining.Remove(pick);
        }

        return new RewardOffer(offered, salvage);
    }

    /// <summary>Takes the offered piece at <paramref name="index"/> into the cargo hold.</summary>
    public PieceInstance Take(RunState run, int index)
    {
        var offer = RequireOffer(run);
        if (index < 0 || index >= offer.Pieces.Count)
        {
            throw new InvalidOperationException($"There is no offered piece {index}; the offer has {offer.Pieces.Count}.");
        }

        var piece = run.NewPiece(offer.Pieces[index]);
        run.Cargo.Add(piece);
        run.Reward = null;
        run.UndoStack.Clear();
        MapEngine.FinishNode(run);
        return piece;
    }

    /// <summary>Takes no piece. The salvage was already paid when the fight was won.</summary>
    public void Skip(RunState run)
    {
        RequireOffer(run);
        run.Reward = null;
        run.UndoStack.Clear();
        MapEngine.FinishNode(run);
    }

    /// <summary>The tags a piece is favored for: its own, and for a piece with a bonus, the tag the bonus targets.</summary>
    private static IEnumerable<Tag> Affinity(PieceDef def) =>
        def.Bonus is { } bonus ? def.Tags.Append(bonus.TargetTag) : def.Tags;

    private static Rarity RollRarity(RunState run)
    {
        int total = Rules.RewardRarityOdds.Sum(o => o.Percent);
        int roll = run.Rng.Next(total);
        foreach (var (rarity, percent) in Rules.RewardRarityOdds)
        {
            roll -= percent;
            if (roll < 0)
            {
                return rarity;
            }
        }

        return Rules.RewardRarityOdds[^1].Rarity;
    }

    private static PieceDef PickWeighted(RunState run, IReadOnlyList<PieceDef> candidates, Func<PieceDef, int> weight)
    {
        int roll = run.Rng.Next(candidates.Sum(weight));
        foreach (var candidate in candidates)
        {
            roll -= weight(candidate);
            if (roll < 0)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    private static RewardOffer RequireOffer(RunState run) =>
        run.Phase == RunPhase.Reward && run.Reward is { } offer
            ? offer
            : throw new InvalidOperationException("No reward is waiting to be taken.");
}
