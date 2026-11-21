using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// The combat screen. It draws the run and forwards every tap to <see cref="GameRoot.Act"/>, which saves
/// after it. Numbers that depend on rules (a card's damage after bonuses, whether it can be played, which
/// piece a heavy attack would hit) come from <see cref="CombatEngine"/>. After the fight it shows the
/// defeat or, after the last fight, the voyage complete; a fight won before that goes to the Reward screen.
/// </summary>
/// <remarks>
/// Picking pieces for a repair card is the only state kept here. It is not saved: quitting while
/// picking resumes as if the card had not been tapped.
/// </remarks>
public partial class CombatScreen : Control
{
    private const int HandGap = 8;
    private const float MinCardWidth = 88;
    private const float MaxCardWidth = 136;
    private const float CardHeight = 208;

    private readonly List<int> _chosenTargets = [];
    private int? _targetingCardId;
    private int _targetsNeeded;
    private bool _refreshQueued;

    private Label _enemyName = null!;
    private Label _turn = null!;
    private ProgressBar _enemyHullBar = null!;
    private Label _enemyHull = null!;
    private Label _enemyBlock = null!;
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
        _enemyName = GetNode<Label>("%EnemyName");
        _turn = GetNode<Label>("%TurnLabel");
        _enemyHullBar = GetNode<ProgressBar>("%EnemyHullBar");
        _enemyHull = GetNode<Label>("%EnemyHullLabel");
        _enemyBlock = GetNode<Label>("%EnemyBlockLabel");
        _intent = GetNode<Label>("%IntentLabel");
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

        _endTurn.Pressed += () => Act(s => s.Combat.EndTurn(s.State));
        GetNode<Button>("%CancelButton").Pressed += CancelTargeting;
        GetNode<Button>("%NewRunButton").Pressed += StartNewRun;
        _grid.PieceTapped += OnPieceTapped;
        _handScroll.Resized += FitCardWidths;
        GameRoot.Instance.Changed += QueueRefresh;

        Refresh();
        if (Session?.State is { Combat: { } combat } run)
        {
            GD.Print($"CombatScreen: turn {combat.Turn} against {_enemyName.Text}, {combat.Hand.Count} cards in hand, hull {run.Hull}/{run.MaxHull}.");
        }
    }

    public override void _ExitTree() => GameRoot.Instance.Changed -= QueueRefresh;

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
            _enemyName.Text = run.LastFight is { } last ? session.Content.Enemy(last.EnemyId).Name : "";
            _turn.Text = "";
            _enemyHullBar.Value = 0;
            _enemyHull.Text = "";
            _enemyBlock.Text = "";
            _intent.Text = "";
            return;
        }

        var enemy = combat.Enemy;
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

        if (combat is null)
        {
            return;
        }

        foreach (var card in combat.Hand)
        {
            _hand.AddChild(BuildCard(session, run, session.Combat.Preview(run, card)));
        }

        FitCardWidths();
    }

    private Button BuildCard(RunSession session, RunState run, CardPreview preview)
    {
        bool targetingThis = _targetingCardId == preview.Card.Id;
        bool enabled = preview.Playable && (_targetingCardId is null || targetingThis);
        var button = new Button
        {
            CustomMinimumSize = new Vector2(MaxCardWidth, CardHeight),
            Disabled = !enabled,
            ClipContents = true,
            FocusMode = FocusModeEnum.None,
            Modulate = targetingThis ? Palette.Selectable : enabled ? Colors.White : new Color(1, 1, 1, 0.4f),
        };

        var source = run.Ship.Find(preview.Card.SourcePieceId);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        box.AddChild(new ColorRect
        {
            Color = source is null ? Palette.Muted : Palette.ForPiece(session.Content.Piece(source.Piece.DefId)),
            CustomMinimumSize = new Vector2(0, 10),
            MouseFilter = MouseFilterEnum.Ignore,
        });
        box.AddChild(MakeLabel(preview.Def.Name, 24, Colors.White));
        box.AddChild(MakeLabel($"{preview.Def.Cost} energy", 20, Palette.Energy));

        var effects = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Text = string.Join("\n", preview.Effects.Zip(preview.Def.Effects, CardText.Describe)),
        };
        effects.AddThemeFontSizeOverride("normal_font_size", 20);
        box.AddChild(effects);

        button.AddChild(box);
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = 8;
        box.OffsetTop = 8;
        box.OffsetRight = -8;
        box.OffsetBottom = -8;

        // The spec: touching a card highlights the piece that adds it, because the ship is the deck.
        int pieceId = preview.Card.SourcePieceId;
        button.MouseEntered += () => _grid.HighlightPieceId = pieceId;
        button.MouseExited += () => _grid.HighlightPieceId = null;
        button.ButtonDown += () => _grid.HighlightPieceId = pieceId;
        button.ButtonUp += () => _grid.HighlightPieceId = null;
        button.Pressed += () => OnCardPressed(preview);
        return button;
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

    /// <summary>Cards share the hand's width, between 88 and 136 px each; past that the hand scrolls.</summary>
    private void FitCardWidths()
    {
        int count = _hand.GetChildCount();
        if (count == 0)
        {
            return;
        }

        float available = _handScroll.Size.X > 0 ? _handScroll.Size.X : MaxCardWidth * 5;
        float width = Mathf.Clamp((available - (HandGap * (count - 1))) / count, MinCardWidth, MaxCardWidth);
        foreach (var child in _hand.GetChildren())
        {
            if (child is Control card)
            {
                card.CustomMinimumSize = new Vector2(width, CardHeight);
            }
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
        _resultOverlay.Visible = run.Phase is RunPhase.Lost or RunPhase.Won;
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
