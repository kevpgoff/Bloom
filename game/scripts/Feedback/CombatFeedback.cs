using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Game.Audio;
using Bloom.Game.Screens;
using Godot;

namespace Bloom.Game.Feedback;

/// <summary>
/// Turns the <see cref="CombatEvent"/>s of one action into a timeline of effects, shake, haptics and sound on the
/// combat screen's <see cref="FxLayer"/>. The screen has already redrawn to the new state; the timeline only
/// shows how it got there: a card lights its piece, a bolt flies to the enemy, the enemy hits back, and the new
/// hand deals in after the hit. Nothing here blocks input or changes the run.
/// </summary>
/// <remarks>
/// Rects of things about to be freed (the card buttons) are copied when <see cref="Play"/> runs. Everything else
/// (pieces, labels, the new cards) is looked up when its effect fires, after the screen has redrawn.
/// </remarks>
internal sealed class CombatFeedback(FxLayer fx, CombatFeedback.Widgets w, Func<int, Rect2?> cardRect, Func<IReadOnlyList<Rect2>> handRects)
{
    /// <summary>The widgets effects start from and land on.</summary>
    public sealed record Widgets(
        EnemyEmblem Emblem,
        ShipGridView Grid,
        ProgressBar EnemyHullBar,
        Label ShipLabel,
        Label BlockLabel,
        Label EnergyLabel,
        Label PilesLabel,
        Label TurnLabel,
        Control EndTurn,
        Func<int, PartPanel?> Part);

    /// <summary>
    /// What the screen does for this action: when each new card fades in, when the result overlay may show, and
    /// how long the screen holds before leaving for the next one. Times are seconds from now.
    /// </summary>
    public sealed record Cue(IReadOnlyList<(int CardId, float At)> DealIns, float? ResultAt, float? ExitHold);

    /// <summary>Every timing in one place. All are placeholders, to tune by feel.</summary>
    public static class Tuning
    {
        public const float CardPop = 0.18f;
        public const float StreakDelay = 0.03f;
        public const float StreakTravel = 0.08f;
        public const float BoltTravel = 0.11f;
        public const float EffectGap = 0.07f;
        public const float StreamTime = 0.4f;
        public const float RepairBeam = 0.14f;
        public const float EnemyWindup = 0.06f;
        public const float EnemyBoltTravel = 0.10f;
        public const float DealAfterImpact = 0.40f;
        public const float OpeningDeal = 0.2f;
        public const float DealStagger = 0.06f;
        public const float DealFlight = 0.2f;
        public const float DealFade = 0.18f;
        public const float ShuffleTime = 0.3f;
        public const float DiscardFlight = 0.3f;
        public const float WinHold = 1.0f;
        public const float AfterShatter = 0.7f;
        public const float ResultAfterImpact = 0.9f;

        /// <summary>The draw sound is a swish that ends in a tick; it starts this long before the card lands, so the tick lands with it.</summary>
        public const float DrawSoundLead = 0.04f;

        /// <summary>Each card of one deal sounds this much higher than the card before it.</summary>
        public const float DrawPitchStep = 0.03f;

        /// <summary>How long after a fight's first events the screen is laid out, so that its widgets can be measured.</summary>
        public const float OpeningLayout = 0.05f;

        /// <summary>The time between a passive's pulse and what it does.</summary>
        public const float PassiveGap = 0.12f;

        /// <summary>Each passive of one action sounds this much higher than the one before it.</summary>
        public const float PassivePitchStep = 0.04f;

        /// <summary>A won fight's regained hull shows this long after the last hit, clear of the enemy's shatter.</summary>
        public const float RegainAfterImpact = 0.3f;

        /// <summary>How long the regained hull's number stays up; the screen waits for it.</summary>
        public const float RegainShown = 1.0f;
    }

    /// <summary>Plays the events of one action and returns what the screen must time to match.</summary>
    public Cue Play(RunSession session, IReadOnlyList<CombatEvent> events)
    {
        var beat = new Beat
        {
            BarValue = w.EnemyHullBar.Value,
            BarMax = Math.Max(1.0, w.EnemyHullBar.MaxValue),
            Bar = fx.FromGlobal(w.EnemyHullBar.GetGlobalRect()),
            OldHand = handRects().Select(fx.FromGlobal).ToList(),
        };

        foreach (var e in events)
        {
            switch (e)
            {
                case FightStarted:
                    At(0.05f, () => ReticleLock(null));
                    beat.T = Tuning.OpeningDeal;

                    // The screen has not drawn this enemy yet, so its hull bar still holds the scene's values and
                    // its place on screen is not laid out. A fight starts at full hull.
                    if (session.State.Combat?.Enemy is { } enemy)
                    {
                        beat.BarMax = Math.Max(1.0, enemy.MaxHull);
                        beat.BarValue = enemy.MaxHull;
                        beat.Opening = true;
                    }

                    break;
                case TurnStarted:
                    TurnStart(beat);
                    break;
                case CardDrawn drawn:
                    Drawn(beat, drawn.CardId);
                    break;
                case DiscardShuffled shuffled:
                    Shuffled(beat, shuffled.Count);
                    break;
                case HandDiscarded:
                    HandToDiscard(beat);
                    break;
                case CardPlayed played:
                    OnCardPlayed(session, beat, played);
                    break;
                case FocusSpent:
                    OnFocusSpent(beat);
                    break;
                case EnemyHit hit:
                    OnEnemyHit(session, beat, hit);
                    break;
                case BlockGained block:
                    OnBlockGained(beat, block.Amount);
                    break;
                case EnergyGained energy:
                    OnEnergyGained(beat, energy.Amount);
                    break;
                case FocusGained focus:
                    OnFocusGained(beat, focus.Amount);
                    break;
                case PieceRepaired repaired:
                    Repaired(beat, repaired.PieceId);
                    break;
                case CardGrew grew:
                    Grew(beat, grew.Growth);
                    break;
                case ShipHit hit:
                    OnShipHit(beat, hit);
                    break;
                case PieceDisabled disabled:
                    Disabled(beat, disabled.PieceId, disabled.Lane);
                    break;
                case EnemyBlocked block:
                    OnEnemyBlocked(beat, block.Amount, block.Part);
                    break;
                case PartDisabled disabled:
                    OnPartDisabled(beat, disabled.Part);
                    break;
                case TargetChanged changed:
                    At(0f, () => ReticleLock(changed.Part));
                    break;
                case PassiveFired fired:
                    OnPassiveFired(beat, fired);
                    break;
                case HullRegained regained:
                    OnHullRegained(beat, regained);
                    break;
                case FightEnded { Outcome: CombatOutcome.Won }:
                    Won(beat);
                    break;
                case FightEnded:
                    Lost(beat);
                    break;
            }
        }

        return new Cue(beat.Deals, beat.ResultAt, beat.ExitHold);
    }

    /// <summary>End Turn answers the press at once, before the enemy's hit plays.</summary>
    public void EndTurnPressed()
    {
        var r = fx.FromGlobal(w.EndTurn.GetGlobalRect());
        fx.Flash([r], Palette.HoloLine with { A = 0.14f }, 0.25f);
        fx.RectRing(r, Palette.HoloBright, 8f, 0.3f, 2f);
        Haptics.Play(HapticKind.Tick);
        Sfx.Play(SfxCue.EndTurn);
    }

    // The player's cards

    private void OnCardPlayed(RunSession session, Beat beat, CardPlayed played)
    {
        int source = played.SourcePieceId;
        var piece = session.State.Ship.Find(source) is { } fitted ? session.Content.Piece(fitted.Piece.DefId) : null;
        var color = piece is null ? Palette.HoloLine : Palette.ForPiece(piece);
        var card = cardRect(played.CardId) is { } global ? fx.FromGlobal(global) : (Rect2?)null;
        beat.Source = source;
        beat.CardCenter = card?.GetCenter();
        beat.CardDefId = played.CardDefId;
        beat.CardUpgraded = played.Upgraded;
        beat.Focused = false;

        if (card is { } rect)
        {
            fx.GhostCard(rect, color, Tuning.CardPop);
        }

        Haptics.Play(HapticKind.Tick);
        Sfx.Play(CardVoice(piece));
        var from = beat.CardCenter;
        At(Tuning.StreakDelay, () =>
        {
            if (from is { } start && PieceCenter(source) is { } piece)
            {
                fx.Bolt(start, piece, color, Tuning.StreakTravel, 2.5f, color.Lerp(Colors.White, 0.6f));
            }
        });
        At(Tuning.StreakDelay + Tuning.StreakTravel, () =>
        {
            fx.Flash(PieceCells(source), color with { A = 0.3f }, 0.3f);
            if (PieceCenter(source) is { } piece)
            {
                fx.Pulse(piece, color, CellSize() * 0.9f, 0.35f);
            }
        });
        beat.T = Tuning.StreakDelay + Tuning.StreakTravel;
    }

    private void OnFocusSpent(Beat beat)
    {
        var origin = OriginOf(beat);
        At(beat.T, () =>
        {
            fx.Implode(origin(), Palette.Focus, 12, CellSize() * 1.2f, 0.3f, 2.4f);
            Sfx.Play(SfxCue.FocusSpend);
        });
        beat.Focused = true;
        beat.T += 0.12f;
    }

    private void OnEnemyHit(RunSession session, Beat beat, EnemyHit hit)
    {
        var def = beat.CardDefId is { } id ? session.Content.Card(id, beat.CardUpgraded) : null;
        int baseDamage = def?.Effects.OfType<DamageEffect>().FirstOrDefault()?.Amount ?? hit.Amount;
        bool boosted = hit.Amount > baseDamage;
        bool focused = beat.Focused;
        var origin = OriginOf(beat);
        float launch = beat.T;
        float impact = launch + Tuning.BoltTravel;

        // Launch and impact share one origin, so the recoil points away from where the bolt came from.
        var from = Vector2.Zero;
        At(launch, () =>
        {
            from = origin();
            var to = hit.Part is int part ? PartCenter(part) : EmblemCenter();
            fx.Bolt(from, to, Palette.Attack, Tuning.BoltTravel, focused ? 7f : 4.5f, boosted ? Palette.Boosted : Colors.White, 0.06f);
            if (focused)
            {
                fx.Bolt(from, to, Palette.Focus, Tuning.BoltTravel, 12f, Palette.Focus, 0.1f);
            }
        });
        At(impact, () => EnemyImpact(hit, from));

        // The hull bar has already dropped; the lost part stays lit until the bolt lands. A part's bar is its panel's.
        if (hit.Part is null && beat.BarValue > hit.HullLeft)
        {
            float before = (float)(beat.BarValue / beat.BarMax);
            float left = (float)(hit.HullLeft / beat.BarMax);
            if (beat.Opening)
            {
                // A passive's hit as the fight starts: the bar is measured once the screen has laid it out.
                At(Tuning.OpeningLayout, () => fx.BarChunk(fx.FromGlobal(w.EnemyHullBar.GetGlobalRect()), before, left, Palette.Attack, impact - Tuning.OpeningLayout));
            }
            else
            {
                fx.BarChunk(beat.Bar, before, left, Palette.Attack, impact);
            }
        }

        if (hit.Part is null)
        {
            beat.BarValue = hit.HullLeft;
        }

        beat.LastImpact = impact;
        beat.T = impact + Tuning.EffectGap;
    }

    private void EnemyImpact(EnemyHit hit, Vector2 from)
    {
        if (hit.Part is int part)
        {
            PartImpact(hit, part, from);
            return;
        }

        var c = EmblemCenter();
        float r = EmblemRadius();
        var dir = (c - from).LengthSquared() > 1f ? (c - from).Normalized() : Vector2.Up;
        if (hit.HullDamage > 0)
        {
            int damage = hit.HullDamage;
            int count = Math.Clamp(12 + damage, 12, 40);
            fx.Burst(c, Palette.Attack, count, 260f + (damage * 8f), 0.5f, 2.6f, ParticleShape.Streak, dir.Angle(), 2.4f, drag: 4f);
            fx.Burst(c, Palette.Attack.Lerp(Colors.White, 0.5f), count / 2, 140f, 0.4f, 2.2f);
            fx.Ring(c, Palette.Attack, r * 0.3f, r * 1.05f, 0.35f);
            fx.Pulse(c, Palette.Attack, r * 1.4f, 0.3f);
            w.Emblem.Kick(dir, Mathf.Clamp(4f + (damage * 0.5f), 4f, 18f));
            w.Emblem.Flash(Colors.White, 0.2f);
            // Low on the emblem and rising only a little: the enemy sits at the top of the screen, its name beside it.
            fx.FloatText($"-{damage}", c + new Vector2(r * 0.45f, r * 0.45f), Palette.Attack.Lerp(Colors.White, 0.15f), Math.Clamp(46 + damage, 46, 76), 0.95f, 32f);
            fx.Shake(damage >= 20 ? 0.5f : damage >= 10 ? 0.35f : 0.15f);
            Haptics.Play(damage >= 20 ? HapticKind.Heavy : damage >= 10 ? HapticKind.Medium : HapticKind.Light);
            Sfx.Play(HitSound(damage));
        }

        if (hit.Blocked > 0)
        {
            fx.Ring(c, Palette.Block, r * 1.0f, r * 0.85f, 0.3f, 3f, hex: true);
            fx.Burst(c - (dir * r * 0.7f), Palette.Block, 10, 160f, 0.45f, 3f, ParticleShape.Hex, (-dir).Angle(), 2f);
            fx.FloatText($"{hit.Blocked} BLOCKED", c + new Vector2(0, r * 0.55f), Palette.Block, 24, 0.9f, 40f);
            if (hit.HullDamage == 0)
            {
                w.Emblem.Kick(dir, 3f);
                Haptics.Play(HapticKind.Light);
                Sfx.Play(SfxCue.HitBlocked);
            }
        }
    }

    /// <summary>A hit on one of a boss's parts: smaller than on the body, on its panel, which flashes.</summary>
    private void PartImpact(EnemyHit hit, int part, Vector2 from)
    {
        var c = PartCenter(part);
        var rect = PartRectOrEmblem(part);
        float r = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.5f;
        var dir = (c - from).LengthSquared() > 1f ? (c - from).Normalized() : Vector2.Up;
        if (hit.HullDamage > 0)
        {
            int damage = hit.HullDamage;
            fx.Burst(c, Palette.Attack, Math.Clamp(10 + damage, 10, 30), 220f + (damage * 6f), 0.45f, 2.4f, ParticleShape.Streak, dir.Angle(), 2.4f, drag: 4f);
            fx.Ring(c, Palette.Attack, r * 0.3f, r * 1.1f, 0.3f);
            w.Part(part)?.Flash(Colors.White);
            fx.FloatText($"-{damage}", c + new Vector2(r * 0.6f, 0f), Palette.Attack.Lerp(Colors.White, 0.15f), Math.Clamp(40 + damage, 40, 64), 0.9f, 28f);
            fx.Shake(damage >= 10 ? 0.25f : 0.12f);
            Haptics.Play(damage >= 10 ? HapticKind.Medium : HapticKind.Light);
            Sfx.Play(HitSound(damage));
        }

        if (hit.Blocked > 0)
        {
            fx.RectRing(rect, Palette.Block, 6f, 0.3f, 3f);
            fx.FloatText($"{hit.Blocked} BLOCKED", c - new Vector2(0f, r * 0.5f), Palette.Block, 22, 0.9f, 30f);
            if (hit.HullDamage == 0)
            {
                Haptics.Play(HapticKind.Light);
                Sfx.Play(SfxCue.HitBlocked);
            }
        }
    }

    /// <summary>A part goes dark: sparks fall from its panel, and the aim jumps back to the body.</summary>
    private void OnPartDisabled(Beat beat, int part)
    {
        // Just after the killing hit's own numbers, and below them.
        float at = Math.Max(beat.LastImpact, beat.T) + 0.12f;
        At(at, () =>
        {
            var rect = PartRectOrEmblem(part);
            var c = rect.GetCenter() + new Vector2(0f, rect.Size.Y * 0.2f);
            fx.Flash([rect], Palette.Refused with { A = 0.45f }, 0.5f, flicker: true);
            fx.Burst(c, Palette.Threat, 30, 300f, 0.7f, 2.4f, ParticleShape.Streak, -Mathf.Pi / 2f, 1.8f, gravity: 520f, drag: 1.5f);
            fx.Burst(c, Colors.White, 12, 160f, 0.45f, 2.2f);
            fx.FloatText("DISABLED", c, Palette.Refused, 26, 1.1f, 36f);
            fx.Shake(0.3f);
            Haptics.Play(HapticKind.Heavy);
            Sfx.Play(SfxCue.PartDisabled);
            fx.Schedule(0.09f, () => Haptics.Play(HapticKind.Medium));
        });
        At(at + 0.35f, () => ReticleLock(null));
        beat.T = at + 0.1f;
    }

    private void OnBlockGained(Beat beat, int amount)
    {
        var origin = OriginOf(beat);
        At(beat.T, () =>
        {
            var box = HullBox();
            fx.Flash([box], Palette.Block with { A = 0.08f }, 0.35f);
            fx.RectRing(box, Palette.Block, 16f, 0.45f);
            fx.RectRing(box, Palette.Block, 30f, 0.55f, 2f, delay: 0.08f);
            var block = LabelCenter(w.BlockLabel);
            fx.Stream(origin(), block, Palette.Block, 8, Tuning.StreamTime, 3f, ParticleShape.Hex, 50f);
            fx.FloatText($"+{amount} BLOCK", block + new Vector2(0, -34), Palette.Block, 30, 0.9f, 40f, Tuning.StreamTime * 0.8f);
            Haptics.Play(HapticKind.Light);
            Sfx.Play(SfxCue.BlockGain);
        });
        beat.T += Tuning.EffectGap;
    }

    private void OnEnergyGained(Beat beat, int amount)
    {
        var origin = OriginOf(beat);
        At(beat.T, () => fx.Stream(origin(), LabelCenter(w.EnergyLabel), Palette.Energy, 10, Tuning.StreamTime, 2.6f, ParticleShape.Dot, 60f));
        At(beat.T + (Tuning.StreamTime * 0.85f), () =>
        {
            var to = LabelCenter(w.EnergyLabel);
            fx.Pulse(to, Palette.Energy, 70f, 0.45f);
            fx.Flash([LabelRect(w.EnergyLabel)], Palette.Energy with { A = 0.15f }, 0.4f);
            fx.FloatText($"+{amount}", to + new Vector2(0, -36), Palette.Energy, 34);
            Haptics.Play(HapticKind.Tick);
            Sfx.Play(SfxCue.EnergyGain);
        });
        beat.T += Tuning.EffectGap;
    }

    private void OnFocusGained(Beat beat, int amount)
    {
        var origin = OriginOf(beat);
        At(beat.T, () =>
        {
            var c = origin();
            float cell = CellSize();
            fx.Ring(c, Palette.Focus, cell * 0.9f, cell * 0.2f, 0.35f, 2.5f);
            fx.Ring(c, Palette.Focus, cell * 0.6f, 4f, 0.3f, 2f, delay: 0.06f);
            fx.Pulse(c, Palette.Focus, cell * 0.8f, 0.4f, 0.25f);
            fx.FloatText($"+{amount} FOCUS", c + new Vector2(0, -cell * 0.55f), Palette.Focus, 26, 0.9f, 40f);
            Haptics.Play(HapticKind.Tick);
            Sfx.Play(SfxCue.FocusGain);
        });
        beat.T += Tuning.EffectGap;
    }

    private void Repaired(Beat beat, int pieceId)
    {
        var origin = OriginOf(beat);
        At(beat.T, () =>
        {
            if (PieceCenter(pieceId) is { } to)
            {
                fx.Bolt(origin(), to, Palette.Repair, Tuning.RepairBeam, 3f, Colors.White, 0.3f, beam: true);
            }
        });
        At(beat.T + Tuning.RepairBeam, () =>
        {
            var cells = PieceCells(pieceId);
            var c = PieceCenter(pieceId) ?? HullBox().GetCenter();
            fx.Sweep(cells, Palette.Repair, 0.45f);
            fx.Flash(cells, Palette.Repair with { A = 0.25f }, 0.5f);
            fx.Burst(c, Palette.Repair, 16, 120f, 0.7f, 2.2f, ParticleShape.Dot, -Mathf.Pi / 2f, 1.6f, gravity: -80f);
            fx.FloatText("ONLINE", c, Palette.Repair, 26, 1.0f, 46f);
            Haptics.Play(HapticKind.Medium);
            Sfx.Play(SfxCue.Repair);
        });
        beat.T += Tuning.RepairBeam + 0.12f;
    }

    private void Grew(Beat beat, int growth)
    {
        var origin = OriginOf(beat);
        At(beat.T, () =>
        {
            var c = origin();
            fx.Burst(c, Palette.GardenGlow, 14, 110f, 0.85f, 2.6f, ParticleShape.Leaf, -Mathf.Pi / 2f, 1.3f, gravity: -40f, drag: 2f);
            fx.FloatText($"GROWN ×{growth}", c + new Vector2(0, -CellSize() * 0.5f), Palette.GardenGlow, 22, 0.9f, 50f, 0.05f);
            Haptics.Play(HapticKind.Tick);
            Sfx.Play(SfxCue.Grow);
        });
        beat.T += Tuning.EffectGap;
    }

    // Passives

    /// <summary>
    /// A passive takes effect: its piece lights up and its mark sends out a ring. What it does follows as its own
    /// event, and starts from the piece. Lane armor waits for the heavy attack it reduces (<see cref="OnShipHit"/>).
    /// </summary>
    private void OnPassiveFired(Beat beat, PassiveFired fired)
    {
        beat.Source = fired.PieceId;
        beat.CardCenter = null;
        beat.CardDefId = null;
        beat.Focused = false;
        if (fired.Trigger == PassiveTrigger.Standing)
        {
            beat.ArmorPiece = fired.PieceId;
            return;
        }

        // After a won fight the pulse waits a moment, so that it is not lost in the enemy's shatter.
        float at = fired.Trigger == PassiveTrigger.FightWon
            ? Math.Max(beat.T, Math.Max(beat.LastImpact, 0f) + Tuning.RegainAfterImpact)
            : beat.T;
        float pitch = 1f + (beat.Passives++ * Tuning.PassivePitchStep);
        At(at, () => PassivePulse(fired.PieceId, Palette.Passive, pitch));
        beat.T = at + Tuning.PassiveGap;
    }

    private void PassivePulse(int pieceId, Color color, float pitch = 1f)
    {
        float cell = CellSize();
        fx.Flash(PieceCells(pieceId), color with { A = 0.3f }, 0.4f);
        if (w.Grid.PassiveMarkCenter(pieceId) is { } local)
        {
            var mark = fx.FromGlobal(w.Grid.GlobalPosition + local);
            fx.Ring(mark, color, 4f, cell * 0.6f, 0.4f, 2f);
            fx.Pulse(mark, color, cell * 0.45f, 0.3f);
        }

        Haptics.Play(HapticKind.Tick);
        Sfx.Play(SfxCue.PassiveFire, pitch);
    }

    /// <summary>The hull that a won fight gives back streams from the piece to the hull's numbers.</summary>
    private void OnHullRegained(Beat beat, HullRegained regained)
    {
        var origin = OriginOf(beat);
        float at = beat.T;
        At(at, () =>
        {
            var to = LabelCenter(w.ShipLabel);
            fx.Stream(origin(), to, Palette.Repair, 8, Tuning.StreamTime, 2.6f, ParticleShape.Dot, 50f);
            fx.Flash([LabelRect(w.ShipLabel)], Palette.Repair with { A = 0.2f }, 0.5f);
            fx.FloatText($"+{regained.Amount} HULL", to + new Vector2(0, -34), Palette.Repair, 30, 0.9f, 40f, Tuning.StreamTime * 0.8f);
        });
        beat.T = at + Tuning.EffectGap;
        beat.HoldUntil = at + Tuning.RegainShown;
    }

    // Cards moving between piles

    private void TurnStart(Beat beat)
    {
        float start = beat.LastImpact >= 0f ? Math.Max(beat.T, beat.LastImpact + Tuning.DealAfterImpact) : beat.T;
        At(start, () =>
        {
            fx.Pulse(LabelCenter(w.EnergyLabel), Palette.Energy, 70f, 0.5f);
            fx.Flash([LabelRect(w.EnergyLabel)], Palette.Energy with { A = 0.18f }, 0.45f);
            fx.Flash([LabelRect(w.TurnLabel)], Palette.HoloBright with { A = 0.25f }, 0.4f);
            Haptics.Play(HapticKind.Tick);
            Sfx.Play(SfxCue.TurnStart);
        });
        beat.T = start;
        beat.TurnDeal = true;
    }

    private void Drawn(Beat beat, int cardId)
    {
        float at = beat.T;
        At(at, () =>
        {
            if (cardRect(cardId) is { } global)
            {
                var to = fx.FromGlobal(global);
                fx.GhostCard(Around(PileAnchor(discard: false), to.Size * 0.3f), Palette.Draw, Tuning.DealFlight, to);
            }
        });

        // A turn's deal ticks once, when it starts; a card drawn by a card ticks as it lands.
        if (!beat.TurnDeal)
        {
            At(at + Tuning.DealFlight, () => Haptics.Play(HapticKind.Tick));
        }

        // Every card is heard as it lands, each a little higher than the one before.
        float pitch = 1f + (beat.Deals.Count * Tuning.DrawPitchStep);
        At(at + Tuning.DealFlight - Tuning.DrawSoundLead, () => Sfx.Play(SfxCue.CardDraw, pitch));

        beat.Deals.Add((cardId, at + (Tuning.DealFlight * 0.7f)));
        beat.T = at + Tuning.DealStagger;
    }

    private void Shuffled(Beat beat, int count)
    {
        At(beat.T, () =>
        {
            var to = PileAnchor(discard: false);
            fx.Stream(PileAnchor(discard: true), to, Palette.HoloLine, Math.Clamp(count, 4, 12), Tuning.ShuffleTime, 2.2f, ParticleShape.Streak, 26f);
            fx.Ring(to, Palette.HoloLine, 6f, 26f, 0.3f, 2f, delay: Tuning.ShuffleTime * 0.7f);
            Haptics.Play(HapticKind.Tick);
            Sfx.Play(SfxCue.Shuffle);
        });
        beat.T += Tuning.ShuffleTime;
    }

    /// <summary>The old hand's cards wait, dimmed, where they were until the enemy's hit lands, then sink into the discard pile.</summary>
    private void HandToDiscard(Beat beat)
    {
        var to = PileAnchor(discard: true);
        for (int i = 0; i < beat.OldHand.Count; i++)
        {
            var rect = beat.OldHand[i];
            float hold = beat.T + (i * 0.025f);
            fx.GhostCard(rect, Palette.Muted, hold + Tuning.DiscardFlight, Around(to, rect.Size * 0.3f), hold);
        }

        if (beat.OldHand.Count > 0)
        {
            At(beat.T, () => Sfx.Play(SfxCue.HandDiscard));
        }

        beat.T += 0.05f;
    }

    // The enemy's turn

    private void OnShipHit(Beat beat, ShipHit hit)
    {
        float launch = beat.T + Tuning.EnemyWindup;
        float impact = launch + Tuning.EnemyBoltTravel;
        var color = hit.HeavyLane is null ? Palette.Threat : Palette.Heavy;

        // The piece whose lane armor reduced this heavy attack lights up as the attack lands on it.
        int? armorPiece = hit.Armored > 0 ? beat.ArmorPiece : null;
        beat.ArmorPiece = null;
        if (armorPiece is int armored)
        {
            At(impact, () =>
            {
                PassivePulse(armored, Palette.Block);

                // Beside the piece's mark, in its first cell: clear of the "BLOCKED" that rises above the hull and of
                // the "OFFLINE" that rises from the piece's center when the attack still disables it.
                float cell = CellSize();
                var at = w.Grid.PassiveMarkCenter(armored) is { } local
                    ? fx.FromGlobal(w.Grid.GlobalPosition + local) + new Vector2(cell * 0.48f, cell * 0.04f)
                    : ShipTarget(hit.HeavyLane);
                fx.FloatText($"{hit.Armored} ARMOR", at, Palette.Block, 22, 0.9f, 14f);
            });
        }

        At(beat.T, () =>
        {
            if (hit.Part is int part)
            {
                w.Part(part)?.Flash(color);
            }
            else
            {
                w.Emblem.Kick(Vector2.Down, 8f);
                w.Emblem.Flash(color, 0.15f);
            }
        });
        At(launch, () => fx.Bolt(hit.Part is int part ? PartCenter(part) : EmblemCenter(), ShipTarget(hit.HeavyLane), color, Tuning.EnemyBoltTravel, hit.HeavyLane is null ? 5f : 8f, Colors.White));
        At(impact, () => ShipImpact(hit));
        beat.LastImpact = impact;
        beat.T = impact + 0.08f;
    }

    private void ShipImpact(ShipHit hit)
    {
        var box = HullBox();
        var at = ShipTarget(hit.HeavyLane);
        if (hit.Blocked > 0)
        {
            fx.RectRing(box, Palette.Block, 6f, 0.4f, 4f);
            fx.Flash([box], Palette.Block with { A = 0.1f }, 0.3f);
            fx.Burst(at, Palette.Block, 12, 200f, 0.45f, 3f, ParticleShape.Hex, -Mathf.Pi / 2f, 2.2f);
            // Above the hull, clear of the piece labels and an "OFFLINE" on the front row.
            fx.FloatText($"{hit.Blocked} BLOCKED", new Vector2(at.X, box.Position.Y - 22f), Palette.Block, 26, 0.9f, 24f);
        }

        if (hit.HullDamage <= 0)
        {
            Haptics.Play(HapticKind.Light);
            Sfx.Play(SfxCue.ShipBlocked);
            return;
        }

        int damage = hit.HullDamage;
        bool heavy = hit.HeavyLane is not null;
        fx.Burst(at, heavy ? Palette.Heavy : Palette.Threat, 18 + Math.Min(damage, 20), 320f, 0.5f, 2.6f, ParticleShape.Streak, Mathf.Pi / 2f, 2.6f, drag: 4f);
        fx.Pulse(at, Palette.Refused, box.Size.X * 0.35f, 0.35f);
        fx.Vignette(Palette.Refused, 0.5f, 0.3f + (0.03f * damage));
        fx.Flash([LabelRect(w.ShipLabel)], Palette.Refused with { A = 0.3f }, 0.45f);
        if (hit.HeavyLane is int lane && LaneRect(lane) is { } laneRect)
        {
            fx.Flash([laneRect], Palette.Heavy with { A = 0.28f }, 0.4f);
        }

        fx.FloatText($"-{damage}", box.GetCenter(), Palette.Refused.Lerp(Colors.White, 0.1f), Math.Clamp(54 + damage, 54, 84), 1.0f, 60f);
        fx.Shake(Mathf.Min(0.7f, 0.3f + (0.025f * damage)));
        Haptics.Play(damage >= 12 || heavy ? HapticKind.Heavy : HapticKind.Medium);
        Sfx.Play(damage >= 12 || heavy ? SfxCue.ShipHitHeavy : SfxCue.ShipHit);
    }

    private void Disabled(Beat beat, int pieceId, int lane)
    {
        At(beat.T, () =>
        {
            var c = PieceCenter(pieceId) ?? ShipTarget(lane);
            fx.Flash(PieceCells(pieceId), Palette.Refused with { A = 0.5f }, 0.55f, flicker: true);
            fx.Burst(c, Palette.Heavy, 24, 260f, 0.7f, 2.4f, ParticleShape.Streak, -Mathf.Pi / 2f, 1.6f, gravity: 520f, drag: 1.5f);
            fx.Burst(c, Palette.Refused, 10, 120f, 0.5f, 3f);
            fx.FloatText("OFFLINE", c, Palette.Refused, 26, 1.1f, 40f);
            fx.Shake(0.25f);
            Haptics.Play(HapticKind.Heavy);
            Sfx.Play(SfxCue.PieceOffline);
            fx.Schedule(0.09f, () => Haptics.Play(HapticKind.Medium));
        });
        beat.T += 0.1f;
    }

    private void OnEnemyBlocked(Beat beat, int amount, int? part)
    {
        float at = beat.T + Tuning.EnemyWindup;
        if (part is int index)
        {
            // A part's block joins the enemy's one block: it streams from the part to the body.
            At(at, () =>
            {
                var from = PartCenter(index);
                var rect = PartRectOrEmblem(index);
                w.Part(index)?.Flash(Palette.Block);
                fx.RectRing(rect, Palette.Block, 10f, 0.4f, 2f);
                fx.Stream(from, EmblemCenter(), Palette.Block, 8, 0.3f, 3f, ParticleShape.Hex, 40f);
                fx.FloatText($"+{amount} BLOCK", from - new Vector2(0f, rect.Size.Y * 0.25f), Palette.Block, 24, 0.9f, 30f);
                Haptics.Play(HapticKind.Tick);
                Sfx.Play(SfxCue.EnemyBlock);
            });
            beat.LastImpact = at + 0.3f;
            beat.T = at + 0.3f;
            return;
        }

        At(at, () =>
        {
            var c = EmblemCenter();
            float r = EmblemRadius();
            fx.Ring(c, Palette.Block, r * 1.3f, r * 0.95f, 0.4f, 3f, hex: true);
            fx.Ring(c, Palette.Block, r * 1.5f, r * 1.0f, 0.45f, 2f, hex: true, delay: 0.07f);
            fx.Pulse(c, Palette.Block, r * 1.2f, 0.4f);
            fx.FloatText($"+{amount} BLOCK", c + new Vector2(0, r * 0.7f), Palette.Block, 28, 0.9f, 30f);
            Haptics.Play(HapticKind.Tick);
            Sfx.Play(SfxCue.EnemyBlock);
        });
        beat.LastImpact = at + 0.3f;
        beat.T = at + 0.3f;
    }

    /// <summary>The reticle locks onto what the player aims at: the body (null) or one of its parts.</summary>
    private void ReticleLock(int? part)
    {
        Sfx.Play(SfxCue.TargetLock);
        if (part is int index)
        {
            var rect = PartRectOrEmblem(index);
            fx.RectRing(rect, Palette.Threat, 4f, 0.35f, 2.5f);
            fx.RectRing(rect, Palette.Threat, 14f, 0.45f, 1.5f, delay: 0.06f);
            Haptics.Play(HapticKind.Tick);
            return;
        }

        var c = EmblemCenter();
        float r = EmblemRadius();
        fx.Ring(c, Palette.Threat, r * 1.6f, r * 0.95f, 0.45f, 2.5f);
        fx.Ring(c, Palette.Threat, r * 1.9f, r * 1.0f, 0.5f, 1.5f, delay: 0.08f);
        fx.Pulse(c, Palette.Threat, r * 1.2f, 0.5f, 0.3f);
    }

    // The end of the fight

    private void Won(Beat beat)
    {
        float at = Math.Max(beat.LastImpact, 0f) + 0.02f;

        // The screen redraws the emblem grey this frame; keep its color until it shatters.
        w.Emblem.DelayTintChange(at + 0.05f);
        At(at, () =>
        {
            var c = EmblemCenter();
            float r = EmblemRadius();
            fx.Burst(c, Palette.Threat, 50, 460f, 0.8f, 2.8f, ParticleShape.Streak, drag: 2.5f);
            fx.Burst(c, Colors.White, 26, 260f, 0.6f, 2.4f);
            fx.Burst(c, Palette.Threat.Lerp(Colors.White, 0.3f), 18, 200f, 0.9f, 3.5f, ParticleShape.Hex, gravity: 200f);
            for (int i = 0; i < 3; i++)
            {
                fx.Ring(c, i == 0 ? Colors.White : Palette.Threat, r * 0.3f, r * (1.4f + (0.4f * i)), 0.45f + (0.1f * i), 3f - (i * 0.6f), delay: i * 0.07f);
            }

            fx.Pulse(c, Colors.White, r * 2f, 0.35f);
            w.Emblem.Flash(Colors.White, 0.35f);
            fx.Shake(0.4f);
            Sfx.Play(SfxCue.FightWon);
            Haptics.Play(HapticKind.Light);
            fx.Schedule(0.1f, () => Haptics.Play(HapticKind.Light));
            fx.Schedule(0.2f, () => Haptics.Play(HapticKind.Heavy));
        });
        beat.ExitHold = Math.Max(Math.Max(Tuning.WinHold, at + Tuning.AfterShatter), beat.HoldUntil);
        beat.ResultAt = at + Tuning.ResultAfterImpact;
    }

    private void Lost(Beat beat)
    {
        float at = Math.Max(beat.LastImpact, 0f) + 0.1f;
        At(at, () =>
        {
            fx.Vignette(Palette.Refused, 0.6f);
            fx.Vignette(Palette.Refused, 0.6f, delay: 0.3f);
            fx.Flash([HullBox()], Palette.Refused with { A = 0.25f }, 0.7f, flicker: true);
            fx.Shake(0.9f);
            Haptics.Play(280, 1f);
            Sfx.Play(SfxCue.FightLost);
        });
        beat.ResultAt = at + Tuning.ResultAfterImpact;
    }

    // Which sound

    /// <summary>A card sounds like the piece it comes from: the voice of the piece's first tag, the same tag that colors it.</summary>
    private static SfxCue CardVoice(PieceDef? piece) => piece is { Tags.Count: > 0 }
        ? piece.Tags[0] switch
        {
            Tag.Core => SfxCue.CardPlayCore,
            Tag.Weapon => SfxCue.CardPlayWeapon,
            Tag.Shield => SfxCue.CardPlayShield,
            Tag.Solar => SfxCue.CardPlaySolar,
            Tag.Garden => SfxCue.CardPlayGarden,
            Tag.Drone => SfxCue.CardPlayDrone,
            _ => SfxCue.CardPlay,
        }
        : SfxCue.CardPlay;

    /// <summary>A hit on the enemy sounds bigger at the same damage where its haptic pulse gets stronger.</summary>
    private static SfxCue HitSound(int damage) => damage >= 20 ? SfxCue.HitHeavy : damage >= 10 ? SfxCue.HitMedium : SfxCue.HitLight;

    // Where things are, in the effect layer's coordinates

    private void At(float seconds, Action action) => fx.Schedule(seconds, action);

    /// <summary>Where the current card's effects start: its piece, or the card itself, or the ship.</summary>
    private Func<Vector2> OriginOf(Beat beat)
    {
        int? source = beat.Source;
        var card = beat.CardCenter;
        return () => (source is int id ? PieceCenter(id) : null) ?? card ?? HullBox().GetCenter();
    }

    private Vector2 EmblemCenter() => fx.FromGlobal(w.Emblem.GetGlobalRect().GetCenter());

    /// <summary>A part's panel, or the emblem when the panel is gone (the screen was rebuilt).</summary>
    private Rect2 PartRectOrEmblem(int part) => fx.FromGlobal(w.Part(part)?.GetGlobalRect() ?? w.Emblem.GetGlobalRect());

    private Vector2 PartCenter(int part) => PartRectOrEmblem(part).GetCenter();

    /// <summary>The radius of the reticle's inner disc.</summary>
    private float EmblemRadius() => Mathf.Min(w.Emblem.Size.X, w.Emblem.Size.Y) * 0.4f;

    private Rect2 FromGrid(Rect2 local) => new(fx.FromGlobal(w.Grid.GlobalPosition + local.Position), local.Size);

    private Vector2? PieceCenter(int pieceId) =>
        w.Grid.PieceCenter(pieceId) is { } local ? fx.FromGlobal(w.Grid.GlobalPosition + local) : null;

    private IReadOnlyList<Rect2> PieceCells(int pieceId) => w.Grid.PieceCellRects(pieceId).Select(FromGrid).ToList();

    private Rect2? LaneRect(int lane) => w.Grid.LaneRect(lane) is { } local ? FromGrid(local) : null;

    private Rect2 HullBox() => w.Grid.HullBounds() is { } local ? FromGrid(local) : fx.FromGlobal(w.Grid.GetGlobalRect());

    private float CellSize() => w.Grid.LaneRect(0)?.Size.X ?? 60f;

    /// <summary>A normal attack aims at the middle of the ship; a heavy one at the front of its lane.</summary>
    private Vector2 ShipTarget(int? lane)
    {
        var box = HullBox();
        return lane is int l && LaneRect(l) is { } rect
            ? new Vector2(rect.GetCenter().X, box.Position.Y + (CellSize() * 0.5f))
            : box.GetCenter();
    }

    private static Rect2 Around(Vector2 center, Vector2 size) => new(center - (size / 2f), size);

    private static float TextWidth(Label label, string text) =>
        label.GetThemeFont("font").GetStringSize(text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X;

    /// <summary>A point <paramref name="x"/> pixels into a label's text, halfway down.</summary>
    private Vector2 TextPoint(Label label, float x)
    {
        var r = label.GetGlobalRect();
        return fx.FromGlobal(new Vector2(r.Position.X + x, r.Position.Y + (r.Size.Y / 2f)));
    }

    private Vector2 LabelCenter(Label label) => TextPoint(label, Mathf.Min(TextWidth(label, label.Text), label.Size.X) / 2f);

    /// <summary>The part of a label that its text covers, with a little room around it.</summary>
    private Rect2 LabelRect(Label label)
    {
        var r = fx.FromGlobal(label.GetGlobalRect());
        float width = Mathf.Min(TextWidth(label, label.Text) + 12f, r.Size.X);
        return new Rect2(r.Position - new Vector2(6f, 0f), new Vector2(width, r.Size.Y));
    }

    /// <summary>The middle of "Draw N" or "Discard N" in the piles line.</summary>
    private Vector2 PileAnchor(bool discard)
    {
        var label = w.PilesLabel;
        string text = label.Text;
        int at = text.IndexOf("Discard", StringComparison.Ordinal);
        if (at < 0)
        {
            return LabelCenter(label);
        }

        if (!discard)
        {
            return TextPoint(label, TextWidth(label, text[..at].TrimEnd(' ', '·')) / 2f);
        }

        int end = text.IndexOf("  ·  ", at, StringComparison.Ordinal);
        string segment = end < 0 ? text[at..] : text[at..end];
        return TextPoint(label, TextWidth(label, text[..at]) + (TextWidth(label, segment) / 2f));
    }

    /// <summary>The state carried from one event to the next while a timeline is built.</summary>
    private sealed class Beat
    {
        /// <summary>The time the next effect starts, in seconds from now.</summary>
        public float T { get; set; }

        /// <summary>When the latest hit landed, or -1 before any; the new hand deals in after it.</summary>
        public float LastImpact { get; set; } = -1f;

        public int? Source { get; set; }

        public Vector2? CardCenter { get; set; }

        public string? CardDefId { get; set; }

        public bool CardUpgraded { get; set; }

        public bool Focused { get; set; }

        public bool TurnDeal { get; set; }

        /// <summary>These are the first events of a fight, played before the screen has drawn it.</summary>
        public bool Opening { get; set; }

        /// <summary>How many passives have pulsed in this action; each sounds a little higher than the last.</summary>
        public int Passives { get; set; }

        /// <summary>The piece whose lane armor reduces the heavy attack that comes next.</summary>
        public int? ArmorPiece { get; set; }

        /// <summary>The time until which a won fight's screen should stay up for effects that outlast the shatter.</summary>
        public float HoldUntil { get; set; }

        public double BarValue { get; set; }

        public double BarMax { get; set; } = 1.0;

        public Rect2 Bar { get; set; }

        public List<Rect2> OldHand { get; set; } = [];

        public List<(int CardId, float At)> Deals { get; } = [];

        public float? ResultAt { get; set; }

        public float? ExitHold { get; set; }
    }
}
