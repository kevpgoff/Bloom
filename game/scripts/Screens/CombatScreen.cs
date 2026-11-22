using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Feedback;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// The combat screen. It draws the run and forwards every tap to <see cref="GameRoot.Act"/>, which saves
/// after it. Numbers that depend on rules (a card's damage after bonuses, whether it can be played, which
/// piece a heavy attack would hit) come from <see cref="CombatEngine"/>. After the fight it shows the
/// defeat or, after the last fight, the voyage complete; a fight won before that goes to the Reward screen.
/// </summary>
/// <remarks>
/// <para>
/// Picking pieces for a repair card is the only rule-related state kept here. It is not saved: quitting while
/// picking resumes as if the card had not been tapped.
/// </para>
/// <para>
/// Feedback: the events of each action (<see cref="GameRoot.CombatEvents"/>) play on an <see cref="FxLayer"/>
/// through <see cref="CombatFeedback"/>. The screen still redraws to the new state at once; new cards fade in
/// when their deal reaches them, the result overlay waits for the last hit, and after a won fight
/// <see cref="TakeExitHold"/> tells <see cref="MainScreen"/> how long to wait before the next screen.
/// </para>
/// </remarks>
public partial class CombatScreen : Control
{
    private const int HandGap = 8;
    private const float MinCardWidth = 88;
    private const float MaxCardWidth = 136;
    private const float CardHeight = 220;
    private const float CardPadding = 8;
    private const float DisabledCardAlpha = 0.45f;

    /// <summary>
    /// End Turn ignores a press this soon after the last one, so a double tap can't end two turns while the new
    /// hand deals in. Real time, like the finger.
    /// </summary>
    private const ulong EndTurnGuardMs = 300;

    private readonly List<int> _chosenTargets = [];
    private readonly Dictionary<int, Button> _cardButtons = [];
    private readonly HashSet<int> _waitingToDeal = [];
    private int? _targetingCardId;
    private int _targetsNeeded;
    private bool _refreshQueued;
    private float _cardWidth = MaxCardWidth;
    private bool _resultHeld;
    private float _exitHold;
    private ulong? _lastEndTurnMs;

    private FxLayer _fx = null!;
    private CombatFeedback _feedback = null!;

    private EnemyEmblem _enemyEmblem = null!;
    private Label _enemyName = null!;
    private Label _turn = null!;
    private ProgressBar _enemyHullBar = null!;
    private Label _enemyHull = null!;
    private Label _enemyBlock = null!;
    private TextureRect _intentIcon = null!;
    private Label _intent = null!;
    private ShipGridView _grid = null!;
    private Label _ship = null!;
    private Label _energy = null!;
    private Label _piles = null!;
    private Control _targetBanner = null!;
    private Label _targetLabel = null!;
    private ScrollContainer _handScroll = null!;
    private HBoxContainer _hand = null!;
    private Button _endTurn = null!;
    private Control _resultOverlay = null!;
    private Label _resultTitle = null!;
    private Label _resultMessage = null!;

    private static RunSession? Session => GameRoot.Instance.Session;

    public override void _Ready()
    {
        _enemyEmblem = GetNode<EnemyEmblem>("%EnemyEmblem");
        _enemyName = GetNode<Label>("%EnemyName");
        _turn = GetNode<Label>("%TurnLabel");
        _enemyHullBar = GetNode<ProgressBar>("%EnemyHullBar");
        _enemyHull = GetNode<Label>("%EnemyHullLabel");
        _enemyBlock = GetNode<Label>("%EnemyBlockLabel");
        _intentIcon = GetNode<TextureRect>("%IntentIcon");
        _intent = GetNode<Label>("%IntentLabel");

        BloomTheme.AttachPanelFrame(GetNode<Control>("Layout/EnemyPanel"));
        BloomTheme.AttachPanelFrame(GetNode<Control>("ResultOverlay/Center/ResultPanel"), Palette.HoloBright with { A = 0.7f });
        _grid = GetNode<ShipGridView>("%ShipGrid");
        _ship = GetNode<Label>("%ShipLabel");
        _energy = GetNode<Label>("%EnergyLabel");
        _piles = GetNode<Label>("%PilesLabel");
        _targetBanner = GetNode<Control>("%TargetBanner");
        _targetLabel = GetNode<Label>("%TargetLabel");
        _handScroll = GetNode<ScrollContainer>("%HandScroll");
        _hand = GetNode<HBoxContainer>("%Hand");
        _endTurn = GetNode<Button>("%EndTurnButton");
        _resultOverlay = GetNode<Control>("%ResultOverlay");
        _resultTitle = GetNode<Label>("%ResultTitle");
        _resultMessage = GetNode<Label>("%ResultMessage");

        // Effects draw over the screen's content and under the result overlay.
        var layout = GetNode<Control>("Layout");
        _fx = new FxLayer { Name = "Fx", ShakeTarget = layout };
        AddChild(_fx);
        MoveChild(_fx, layout.GetIndex() + 1);
        _fx.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _feedback = new CombatFeedback(
            _fx,
            new CombatFeedback.Widgets(_enemyEmblem, _grid, _enemyHullBar, _ship, _energy, _piles, _turn, _endTurn),
            CardRect,
            () => _cardButtons.Values.Where(IsLive).Select(b => b.GetGlobalRect()).ToList());

        _endTurn.Pressed += OnEndTurnPressed;
        GetNode<Button>("%CancelButton").Pressed += CancelTargeting;
        GetNode<Button>("%NewRunButton").Pressed += StartNewRun;
        _grid.PieceTapped += OnPieceTapped;
        _handScroll.Resized += OnHandResized;
        GameRoot.Instance.Changed += QueueRefresh;
        GameRoot.Instance.CombatEvents += OnCombatEvents;

        // A fight starts on the Fitting screen, so its first events (the opening deal) are played here.
        if (Session?.LastEvents is { Count: > 0 } opening && opening.Any(e => e is FightStarted))
        {
            OnCombatEvents(opening);
        }

        Refresh();
        if (Session?.State is { Combat: { } combat } run)
        {
            GD.Print($"CombatScreen: turn {combat.Turn} against {_enemyName.Text}, {combat.Hand.Count} cards in hand, hull {run.Hull}/{run.MaxHull}.");
        }
    }

    public override void _ExitTree()
    {
        GameRoot.Instance.Changed -= QueueRefresh;
        GameRoot.Instance.CombatEvents -= OnCombatEvents;
    }

    /// <summary>
    /// How long to keep this screen up before showing the next one, once: after a won fight, so its last hit and
    /// the enemy's shatter can play. Zero otherwise. Reading it clears it.
    /// </summary>
    public float TakeExitHold()
    {
        float hold = _exitHold;
        _exitHold = 0f;
        return hold;
    }

    /// <summary>
    /// Plays an action's events. Runs before the redraw, so the old card buttons can still be measured.
    /// </summary>
    private void OnCombatEvents(IReadOnlyList<CombatEvent> events)
    {
        if (Session is not { } session || !IsInsideTree())
        {
            return;
        }

        var cue = _feedback.Play(session, events);
        foreach (var (cardId, at) in cue.DealIns)
        {
            _waitingToDeal.Add(cardId);
            _fx.Schedule(at, () => DealIn(cardId));
        }

        if (cue.ResultAt is float result)
        {
            _resultHeld = true;
            _fx.Schedule(result, () =>
            {
                _resultHeld = false;
                QueueRefresh();
            });
        }

        // Only a fight that leads to the Reward screen leaves this screen; after the last fight it stays up.
        if (cue.ExitHold is float hold && session.State.Phase == RunPhase.Reward)
        {
            _exitHold = hold;
        }
    }

    private void OnEndTurnPressed()
    {
        ulong now = Time.GetTicksMsec();
        if (_lastEndTurnMs is ulong last && now - last < EndTurnGuardMs)
        {
            return;
        }

        _lastEndTurnMs = now;
        _feedback.EndTurnPressed();
        Act(s => s.Combat.EndTurn(s.State));
    }

    private static bool IsLive(Button button) => IsInstanceValid(button) && button.IsInsideTree();

    private Rect2? CardRect(int cardId) =>
        _cardButtons.TryGetValue(cardId, out var button) && IsLive(button) ? button.GetGlobalRect() : null;

    /// <summary>A dealt card fades in from nothing to its usual brightness, which is dimmer when it can't be played.</summary>
    private void DealIn(int cardId)
    {
        _waitingToDeal.Remove(cardId);
        if (_cardButtons.TryGetValue(cardId, out var button) && IsLive(button))
        {
            button.CreateTween().TweenProperty(button, "modulate:a", button.Disabled ? DisabledCardAlpha : 1f, CombatFeedback.Tuning.DealFade);
        }
    }

    /// <summary>Runs one player action through the session (which saves); <see cref="GameRoot.Changed"/> then redraws.</summary>
    private static void Act(Action<RunSession> action) => GameRoot.Instance.Act(action);

    /// <summary>
    /// Redraws at the end of the frame. Taps call this rather than <see cref="Refresh"/>, because a
    /// redraw replaces the card buttons, and a button should not be removed while it handles its own tap.
    /// </summary>
    private void QueueRefresh()
    {
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Callable.From(() =>
        {
            _refreshQueued = false;

            // The run may have moved on to another screen, which removes this one.
            if (IsInsideTree())
            {
                Refresh();
            }
        }).CallDeferred();
    }

    private void Refresh()
    {
        if (Session is not { } session)
        {
            return;
        }

        var run = session.State;
        var combat = run.Combat;
        if (combat is null || (_targetingCardId is int cardId && !combat.Hand.Exists(c => c.Id == cardId)))
        {
            ClearTargeting();
        }

        ShowEnemy(session, run, combat);
        ShowStatus(run, combat);
        ShowHand(session, run, combat);
        ShowTargeting(combat);
        _endTurn.Disabled = combat is null || _targetingCardId is not null;
        ShowResult(session, run);
    }

    private void ShowEnemy(RunSession session, RunState run, CombatState? combat)
    {
        if (combat is null)
        {
            _enemyEmblem.Icon = run.LastFight is { } last ? ArtLibrary.EnemyIcon(last.EnemyId) : null;
            _enemyEmblem.Tint = Palette.Muted;
            _enemyEmblem.Active = false;
            _enemyName.Text = run.LastFight is { } lastFight ? session.Content.Enemy(lastFight.EnemyId).Name : "";
            _turn.Text = "";
            _enemyHullBar.Value = 0;
            _enemyHull.Text = "";
            _enemyBlock.Text = "";
            _intentIcon.Texture = null;
            _intent.Text = "";
            return;
        }

        var enemy = combat.Enemy;
        _enemyEmblem.Icon = ArtLibrary.EnemyIcon(enemy.DefId);
        _enemyEmblem.Tint = Palette.Threat;
        _enemyEmblem.Active = true;
        _enemyName.Text = session.Content.Enemy(enemy.DefId).Name;
        _turn.Text = $"Turn {combat.Turn}";
        _enemyHullBar.MaxValue = enemy.MaxHull;
        _enemyHullBar.Value = enemy.Hull;
        _enemyHull.Text = $"Hull {enemy.Hull} / {enemy.MaxHull}";
        _enemyBlock.Text = enemy.Block > 0 ? $"Block {enemy.Block}" : "";

        var intent = session.Combat.CurrentIntent(combat);
        var (text, color) = intent.Kind switch
        {
            IntentKind.Attack => ($"Next: attack for {intent.Amount}", Palette.Attack),
            IntentKind.HeavyAttack => ($"Next: heavy attack for {intent.Amount} on the marked lane. " +
                                       (session.Combat.HeavyAttackTarget(run) is { } target
                                           ? $"Disables {session.Content.Piece(target.Piece.DefId).Name} if any damage gets past block."
                                           : "The lane is empty."), Palette.Heavy),
            IntentKind.Block => ($"Next: block {intent.Amount}", Palette.Block),
            _ => (intent.Kind.ToString(), Colors.White),
        };
        _intentIcon.Texture = ArtLibrary.IntentIcon(intent.Kind);
        _intentIcon.Modulate = color;
        _intent.Text = text;
        _intent.AddThemeColorOverride("font_color", color);
    }

    private void ShowStatus(RunState run, CombatState? combat)
    {
        _ship.Text = combat is null ? $"Hull {run.Hull} / {run.MaxHull}" : $"Hull {run.Hull} / {run.MaxHull}    Block {combat.Block}";
        _energy.Text = combat is null ? "" : $"Energy {combat.Energy}";
        if (combat is null)
        {
            _piles.Text = "";
            return;
        }

        var parts = new List<string> { $"Draw {combat.DrawPile.Count}", $"Discard {combat.DiscardPile.Count}" };
        if (combat.OfflineCards.Count > 0)
        {
            parts.Add($"Offline {combat.OfflineCards.Count}");
        }

        if (combat.PendingFocus > 0)
        {
            parts.Add($"Focus +{combat.PendingFocus} on next attack");
        }

        _piles.Text = string.Join("  ·  ", parts);
    }

    private void ShowHand(RunSession session, RunState run, CombatState? combat)
    {
        foreach (var child in _hand.GetChildren())
        {
            _hand.RemoveChild(child);
            child.QueueFree();
        }

        _cardButtons.Clear();
        if (combat is null)
        {
            return;
        }

        _cardWidth = CardWidth(combat.Hand.Count);
        foreach (var card in combat.Hand)
        {
            var button = BuildCard(session, run, session.Combat.Preview(run, card), _cardWidth);
            _cardButtons[card.Id] = button;
            _hand.AddChild(button);
        }
    }

    /// <summary>
    /// Builds one card at the given width. Everything inside is sized from the width, so a hand of 7
    /// still fits on screen and two effects fit side by side on the narrowest card.
    /// </summary>
    private Button BuildCard(RunSession session, RunState run, CardPreview preview, float width)
    {
        float inner = width - (CardPadding * 2);
        float badgeSize = Mathf.Clamp(inner * 0.34f, 28f, 40f);

        bool targetingThis = _targetingCardId == preview.Card.Id;
        bool enabled = preview.Playable && (_targetingCardId is null || targetingThis);
        var source = run.Ship.Find(preview.Card.SourcePieceId);
        var sourceDef = source is null ? null : session.Content.Piece(source.Piece.DefId);
        var accent = sourceDef is null ? Palette.Muted : Palette.ForPiece(sourceDef);

        var button = new Button
        {
            CustomMinimumSize = new Vector2(width, CardHeight),
            Disabled = !enabled,
            ClipContents = true,
            FocusMode = FocusModeEnum.None,
            // A card still on its way from the draw pile starts invisible; DealIn fades it in.
            Modulate = _waitingToDeal.Contains(preview.Card.Id)
                ? Colors.Transparent
                : enabled ? Colors.White : new Color(1, 1, 1, DisabledCardAlpha),
        };
        button.AddThemeStyleboxOverride("normal", BloomTheme.CardSurface(accent, targetingThis, hovered: false, !enabled));
        button.AddThemeStyleboxOverride("hover", BloomTheme.CardSurface(accent, targetingThis, hovered: true, !enabled));
        button.AddThemeStyleboxOverride("pressed", BloomTheme.CardSurface(accent, highlighted: true, hovered: true, !enabled));
        button.AddThemeStyleboxOverride("disabled", BloomTheme.CardSurface(accent, highlighted: false, hovered: false, disabled: true));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        if (BloomTheme.CreateFrameOverlay(ArtLibrary.CardFrame, targetingThis ? Palette.Selectable : accent with { A = 0.85f }) is { } frame)
        {
            button.AddChild(frame);
        }

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 6);

        // Top strip: sunburst energy medallion + source piece emblem share one compact row,
        // freeing the full width below for large, legible card title and effect numbers.
        var topRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        topRow.AddThemeConstantOverride("separation", inner > 100 ? 6 : 4);
        topRow.AddChild(BuildEnergyBadge(preview.Def.Cost, badgeSize));

        var artPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, badgeSize + 4),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        artPanel.AddThemeStyleboxOverride("panel", BloomTheme.FlatBox(
            Palette.SurfaceDeep.Lerp(accent, 0.08f),
            accent with { A = 0.4f },
            border: 1,
            radius: 2,
            padX: 4,
            padY: 1));

        // The emblem fills the strip's height (the line-art icons need every pixel at this size), and it takes
        // only the width the strip has, so a narrow card draws it smaller instead of growing wider.
        var emblem = new TextureRect
        {
            Texture = sourceDef is not null
                ? ArtLibrary.PieceIcon(sourceDef)
                : preview.Def.Effects.Count > 0
                    ? ArtLibrary.EffectIcon(preview.Def.Effects[0])
                    : null,
            CustomMinimumSize = new Vector2(0, badgeSize + 2),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Modulate = Palette.Glyph(accent),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        artPanel.AddChild(emblem);
        topRow.AddChild(artPanel);
        box.AddChild(topRow);

        // Full-width card title in tracked capitals, for legibility on narrow portrait cards.
        string name = preview.Def.Name.ToUpperInvariant();
        var title = MakeLabel(name, FitFontSize(name, inner, 21, 14, BloomTheme.Hud), Palette.Ink);
        title.AddThemeFontOverride("font", BloomTheme.Hud);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        box.AddChild(title);

        box.AddChild(new ColorRect
        {
            Color = accent with { A = 0.5f },
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        // Bottom effects area: large effect symbols with bold numbers inside + full-word captions.
        int effectCount = preview.Effects.Count;
        bool single = effectCount == 1;
        float gap = single ? 0 : 4;
        float columnWidth = (inner - (gap * (effectCount - 1))) / Math.Max(1, effectCount);
        var effectsRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        effectsRow.AddThemeConstantOverride("separation", (int)gap);
        foreach (var (current, original) in preview.Effects.Zip(preview.Def.Effects))
        {
            effectsRow.AddChild(BuildEffectBadge(current, original, single, columnWidth));
        }

        box.AddChild(effectsRow);

        button.AddChild(box);
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = CardPadding;
        box.OffsetTop = CardPadding;
        box.OffsetRight = -CardPadding;
        box.OffsetBottom = -CardPadding;

        // The spec: touching a card highlights the piece that adds it, because the ship is the deck.
        int pieceId = preview.Card.SourcePieceId;
        button.MouseEntered += () => _grid.HighlightPieceId = pieceId;
        button.MouseExited += () => _grid.HighlightPieceId = null;
        button.ButtonDown += () => _grid.HighlightPieceId = pieceId;
        button.ButtonUp += () => _grid.HighlightPieceId = null;
        button.Pressed += () => OnCardPressed(preview);
        return button;
    }

    /// <summary>An effect symbol with its number inside and its name under it, sized to fit <paramref name="columnWidth"/>.</summary>
    private Control BuildEffectBadge(Effect current, Effect original, bool single, float columnWidth)
    {
        var column = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = single ? SizeFlags.ShrinkCenter : SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        column.AddThemeConstantOverride("separation", 2);

        float symbolSize = Mathf.Min(single ? 76f : 50f, columnWidth);
        int numberFontSize = Math.Max(13, Mathf.RoundToInt(symbolSize * 0.43f));
        string word = CardText.EffectWord(current).ToUpperInvariant();
        int captionFontSize = FitFontSize(word, columnWidth, single ? 15 : 13, 10, BloomTheme.Hud);
        var tint = Palette.ForEffect(current);
        var (valueText, boosted) = CardText.EffectNumber(current, original);

        var symbol = new Control
        {
            CustomMinimumSize = new Vector2(symbolSize, symbolSize),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (ArtLibrary.EffectIcon(current) is { } iconTex)
        {
            var bg = new TextureRect
            {
                Texture = iconTex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = tint,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            symbol.AddChild(bg);
        }

        var number = new Label
        {
            Text = valueText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        number.AddThemeFontOverride("font", BloomTheme.DisplayBold);
        number.AddThemeFontSizeOverride("font_size", numberFontSize);
        number.AddThemeColorOverride("font_color", boosted ? Palette.Boosted : Colors.White);
        number.AddThemeColorOverride("font_outline_color", Palette.SurfaceDeep);
        number.AddThemeConstantOverride("outline_size", symbolSize >= 60 ? 7 : symbolSize >= 40 ? 5 : 4);
        // Every effect icon keeps its dark well at the center (game/art/README.md), so the number is centered too.
        number.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        symbol.AddChild(number);
        column.AddChild(symbol);

        var caption = MakeLabel(word, captionFontSize, tint.Lightened(0.28f));
        caption.AddThemeFontOverride("font", BloomTheme.Hud);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.AutowrapMode = TextServer.AutowrapMode.Off;
        column.AddChild(caption);

        return column;
    }

    private static Control BuildEnergyBadge(int cost, float size)
    {
        var badge = new Control
        {
            CustomMinimumSize = new Vector2(size, size),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (ArtLibrary.EnergyBadge is { } tex)
        {
            var bg = new TextureRect
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = Palette.Energy,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            badge.AddChild(bg);
        }

        var costLabel = new Label
        {
            Text = cost.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        costLabel.AddThemeFontOverride("font", BloomTheme.DisplayBold);
        costLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(size * 0.6f));
        costLabel.AddThemeColorOverride("font_color", Colors.White);
        costLabel.AddThemeColorOverride("font_outline_color", Palette.SurfaceDeep);
        costLabel.AddThemeConstantOverride("outline_size", 5);
        costLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        badge.AddChild(costLabel);
        return badge;
    }

    private static Label MakeLabel(string text, int fontSize, Color color)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>
    /// The largest font size from <paramref name="largest"/> down to <paramref name="smallest"/> at which the text
    /// fits the width, in <paramref name="font"/> or else the theme's label font.
    /// </summary>
    private int FitFontSize(string text, float maxWidth, int largest, int smallest, Font? font = null)
    {
        font ??= GetThemeFont("font", "Label");
        for (int size = largest; size > smallest; size--)
        {
            if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= maxWidth)
            {
                return size;
            }
        }

        return smallest;
    }

    /// <summary>Cards share the hand's width, between 88 and 136 px each; past that the hand scrolls.</summary>
    private float CardWidth(int count)
    {
        float available = _handScroll.Size.X > 0 ? _handScroll.Size.X : MaxCardWidth * 5;

        // Whole units: the hand rounds each card up, so a fractional width can push the last card off screen.
        return count == 0
            ? MaxCardWidth
            : Mathf.Floor(Mathf.Clamp((available - (HandGap * (count - 1))) / count, MinCardWidth, MaxCardWidth));
    }

    /// <summary>The cards are sized from the hand's width when built, so a new width rebuilds them.</summary>
    private void OnHandResized()
    {
        if (Session?.State.Combat is { } combat && !Mathf.IsEqualApprox(CardWidth(combat.Hand.Count), _cardWidth))
        {
            QueueRefresh();
        }
    }

    private void OnCardPressed(CardPreview preview)
    {
        _grid.HighlightPieceId = null;
        if (_targetingCardId == preview.Card.Id)
        {
            CancelTargeting();
            return;
        }

        if (preview.RepairTargets > 0)
        {
            _targetingCardId = preview.Card.Id;
            _targetsNeeded = preview.RepairTargets;
            _chosenTargets.Clear();
            QueueRefresh();
            return;
        }

        Act(s => s.Combat.PlayCard(s.State, preview.Card.Id));
    }

    private void OnPieceTapped(int pieceId)
    {
        if (_targetingCardId is not int cardId || Session?.State.Combat is not { } combat || !combat.DisabledPieceIds.Contains(pieceId))
        {
            return;
        }

        if (!_chosenTargets.Remove(pieceId))
        {
            _chosenTargets.Add(pieceId);
        }

        if (_chosenTargets.Count < _targetsNeeded)
        {
            QueueRefresh();
            return;
        }

        var targets = _chosenTargets.ToList();
        ClearTargeting();
        Act(s => s.Combat.PlayCard(s.State, cardId, targets));
    }

    private void ShowTargeting(CombatState? combat)
    {
        bool targeting = _targetingCardId is not null && combat is not null;
        _targetBanner.Visible = targeting;
        if (targeting)
        {
            int left = _targetsNeeded - _chosenTargets.Count;
            _targetLabel.Text = left == 1 ? "Tap a disabled piece to repair it." : $"Tap {left} more disabled pieces to repair.";
        }

        _grid.SetState(Session!, targeting ? combat!.DisabledPieceIds.ToList() : [], _chosenTargets.ToList());
    }

    private void CancelTargeting()
    {
        ClearTargeting();
        QueueRefresh();
    }

    private void ClearTargeting()
    {
        _targetingCardId = null;
        _targetsNeeded = 0;
        _chosenTargets.Clear();
    }

    /// <summary>The overlay at the end of a run: defeat, or the voyage complete after the last fight.</summary>
    private void ShowResult(RunSession session, RunState run)
    {
        _resultOverlay.Visible = (run.Phase is RunPhase.Lost or RunPhase.Won) && !_resultHeld;
        if (!_resultOverlay.Visible || run.LastFight is not { } last)
        {
            return;
        }

        string enemy = session.Content.Enemy(last.EnemyId).Name;
        string turns = last.Turns == 1 ? "1 turn" : $"{last.Turns} turns";
        if (run.Phase == RunPhase.Won)
        {
            _resultTitle.Text = "Voyage complete";
            _resultMessage.Text = $"The {enemy} powers down and drifts away.\n" +
                                  $"{run.FightsWon} fights won, with {run.Hull} / {run.MaxHull} hull left.";
        }
        else
        {
            string won = run.FightsWon == 1 ? "1 fight" : $"{run.FightsWon} fights";
            _resultTitle.Text = "Defeat";
            _resultMessage.Text = $"The hull gives out against the {enemy} after {turns}.\n" +
                                  $"The voyage ends here, after {won} won.";
        }
    }

    private void StartNewRun()
    {
        ClearTargeting();
        GameRoot.Instance.StartNewRun();
    }
}
