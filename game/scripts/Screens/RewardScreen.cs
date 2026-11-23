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

    private readonly List<OptionCard> _offers = [];
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
            var card = new OptionCard(Palette.ForPiece(piece), CardText.PieceDetails(piece, session.Content), piece, OfferHeight, fontSize: 22);
            int index = i;
            card.Button.Pressed += () =>
            {
                _selected = index;
                ShowSelection();
            };
            _offers.Add(card);
            offers.AddChild(card.Button);
        }

        ShowSelection();
        GD.Print($"RewardScreen: offering {string.Join(", ", offer.Pieces)}.");
    }

    private void ShowSelection()
    {
        for (int i = 0; i < _offers.Count; i++)
        {
            _offers[i].SetSelected(i == _selected);
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
