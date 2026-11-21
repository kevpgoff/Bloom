using Bloom.Core.Content;
using Bloom.Core.Pieces;

namespace Bloom.Core.Run;

/// <summary>The pieces offered after a won fight, as piece def ids.</summary>
public sealed record RewardOffer(IReadOnlyList<string> Pieces);

/// <summary>
/// The reward after a won fight: a choice of pieces, of which the player takes one or none.
/// </summary>
/// <remarks>
/// Rulings by the owner on 2026-11-21: each offered piece rolls a rarity from
/// <see cref="Rules.RewardRarityOdds"/>, then a piece of that rarity. A piece that shares a tag with a
/// fitted piece is <see cref="Rules.RewardTagWeight"/> times as likely; crew count the tag their bonus
/// targets. A piece is offered at most once per offer, and the bridge never. A taken piece goes to the
/// cargo hold, even past its capacity.
/// </remarks>
public sealed class RewardEngine(ContentDb content)
{
    /// <summary>
    /// Rolls an offer from the run's random numbers, or returns null when there is nothing to offer.
    /// Pieces are considered in content order, so the same run state always gives the same offer.
    /// </summary>
    public RewardOffer? Roll(RunState run)
    {
        var shipTags = run.Ship.Fitted
            .SelectMany(f => content.Piece(f.Piece.DefId).Tags)
            .ToHashSet();
        int Weight(PieceDef def) => Affinity(def).Any(shipTags.Contains) ? Rules.RewardTagWeight : 1;

        var remaining = content.Pieces.Where(p => !p.IsBridge).ToList();
        var offered = new List<string>();
        while (offered.Count < Rules.RewardChoices && remaining.Count > 0)
        {
            var rarity = RollRarity(run);
            var candidates = remaining.Where(p => p.Rarity == rarity).ToList();
            if (candidates.Count == 0)
            {
                candidates = remaining;
            }

            var pick = PickWeighted(run, candidates, Weight);
            offered.Add(pick.Id);
            remaining.Remove(pick);
        }

        return offered.Count == 0 ? null : new RewardOffer(offered);
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
        return piece;
    }

    public void Skip(RunState run)
    {
        RequireOffer(run);
        run.Reward = null;
        run.UndoStack.Clear();
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
