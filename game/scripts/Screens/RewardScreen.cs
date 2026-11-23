using Bloom.Core.Pieces;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Visuals;
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

    private readonly List<(Button Button, Color Accent, NinePatchRect? Frame)> _offers = [];
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
                $"Salvage +{offer.Salvage}  ·  {run.Salvage} in all  ·  Hull {run.Hull} / {run.MaxHull}";
        }

        if (offer.Pieces.Count == 0)
        {
            GetNode<Button>("%SkipButton").Text = "Continue";
        }

        GetNode<Control>("%CargoNote").Visible = run.Cargo.IsFull;
        var offers = GetNode<VBoxContainer>("%Offers");
        for (int i = 0; i < offer.Pieces.Count; i++)
        {
            var piece = session.Content.Piece(offer.Pieces[i]);
            var (button, frame) = BuildOffer(piece, i);
            _offers.Add((button, Palette.ForPiece(piece), frame));
            offers.AddChild(button);
        }

        ShowSelection();
        GD.Print($"RewardScreen: offering {string.Join(", ", offer.Pieces)}.");
    }

    private (Button Button, NinePatchRect? Frame) BuildOffer(PieceDef piece, int index)
    {
        var accent = Palette.ForPiece(piece);
        var button = new Button
        {
            CustomMinimumSize = new Vector2(0, OfferHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
            ClipContents = true,
        };
        var frame = BloomTheme.CreateFrameOverlay(ArtLibrary.PanelFrame, accent with { A = 0.65f });
        if (frame is not null)
        {
            button.AddChild(frame);
        }

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
        return (button, frame);
    }

    private void ShowSelection()
    {
        for (int i = 0; i < _offers.Count; i++)
        {
            bool selected = i == _selected;
            var (button, accent, frame) = _offers[i];
            var box = BloomTheme.OfferSurface(accent, selected);
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            {
                button.AddThemeStyleboxOverride(state, box);
            }

            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            if (frame is not null)
            {
                frame.Modulate = selected ? Palette.Selectable : accent with { A = 0.65f };
            }
        }

        _take.Disabled = _selected is null;
    }

    private void Take()
    {
        if (_selected is int index)
        {
            GameRoot.Instance.Act(s => s.Rewards.Take(s.State, index));
        }
    }
}
