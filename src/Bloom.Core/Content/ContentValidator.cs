using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Core.Run;

namespace Bloom.Core.Content;

/// <summary>
/// Checks content against the rules in the spec. JSON can leave any field out, so every field is
/// treated as possibly null here, whatever its declared type says.
/// </summary>
internal static class ContentValidator
{
    public static List<string> Validate(ContentDb db)
    {
        var errors = new List<string>();
        CheckIds(db.Pieces.Select(p => p?.Id), "piece", errors);
        CheckIds(db.Cards.Select(c => c?.Id), "card", errors);
        CheckIds(db.Enemies.Select(e => e?.Id), "enemy", errors);
        CheckIds(db.Ships.Select(s => s?.Id), "ship class", errors);
        CheckIds(db.Sections.Select(s => s?.Id), "hull section", errors);

        foreach (var piece in db.Pieces.Where(p => p is not null))
        {
            ValidatePiece(db, piece, errors);
        }

        foreach (var card in db.Cards.Where(c => c is not null))
        {
            ValidateCard(card, errors);
        }

        foreach (var enemy in db.Enemies.Where(e => e is not null))
        {
            ValidateEnemy(enemy, errors);
        }

        CheckEnemyPools(db, errors);

        foreach (var ship in db.Ships.Where(s => s is not null))
        {
            ValidateShip(db, ship, errors);
        }

        foreach (var section in db.Sections.Where(s => s is not null))
        {
            ValidateSection(section, errors);
        }

        if (db.Sections.Count < Rules.ShipyardChoices)
        {
            errors.Add($"There are {db.Sections.Count} hull sections, but a shipyard offers {Rules.ShipyardChoices}.");
        }

        // Fitting the starting pieces needs valid pieces and shapes, so it runs only on otherwise clean content.
        if (errors.Count == 0)
        {
            foreach (var ship in db.Ships)
            {
                try
                {
                    RunFactory.NewRun(db, ship.Id, seed: 0);
                }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException)
                {
                    errors.Add($"ship class '{ship.Id}': {e.Message}");
                }
            }
        }

        return errors;
    }

    private static void CheckIds(IEnumerable<string?> ids, string kind, List<string> errors)
    {
        var seen = new HashSet<string>();
        foreach (string? id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"A {kind} has no id.");
            }
            else if (!seen.Add(id))
            {
                errors.Add($"Duplicate {kind} id '{id}'.");
            }
        }
    }

    private static void ValidatePiece(ContentDb db, PieceDef piece, List<string> errors)
    {
        string where = $"piece '{piece.Id}'";
        if (string.IsNullOrWhiteSpace(piece.Name))
        {
            errors.Add($"{where}: missing name.");
        }

        if (!Enum.IsDefined(piece.Kind) || !Enum.IsDefined(piece.Rarity))
        {
            errors.Add($"{where}: unknown kind or rarity.");
        }

        if (piece.Tags is null)
        {
            errors.Add($"{where}: missing tags (use [] for none).");
        }

        if (piece.Shape is null)
        {
            errors.Add($"{where}: missing shape.");
        }
        else
        {
            if (!piece.Shape.IsConnected())
            {
                errors.Add($"{where}: the shape must be one group of cells joined by edges.");
            }

            var (min, max) = piece.Kind == PieceKind.Crew
                ? (Rules.MinCrewCells, Rules.MaxCrewCells)
                : (Rules.MinEquipmentCells, Rules.MaxEquipmentCells);
            if (piece.Shape.Count < min || piece.Shape.Count > max)
            {
                errors.Add($"{where}: {piece.Kind} must cover {min} to {max} cells, but the shape covers {piece.Shape.Count}.");
            }
        }

        if (piece.Cards is null || piece.Cards.Count == 0)
        {
            errors.Add($"{where}: must add at least one card.");
        }
        else
        {
            int total = piece.Cards.Sum(g => g?.Count ?? 0);
            if (total < Rules.MinCardsPerPiece || total > Rules.MaxCardsPerPiece)
            {
                errors.Add($"{where}: adds {total} cards; a piece adds {Rules.MinCardsPerPiece} to {Rules.MaxCardsPerPiece}.");
            }

            foreach (var grant in piece.Cards)
            {
                if (grant is null || grant.Count < 1)
                {
                    errors.Add($"{where}: each card entry needs a count of at least 1.");
                }
                else if (grant.Card is null || !db.HasCard(grant.Card))
                {
                    errors.Add($"{where}: unknown card '{grant.Card}'.");
                }
            }
        }

        if (piece.Bonus is { } bonus)
        {
            if (string.IsNullOrWhiteSpace(bonus.Name))
            {
                errors.Add($"{where}: the adjacency bonus needs a name.");
            }

            if (bonus.Effect is null)
            {
                errors.Add($"{where}: the adjacency bonus needs an effect.");
            }
        }
    }

    private static void ValidateCard(CardDef card, List<string> errors)
    {
        string where = $"card '{card.Id}'";
        if (string.IsNullOrWhiteSpace(card.Name))
        {
            errors.Add($"{where}: missing name.");
        }

        if (card.Cost < 0)
        {
            errors.Add($"{where}: cost cannot be negative.");
        }

        if (card.Effects is null || card.Effects.Count == 0)
        {
            errors.Add($"{where}: needs at least one effect.");
        }
        else
        {
            ValidateCardEffects(where, card.Effects, errors);
        }

        if (card.Upgrade is { } upgrade)
        {
            if (upgrade.Cost is null && upgrade.Effects is null)
            {
                errors.Add($"{where}: the upgrade must give a cost, effects or both.");
            }

            if (upgrade.Cost < 0)
            {
                errors.Add($"{where}: the upgraded cost cannot be negative.");
            }

            if (upgrade.Effects is { Count: 0 })
            {
                errors.Add($"{where}: the upgrade's effects cannot be empty; leave them out to keep the card's own.");
            }
            else if (upgrade.Effects is { } effects)
            {
                ValidateCardEffects($"{where} (upgraded)", effects, errors);
            }
        }
    }

    private static void ValidateCardEffects(string where, IReadOnlyList<Effect> effects, List<string> errors)
    {
        foreach (var effect in effects)
        {
            if (effect is null)
            {
                errors.Add($"{where}: an effect is empty.");
            }
            else if (effect is BonusEffect)
            {
                errors.Add($"{where}: '{effect.GetType().Name}' can only be used in an adjacency bonus.");
            }
            else if (Magnitude(effect) < 0)
            {
                errors.Add($"{where}: '{effect.GetType().Name}' cannot have a negative amount.");
            }
        }
    }

    /// <summary>Every sector needs an enemy in each pool, since its map can hold every kind of fight.</summary>
    private static void CheckEnemyPools(ContentDb db, List<string> errors)
    {
        for (int sector = 1; sector <= Rules.SectorsPerRun; sector++)
        {
            foreach (var pool in Enum.GetValues<EnemyPool>())
            {
                if (!db.Enemies.Any(e => e?.Pool == pool && e.Sector == sector))
                {
                    errors.Add($"Sector {sector} has no enemy in the {pool} pool.");
                }
            }
        }
    }

    private static void ValidateEnemy(EnemyDef enemy, List<string> errors)
    {
        string where = $"enemy '{enemy.Id}'";
        if (enemy.Hull <= 0)
        {
            errors.Add($"{where}: hull must be above 0.");
        }

        if (enemy.Pool is null || !Enum.IsDefined(enemy.Pool.Value))
        {
            errors.Add($"{where}: needs a pool: {string.Join(", ", Enum.GetNames<EnemyPool>())}.");
        }

        if (enemy.Sector < 1 || enemy.Sector > Rules.SectorsPerRun)
        {
            errors.Add($"{where}: sector {enemy.Sector} is outside 1 to {Rules.SectorsPerRun}.");
        }

        if (enemy.Pattern is null || enemy.Pattern.Count == 0)
        {
            errors.Add($"{where}: needs at least one intent in its pattern.");
            return;
        }

        foreach (var intent in enemy.Pattern)
        {
            if (intent is null)
            {
                errors.Add($"{where}: an intent is empty.");
                continue;
            }

            if (intent.Amount < 0)
            {
                errors.Add($"{where}: intent amounts cannot be negative.");
            }

            if (intent.Kind == IntentKind.HeavyAttack && intent.Lane is null)
            {
                errors.Add($"{where}: a heavy attack must name a lane.");
            }
            else if (intent.Kind != IntentKind.HeavyAttack && intent.Lane is not null)
            {
                errors.Add($"{where}: only heavy attacks name a lane.");
            }

            if (intent.Lane is int lane && (lane < 0 || lane >= Rules.GridColumns))
            {
                errors.Add($"{where}: lane {lane} is outside 0 to {Rules.GridColumns - 1}.");
            }
        }
    }

    private static void ValidateShip(ContentDb db, ShipClassDef ship, List<string> errors)
    {
        string where = $"ship class '{ship.Id}'";
        if (string.IsNullOrWhiteSpace(ship.Name))
        {
            errors.Add($"{where}: missing name.");
        }

        if (ship.Hull <= 0)
        {
            errors.Add($"{where}: hull must be above 0.");
        }

        if (ship.HullShape is null)
        {
            errors.Add($"{where}: missing hullShape.");
        }
        else
        {
            if (!ship.HullShape.IsConnected())
            {
                errors.Add($"{where}: the hull must be one group of cells joined by edges.");
            }

            if (ship.HullShape.CellsAt(ship.HullOrigin, Rotation.R0).Any(c => !ShipGrid.InBounds(c)))
            {
                errors.Add($"{where}: the hull does not fit inside the {Rules.GridColumns}x{Rules.GridRows} grid at {ship.HullOrigin}.");
            }
        }

        if (ship.StartingPieces is null || ship.StartingPieces.Count == 0)
        {
            errors.Add($"{where}: needs starting pieces.");
            return;
        }

        foreach (var start in ship.StartingPieces)
        {
            if (start?.Piece is null || !db.HasPiece(start.Piece))
            {
                errors.Add($"{where}: unknown starting piece '{start?.Piece}'.");
            }
        }

        int bridges = ship.StartingPieces.Count(s => s?.Piece is not null && db.HasPiece(s.Piece) && db.Piece(s.Piece).IsBridge);
        if (bridges != 1)
        {
            errors.Add($"{where}: must start with exactly one bridge (a piece tagged Core), but has {bridges}.");
        }
    }

    private static void ValidateSection(HullSectionDef section, List<string> errors)
    {
        string where = $"hull section '{section.Id}'";
        if (string.IsNullOrWhiteSpace(section.Name))
        {
            errors.Add($"{where}: missing name.");
        }

        if (section.Shape is null)
        {
            errors.Add($"{where}: missing shape.");
            return;
        }

        if (!section.Shape.IsConnected())
        {
            errors.Add($"{where}: the shape must be one group of cells joined by edges.");
        }

        if (section.Shape.Count < Rules.MinSectionCells || section.Shape.Count > Rules.MaxSectionCells)
        {
            errors.Add($"{where}: a section covers {Rules.MinSectionCells} to {Rules.MaxSectionCells} cells, but the shape covers {section.Shape.Count}.");
        }
    }

    private static int Magnitude(Effect effect) => effect switch
    {
        DamageEffect e => e.Amount,
        BlockEffect e => e.Amount,
        GainEnergyEffect e => e.Amount,
        DrawEffect e => e.Count,
        RepairEffect e => e.Count,
        FocusEffect e => e.Amount,
        GrowEffect e => e.Amount,
        _ => 0,
    };
}
