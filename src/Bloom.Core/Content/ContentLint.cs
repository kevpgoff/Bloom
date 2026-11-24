using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;

namespace Bloom.Core.Content;

/// <summary>
/// Checks that valid content also makes sense as a set: mistakes that load and play without an error but
/// leave something dead or missing, such as a card that no piece adds. <see cref="ContentValidator"/> refuses
/// content that the rules cannot run; this reports content that the rules run but that is very likely a mistake.
/// It is separate because small test content breaks these checks on purpose. A test runs it on the shipped data.
/// </summary>
public static class ContentLint
{
    /// <summary>Every finding, in a fixed order. Empty when the content is clean.</summary>
    public static IReadOnlyList<string> Check(ContentDb db)
    {
        var findings = new List<string>();
        var offered = db.Pieces.Where(p => !p.IsBridge).ToList();
        CheckCards(db, findings);
        CheckBonuses(db, findings);
        CheckPassives(db, findings);
        CheckRarities(offered, findings);
        CheckFit(db, offered, findings);
        CheckEnemies(db, findings);
        CheckEvents(db, offered, findings);
        CheckNames(db.Pieces.Select(p => p.Name), "pieces", findings);
        CheckNames(db.Cards.Select(c => c.Name), "cards", findings);
        CheckNames(db.Enemies.Select(e => e.Name), "enemies", findings);
        return findings;
    }

    private static void CheckCards(ContentDb db, List<string> findings)
    {
        var granted = db.Pieces.SelectMany(p => p.Cards).Select(g => g.Card).ToHashSet();
        foreach (var card in db.Cards)
        {
            if (!granted.Contains(card.Id))
            {
                findings.Add($"card '{card.Id}': no piece adds it.");
            }

            CheckEffects($"card '{card.Id}'", card.Effects, findings);
            if (card.Upgrade?.Effects is { } upgraded)
            {
                CheckEffects($"card '{card.Id}' (upgraded)", upgraded, findings);
            }
        }
    }

    private static void CheckEffects(string where, IReadOnlyList<Effect> effects, List<string> findings)
    {
        foreach (var effect in effects)
        {
            if (Amount(effect) == 0)
            {
                findings.Add($"{where}: '{effect.GetType().Name}' has an amount of 0, so it does nothing.");
            }
        }

        // Growth is added to block and to nothing else (CombatEngine.ResolveEffects).
        if (effects.Any(e => e is GrowEffect) && !effects.Any(e => e is BlockEffect))
        {
            findings.Add($"{where}: it grows but has no block, and growth raises only block.");
        }
    }

    private static void CheckBonuses(ContentDb db, List<string> findings)
    {
        // A piece without cards receives no bonus (ShipFitter.ActiveBonuses), so its tags do not count here.
        var carried = db.Pieces.Where(p => p.Cards.Count > 0).SelectMany(p => p.Tags).ToHashSet();
        var byName = new Dictionary<string, AdjacencyBonus>();
        foreach (var piece in db.Pieces)
        {
            if (piece.Bonus is not { } bonus)
            {
                continue;
            }

            string where = $"piece '{piece.Id}'";
            if (Amount(bonus.Effect) <= 0)
            {
                findings.Add($"{where}: its bonus '{bonus.Name}' has an amount of {Amount(bonus.Effect)}, so it gives nothing.");
            }

            if (!carried.Contains(bonus.TargetTag))
            {
                findings.Add($"{where}: its bonus targets {bonus.TargetTag}, a tag that no piece with cards carries.");
            }

            // A piece receives a bonus of a given name once, so two bonuses that share a name must be the same bonus.
            if (byName.TryGetValue(bonus.Name, out var first) && first != bonus)
            {
                findings.Add($"{where}: its bonus '{bonus.Name}' shares its name with a different bonus, and a piece receives a name only once.");
            }

            byName.TryAdd(bonus.Name, bonus);
        }
    }

    private static void CheckPassives(ContentDb db, List<string> findings)
    {
        var byName = new Dictionary<string, PassiveDef>();
        foreach (var piece in db.Pieces)
        {
            if (piece.Passive is not { } passive)
            {
                continue;
            }

            string where = $"piece '{piece.Id}'";
            if (Amount(passive.Effect) == 0)
            {
                findings.Add($"{where}: its passive '{passive.Name}' has an amount of 0, so it does nothing.");
            }

            if (passive.Upgrade is { } upgrade && Amount(upgrade) <= Amount(passive.Effect))
            {
                findings.Add($"{where}: the upgrade of its passive '{passive.Name}' is not larger than the passive, so upgrading it gains nothing.");
            }

            // A ship counts a passive of a given name once, so two passives that share a name must be the same passive.
            if (byName.TryGetValue(passive.Name, out var first) && first != passive)
            {
                findings.Add($"{where}: its passive '{passive.Name}' shares its name with a different passive, and a ship counts a name only once.");
            }

            byName.TryAdd(passive.Name, passive);
        }
    }

    /// <summary>
    /// An offer rolls a rarity for each slot, so a rarity with fewer pieces than an offer has slots repeats in every
    /// offer. An offer holds at most one piece without cards, so those count as one.
    /// </summary>
    private static void CheckRarities(List<PieceDef> offered, List<string> findings)
    {
        foreach (var rarity in Enum.GetValues<Rarity>())
        {
            var ofRarity = offered.Where(p => p.Rarity == rarity).ToList();
            int count = ofRarity.Count(p => p.Cards.Count > 0) + Math.Min(1, ofRarity.Count(p => p.Cards.Count == 0));
            if (count < Rules.RewardChoices)
            {
                findings.Add($"Only {count} {rarity} pieces can be offered, but a reward offers {Rules.RewardChoices}.");
            }
        }
    }

    private static void CheckFit(ContentDb db, List<PieceDef> offered, List<string> findings)
    {
        var hulls = db.Ships.Select(s => s.HullShape.CellsAt(s.HullOrigin, Rotation.R0).ToHashSet()).ToList();
        foreach (var piece in offered)
        {
            if (!hulls.Any(hull => FitsOn(hull, piece.Shape)))
            {
                findings.Add($"piece '{piece.Id}': it fits on no ship's starting hull, even an empty one.");
            }
        }
    }

    private static bool FitsOn(HashSet<Cell> hull, Shape shape)
    {
        foreach (var rotation in Enum.GetValues<Rotation>())
        {
            var rotated = shape.Rotated(rotation);
            for (int y = 0; y <= Rules.GridRows - rotated.Height; y++)
            {
                for (int x = 0; x <= Rules.GridColumns - rotated.Width; x++)
                {
                    if (shape.CellsAt(new Cell(x, y), rotation).All(hull.Contains))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void CheckEnemies(ContentDb db, List<string> findings)
    {
        foreach (var enemy in db.Enemies)
        {
            if (enemy.AllIntents.Any(i => i.Amount == 0))
            {
                findings.Add($"enemy '{enemy.Id}': an intent has an amount of 0, so it does nothing.");
            }
        }
    }

    private static void CheckEvents(ContentDb db, List<PieceDef> offered, List<string> findings)
    {
        // The bridge is always fitted, so a choice that needs one of its tags is open on every ship.
        var onTheBridge = db.Pieces.Where(p => p.IsBridge).SelectMany(p => p.Tags).ToHashSet();
        var carried = offered.SelectMany(p => p.Tags).ToHashSet();
        foreach (var ev in db.Events)
        {
            foreach (var tag in ev.Choices.Select(c => c.NeedsTag).OfType<Tag>().Distinct().Order())
            {
                if (onTheBridge.Contains(tag))
                {
                    findings.Add($"event '{ev.Id}': a choice needs {tag}, which the bridge always carries, so the choice is always open.");
                }
                else if (!carried.Contains(tag))
                {
                    findings.Add($"event '{ev.Id}': a choice needs {tag}, a tag that no piece carries, so the choice can never open.");
                }
            }
        }
    }

    /// <summary>The player tells entries apart by name, so two of a kind must not share one.</summary>
    private static void CheckNames(IEnumerable<string> names, string kind, List<string> findings)
    {
        var seen = new HashSet<string>();
        var reported = new HashSet<string>();
        foreach (string name in names)
        {
            if (!seen.Add(name) && reported.Add(name))
            {
                findings.Add($"Two {kind} are named '{name}'.");
            }
        }
    }

    private static int Amount(Effect effect) => effect switch
    {
        DamageEffect e => e.Amount,
        BlockEffect e => e.Amount,
        GainEnergyEffect e => e.Amount,
        DrawEffect e => e.Count,
        RepairEffect e => e.Count,
        FocusEffect e => e.Amount,
        GrowEffect e => e.Amount,
        DamageBonusEffect e => e.Amount,
        StartGrownEffect e => e.Times,
        LaneArmorEffect e => e.Amount,
        RegainHullEffect e => e.Amount,
        _ => throw new InvalidOperationException($"The lint has no amount for '{effect.GetType().Name}'."),
    };
}
