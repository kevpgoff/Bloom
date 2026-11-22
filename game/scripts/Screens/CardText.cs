using Bloom.Core.Cards;
using Bloom.Core.Content;
using Bloom.Core.Pieces;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Words for cards, pieces and bonuses. The numbers come from Bloom.Core; this only writes them out.
/// Text that uses colors is BBCode for a <see cref="RichTextLabel"/>.
/// </summary>
internal static class CardText
{
    /// <summary>One effect. Numbers raised above <paramref name="baseEffect"/> (by bonuses, focus or growth) are shown in green.</summary>
    public static string Describe(Effect effect, Effect? baseEffect = null)
    {
        string Number(int value, int? baseValue) =>
            value > (baseValue ?? value) ? $"[color=#{Palette.Boosted.ToHtml(false)}]{value}[/color]" : value.ToString();

        return effect switch
        {
            DamageEffect d => $"Deal {Number(d.Amount, (baseEffect as DamageEffect)?.Amount)} damage",
            BlockEffect b => $"Gain {Number(b.Amount, (baseEffect as BlockEffect)?.Amount)} block",
            GainEnergyEffect e => $"Gain {e.Amount} energy",
            DrawEffect d => d.Count == 1 ? "Draw 1 card" : $"Draw {d.Count} cards",
            RepairEffect r => r.Count == 1 ? "Repair 1 disabled piece" : $"Repair {r.Count} disabled pieces",
            FocusEffect f => $"Next attack deals {f.Amount} more damage",
            GrowEffect g => $"Then grows +{g.Amount} block",
            _ => effect.GetType().Name,
        };
    }

    /// <summary>Full-word label paired underneath a large card effect symbol.</summary>
    public static string EffectWord(Effect effect) => effect switch
    {
        DamageEffect => "Damage",
        BlockEffect => "Block",
        GainEnergyEffect => "Energy",
        DrawEffect => "Draw",
        RepairEffect => "Repair",
        FocusEffect => "Focus",
        GrowEffect => "Grow",
        _ => effect.GetType().Name,
    };

    /// <summary>Value displayed inside a large card effect symbol, and whether bonuses/focus/growth boosted it.</summary>
    public static (string Value, bool Boosted) EffectNumber(Effect effect, Effect? baseEffect = null) => effect switch
    {
        DamageEffect d => (d.Amount.ToString(), d.Amount > ((baseEffect as DamageEffect)?.Amount ?? d.Amount)),
        BlockEffect b => (b.Amount.ToString(), b.Amount > ((baseEffect as BlockEffect)?.Amount ?? b.Amount)),
        GainEnergyEffect e => ($"+{e.Amount}", false),
        DrawEffect d => ($"+{d.Count}", false),
        RepairEffect r => (r.Count.ToString(), false),
        FocusEffect f => ($"+{f.Amount}", false),
        GrowEffect g => ($"+{g.Amount}", false),
        _ => ("?", false),
    };

    /// <summary>"Lance (2 energy): Deal 12 damage."</summary>
    public static string Card(CardDef card) =>
        $"{card.Name} ({card.Cost} energy): {string.Join(". ", card.Effects.Select(e => Describe(e)))}.";

    /// <summary>The cards a piece adds, one line each: "2× Lance (2 energy): Deal 12 damage."</summary>
    public static string Cards(PieceDef piece, ContentDb content) =>
        string.Join("\n", piece.Cards.Select(g => $"{g.Count}× {Card(content.Card(g.Card))}"));

    /// <summary>"Equipment · Weapon · Common", or "Crew · Rare".</summary>
    public static string Kind(PieceDef piece) =>
        string.Join(" · ", new[] { piece.Kind.ToString() }.Concat(piece.Tags.Select(t => t.ToString())).Append(piece.Rarity.ToString()));

    /// <summary>"Adjacent Weapon pieces: their cards deal 2 more damage."</summary>
    public static string Bonus(AdjacencyBonus bonus) => bonus.Effect switch
    {
        DamageBonusEffect d => $"Adjacent {bonus.TargetTag} pieces: their cards deal {d.Amount} more damage.",
        StartGrownEffect { Times: 1 } => $"Adjacent {bonus.TargetTag} pieces: their cards start each fight grown once.",
        StartGrownEffect g => $"Adjacent {bonus.TargetTag} pieces: their cards start each fight grown {g.Times} times.",
        _ => $"Adjacent {bonus.TargetTag} pieces get a bonus.",
    };

    /// <summary>A bonus between two pieces, short: "+2 damage" or "starts grown".</summary>
    public static string BonusShort(BonusEffect effect) => effect switch
    {
        DamageBonusEffect d => $"+{d.Amount} damage",
        StartGrownEffect { Times: 1 } => "starts grown",
        StartGrownEffect g => $"starts grown ×{g.Times}",
        _ => "bonus",
    };

    /// <summary>Name, kind, cards and bonus of a piece, for an info panel.</summary>
    public static string PieceDetails(PieceDef piece, ContentDb content)
    {
        string text = $"[b]{piece.Name}[/b]   [color=#{Palette.Muted.ToHtml(false)}]{Kind(piece)}[/color]\n{Cards(piece, content)}";
        return piece.Bonus is { } bonus ? $"{text}\n{Bonus(bonus)}" : text;
    }
}
