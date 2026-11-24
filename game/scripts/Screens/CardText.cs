using Bloom.Core.Cards;
using Bloom.Core.Content;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Words for cards, pieces, passives and bonuses. The numbers come from Bloom.Core; this only writes them out.
/// Text that uses colors is BBCode for a <see cref="RichTextLabel"/>. Every screen that describes a piece
/// (fitting, combat, rewards, docks, groves and the hangar) builds its text from <see cref="PieceBody"/>, so a
/// piece reads the same everywhere.
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

    /// <summary>
    /// "Lance+ (1 energy): Deal 12 damage." for an upgraded card, with what the upgrade changed from
    /// <paramref name="before"/> in green.
    /// </summary>
    public static string UpgradedCard(CardDef before, CardDef after)
    {
        string Green(string text) => $"[color=#{Palette.Boosted.ToHtml(false)}]{text}[/color]";
        string cost = $"{after.Cost} energy";
        var effects = after.Effects.Select((e, i) => i < before.Effects.Count && before.Effects[i] == e ? Describe(e) : Green(Describe(e)));
        return $"{after.Name} ({(after.Cost == before.Cost ? cost : Green(cost))}): {string.Join(". ", effects)}.";
    }

    /// <summary>
    /// The cards a piece adds, one line each: "2× Lance (2 energy): Deal 12 damage." For an upgraded piece the
    /// cards are upgraded, with what changed in green. Empty for a piece that adds no cards.
    /// </summary>
    public static string Cards(PieceDef piece, ContentDb content, bool upgraded = false) =>
        string.Join("\n", piece.Cards.Select(g => upgraded
            ? $"{g.Count}× {UpgradedCard(content.Card(g.Card), content.Card(g.Card, upgraded: true))}"
            : $"{g.Count}× {Card(content.Card(g.Card))}"));

    /// <summary>A piece's name, with "+" after it once it is upgraded.</summary>
    public static string PieceName(PieceDef piece, bool upgraded) => upgraded ? piece.Name + "+" : piece.Name;

    public static string PieceName(PieceDef piece, PieceInstance instance) => PieceName(piece, instance.Upgraded);

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

    /// <summary>
    /// A passive as one line: "Passive · Each turn: gain 3 block." For an upgraded piece it is the upgraded
    /// passive, with the number that changed in green.
    /// </summary>
    public static string Passive(PassiveDef passive, bool upgraded = false)
    {
        var effect = passive.EffectFor(upgraded);
        bool changed = upgraded && effect != passive.Effect;
        string N(int value) => changed ? $"[color=#{Palette.Boosted.ToHtml(false)}]{value}[/color]" : value.ToString();
        string what = effect switch
        {
            BlockEffect b => $"gain {N(b.Amount)} block",
            GainEnergyEffect e => $"gain {N(e.Amount)} energy",
            DrawEffect { Count: 1 } => $"draw {N(1)} card",
            DrawEffect d => $"draw {N(d.Count)} cards",
            FocusEffect f => $"the next attack deals {N(f.Amount)} more damage",
            DamageEffect d => $"deal {N(d.Amount)} damage",
            LaneArmorEffect a => $"a heavy attack that reaches this piece deals {N(a.Amount)} less damage",
            RegainHullEffect r => $"the hull regains {N(r.Amount)}",
            _ => effect.GetType().Name,
        };
        string when = passive.Trigger switch
        {
            PassiveTrigger.FightStart => "Fight start",
            PassiveTrigger.TurnStart => "Each turn",
            PassiveTrigger.Standing => "Armor",
            PassiveTrigger.FightWon => "Fight won",
            _ => passive.Trigger.ToString() ?? "",
        };
        return $"[color=#{Palette.Label(Palette.Passive).ToHtml(false)}]Passive[/color] · {when}: {what}.";
    }

    /// <summary>
    /// What a piece does, one line for each thing: the cards it adds, its passive and its bonus, in that order.
    /// An upgraded piece shows its upgraded cards and passive. <paramref name="withBonus"/> leaves the bonus
    /// out, for a place that shows only what an upgrade changes.
    /// </summary>
    public static string PieceBody(PieceDef piece, ContentDb content, bool upgraded = false, bool withBonus = true)
    {
        var lines = new List<string>();
        if (piece.Cards.Count > 0)
        {
            lines.Add(Cards(piece, content, upgraded));
        }

        if (piece.Passive is { } passive)
        {
            lines.Add(Passive(passive, upgraded));
        }

        if (withBonus && piece.Bonus is { } bonus)
        {
            lines.Add(Bonus(bonus));
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// A line, with a line break in front, for a fitted piece whose passive does not count: the ship counts a
    /// passive of a given name once, and another working piece already gives it. Empty for any other piece.
    /// </summary>
    public static string PassiveNotCounted(RunSession session, int pieceId, IReadOnlyCollection<int> disabledPieceIds)
    {
        var ship = session.State.Ship;
        if (ship.Find(pieceId) is not { } fitted
            || disabledPieceIds.Contains(pieceId)
            || session.Content.Piece(fitted.Piece.DefId).Passive is not { } passive)
        {
            return "";
        }

        var counting = session.Fitter.ActivePassives(ship, disabledPieceIds).FirstOrDefault(p => p.Passive.Name == passive.Name);
        if (counting is null || counting.Fitted.Piece.Id == pieceId)
        {
            return "";
        }

        string name = PieceName(session.Content.Piece(counting.Fitted.Piece.DefId), counting.Fitted.Piece);
        string other = counting.Fitted.Piece.DefId == fitted.Piece.DefId ? $"another {name}" : $"the {name}";
        return $"\n[color=#{Palette.Heavy.ToHtml(false)}]Not counted: {other} already gives this passive.[/color]";
    }

    /// <summary>Name, kind and <see cref="PieceBody"/> of a piece, for an info panel.</summary>
    public static string PieceDetails(PieceDef piece, ContentDb content, bool upgraded = false) =>
        $"[b]{PieceName(piece, upgraded)}[/b]   [color=#{Palette.Muted.ToHtml(false)}]{Kind(piece)}[/color]\n{PieceBody(piece, content, upgraded)}";
}
