using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// After a won fight: the offered pieces, of which the player takes one or none. Tapping an offer selects
/// it; Take or Skip go through <see cref="GameRoot.Act"/>. The choice stays on screen until then, so
/// quitting here resumes with the same offer.
/// </summary>
public partial class RewardScreen : Control
{
    private const float OfferHeight = 228;

    private readonly List<Button> _offerButtons = [];
    private int? _selected;
    private Button _take = null!;

    private static RunSession? Session => GameRoot.Instance.Session;

    public override void _Ready()
    {
        _take = GetNode<Button>("%TakeButton");
        _take.Pressed += Take;
        GetNode<Button>("%SkipButton").Pressed += () => GameRoot.Instance.Act(s => s.Rewards.Skip(s.State));

        if (Session is not { State: { Reward: { } offer } run } session)
        {
            return;
        }

        if (run.LastFight is { } last)
        {
            string turns = last.Turns == 1 ? "1 turn" : $"{last.Turns} turns";
            GetNode<Label>("%Message").Text =
                $"The {session.Content.Enemy(last.EnemyId).Name} powers down and drifts away. Won in {turns}.\n" +
                $"Hull {run.Hull} / {run.MaxHull}  ·  {run.FightsWon} of {run.Encounters.Count} fights won";
        }

        GetNode<Control>("%CargoNote").Visible = run.Cargo.IsFull;
        var offers = GetNode<VBoxContainer>("%Offers");
        for (int i = 0; i < offer.Pieces.Count; i++)
        {
            var button = BuildOffer(session.Content.Piece(offer.Pieces[i]), i);
            _offerButtons.Add(button);
            offers.AddChild(button);
        }

        ShowSelection();
        GD.Print($"RewardScreen: offering {string.Join(", ", offer.Pieces)}.");
    }

    private Button BuildOffer(PieceDef piece, int index)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(0, OfferHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
            ClipContents = true,
        };

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (string side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        }

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(new ShapeView
        {
            Piece = piece,
            CustomMinimumSize = new Vector2(150, 150),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
            Text = CardText.PieceDetails(piece, Session!.Content),
        };
        text.AddThemeFontSizeOverride("normal_font_size", 22);
        text.AddThemeFontSizeOverride("bold_font_size", 30);
        row.AddChild(text);

        margin.AddChild(row);
        button.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        button.Pressed += () =>
        {
            _selected = index;
            ShowSelection();
        };
        return button;
    }

    private void ShowSelection()
    {
        for (int i = 0; i < _offerButtons.Count; i++)
        {
            var box = OfferStyle(i == _selected);
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            {
                _offerButtons[i].AddThemeStyleboxOverride(state, box);
            }

            _offerButtons[i].AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        }

        _take.Disabled = _selected is null;
    }

    private static StyleBoxFlat OfferStyle(bool selected)
    {
        var box = new StyleBoxFlat
        {
            BgColor = Palette.LaneOdd,
            BorderColor = selected ? Palette.Selectable : Palette.Hull,
        };
        box.SetBorderWidthAll(selected ? 6 : 2);
        box.SetCornerRadiusAll(8);
        return box;
    }

    private void Take()
    {
        if (_selected is int index)
        {
            GameRoot.Instance.Act(s => s.Rewards.Take(s.State, index));
        }
    }
}
