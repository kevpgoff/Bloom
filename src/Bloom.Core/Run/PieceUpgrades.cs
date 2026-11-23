using Bloom.Core.Content;
using Bloom.Core.Pieces;

namespace Bloom.Core.Run;

/// <summary>
/// Upgrading pieces. The spec: each piece can be upgraded once, at a grove or through an event, and the
/// upgrade improves every card the piece adds. How each card improves is written in cards.json
/// (<see cref="Cards.CardDef.Upgrade"/>).
/// </summary>
public static class PieceUpgrades
{
    /// <summary>Whether a piece can still be upgraded: it is not upgraded yet, and at least one of its cards has an upgrade.</summary>
    public static bool CanUpgrade(ContentDb content, PieceInstance piece) =>
        !piece.Upgraded && content.Piece(piece.DefId).Cards.Any(g => content.Card(g.Card).Upgrade is not null);

    /// <summary>The owned pieces that can be upgraded: the fitted ones in layout order, then the cargo hold's.</summary>
    public static IReadOnlyList<PieceInstance> Upgradable(ContentDb content, RunState run) =>
        run.Ship.Fitted.Select(f => f.Piece).Concat(run.Cargo.Pieces).Where(p => CanUpgrade(content, p)).ToList();

    /// <summary>Upgrades a fitted or held piece in place. Throws, changing nothing, if it is not owned or cannot be upgraded.</summary>
    internal static void Upgrade(ContentDb content, RunState run, int pieceId)
    {
        int fitted = run.Ship.Fitted.FindIndex(f => f.Piece.Id == pieceId);
        int held = run.Cargo.Pieces.FindIndex(p => p.Id == pieceId);
        var piece = fitted >= 0 ? run.Ship.Fitted[fitted].Piece
            : held >= 0 ? run.Cargo.Pieces[held]
            : throw new InvalidOperationException($"Piece {pieceId} is neither fitted nor in the cargo hold.");
        if (!CanUpgrade(content, piece))
        {
            throw new InvalidOperationException(piece.Upgraded
                ? $"'{piece.DefId}' is already upgraded."
                : $"'{piece.DefId}' has no card that can be upgraded.");
        }

        var upgraded = piece with { Upgraded = true };
        if (fitted >= 0)
        {
            run.Ship.Fitted[fitted] = run.Ship.Fitted[fitted] with { Piece = upgraded };
        }
        else
        {
            run.Cargo.Pieces[held] = upgraded;
        }
    }
}
