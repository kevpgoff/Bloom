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
        CheckIds(db.Events.Select(e => e?.Id), "event", errors);

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

        foreach (var ev in db.Events.Where(e => e is not null))
        {
            ValidateEvent(db, ev, errors);
        }

        for (int sector = 1; sector <= Rules.SectorsPerRun; sector++)
        {
            if (!db.Events.Any(e => e?.Sector == sector))
            {
                errors.Add($"Sector {sector} has no event.");
            }
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

        ValidatePattern(where, enemy.Pattern, errors);
        if (enemy.Parts is not { } parts)
        {
            return;
        }

        if (parts.Count > Rules.MaxEnemyParts)
        {
            errors.Add($"{where}: has {parts.Count} parts, but at most {Rules.MaxEnemyParts} fit on the screen.");
        }

        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] is not { } part)
            {
                errors.Add($"{where}: part {i} is empty.");
                continue;
            }

            string at = $"{where}, part {i}";
            if (string.IsNullOrWhiteSpace(part.Name))
            {
                errors.Add($"{at}: missing name.");
            }
            else if (parts.Take(i).Any(p => p?.Name == part.Name))
            {
                errors.Add($"{at}: another part is already named '{part.Name}'.");
            }

            if (part.Hull <= 0)
            {
                errors.Add($"{at}: hull must be above 0.");
            }

            ValidatePattern(at, part.Pattern, errors);
        }
    }

    /// <summary>A pattern of a body or a part: at least one intent, each with a lane exactly when it is a heavy attack.</summary>
    private static void ValidatePattern(string where, IReadOnlyList<Intent>? pattern, List<string> errors)
    {
        if (pattern is null || pattern.Count == 0)
        {
            errors.Add($"{where}: needs at least one intent in its pattern.");
            return;
        }

        foreach (var intent in pattern)
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

    private static void ValidateEvent(ContentDb db, EventDef ev, List<string> errors)
    {
        string where = $"event '{ev.Id}'";
        if (string.IsNullOrWhiteSpace(ev.Name))
        {
            errors.Add($"{where}: missing name.");
        }

        if (string.IsNullOrWhiteSpace(ev.Text))
        {
            errors.Add($"{where}: missing text.");
        }

        if (ev.Sector < 1 || ev.Sector > Rules.SectorsPerRun)
        {
            errors.Add($"{where}: sector {ev.Sector} is outside 1 to {Rules.SectorsPerRun}.");
        }

        if (ev.Choices is null || ev.Choices.Count < Rules.MinEventChoices || ev.Choices.Count > Rules.MaxEventChoices)
        {
            errors.Add($"{where}: an event has {Rules.MinEventChoices} to {Rules.MaxEventChoices} choices, but this one has {ev.Choices?.Count ?? 0}.");
            return;
        }

        if (!ev.Choices.Any(c => c is not null && c.Cost == 0 && c.NeedsTag is null))
        {
            errors.Add($"{where}: needs a choice with no cost and no tag, so that one is always open.");
        }

        for (int i = 0; i < ev.Choices.Count; i++)
        {
            var choice = ev.Choices[i];
            string at = $"{where}, choice {i + 1}";
            if (choice is null)
            {
                errors.Add($"{at}: the choice is empty.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(choice.Label))
            {
                errors.Add($"{at}: missing label.");
            }

            if (choice.Cost < 0)
            {
                errors.Add($"{at}: the cost cannot be negative.");
            }

            if (choice.NeedsTag is { } tag && !Enum.IsDefined(tag))
            {
                errors.Add($"{at}: unknown tag '{tag}'.");
            }

            if (choice.Results is null || choice.Results.Count == 0)
            {
                errors.Add($"{at}: needs at least one result.");
                continue;
            }

            foreach (var result in choice.Results)
            {
                ValidateEventResult(db, ev, at, result, errors);
            }
        }
    }

    private static void ValidateEventResult(ContentDb db, EventDef ev, string at, EventResult? result, List<string> errors)
    {
        if (result is null)
        {
            errors.Add($"{at}: a result is empty.");
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            errors.Add($"{at}: a result needs text.");
        }

        if (result.Weight < 1)
        {
            errors.Add($"{at}: a result's weight must be at least 1.");
        }

        var outcomes = result.AllOutcomes;
        foreach (var outcome in outcomes)
        {
            switch (outcome)
            {
                case null:
                    errors.Add($"{at}: an outcome is empty.");
                    break;
                case SalvageOutcome { Amount: 0 } or HullOutcome { Amount: 0 } or MaxHullOutcome { Amount: 0 }:
                    errors.Add($"{at}: '{outcome.GetType().Name}' needs an amount other than 0.");
                    break;
                case PieceOutcome piece:
                    ValidatePieceOutcome(db, at, piece, errors);
                    break;
                case FightOutcome fight:
                    ValidateFightOutcome(db, ev, at, fight, errors);
                    break;
            }
        }

        int fights = outcomes.Count(o => o is FightOutcome);
        if (fights > 1)
        {
            errors.Add($"{at}: a result can start only one fight.");
        }

        if (fights > 0 && outcomes.Any(o => o is SectionOutcome))
        {
            errors.Add($"{at}: a result cannot both start a fight and give a hull section.");
        }
    }

    private static void ValidateFightOutcome(ContentDb db, EventDef ev, string at, FightOutcome fight, List<string> errors)
    {
        var enemy = db.Enemies.FirstOrDefault(e => e?.Id is not null && e.Id == fight.Enemy);
        if (enemy is null)
        {
            errors.Add($"{at}: unknown enemy '{fight.Enemy}'.");
        }
        else if (enemy.Sector != ev.Sector)
        {
            errors.Add($"{at}: enemy '{fight.Enemy}' belongs to sector {enemy.Sector}, not the event's sector {ev.Sector}.");
        }
        else if (enemy.Pool == EnemyPool.Boss)
        {
            errors.Add($"{at}: enemy '{fight.Enemy}' is a boss, and an event cannot start a boss fight.");
        }
    }

    private static void ValidatePieceOutcome(ContentDb db, string at, PieceOutcome piece, List<string> errors)
    {
        if (piece.Id is not null)
        {
            if (!db.HasPiece(piece.Id))
            {
                errors.Add($"{at}: unknown piece '{piece.Id}'.");
            }
            else if (db.Piece(piece.Id).IsBridge)
            {
                errors.Add($"{at}: an event cannot give the bridge.");
            }

            if (piece.Rarity is not null || piece.Kind is not null)
            {
                errors.Add($"{at}: a piece outcome names a piece or filters by rarity and kind, not both.");
            }
        }
        else if (!EventEngine.PiecesFor(db, piece).Any())
        {
            errors.Add($"{at}: no piece matches rarity {piece.Rarity?.ToString() ?? "any"} and kind {piece.Kind?.ToString() ?? "any"}.");
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
