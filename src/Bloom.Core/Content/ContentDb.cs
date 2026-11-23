using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bloom.Core.Cards;
using Bloom.Core.Combat;
using Bloom.Core.Pieces;
using Bloom.Core.Save;

namespace Bloom.Core.Content;

public sealed class ContentException(IReadOnlyList<string> errors)
    : Exception("Content is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// All game content: pieces, cards, enemies, ship classes and hull sections. It knows nothing about files; the caller
/// passes the JSON text, so the Godot layer and the tests can each read the files their own way.
/// Content is always validated on load.
/// </summary>
public sealed class ContentDb
{
    private readonly Dictionary<string, PieceDef> _pieces = [];
    private readonly Dictionary<string, CardDef> _cards = [];
    private readonly Dictionary<string, CardDef> _upgradedCards = [];
    private readonly Dictionary<string, EnemyDef> _enemies = [];
    private readonly Dictionary<string, ShipClassDef> _ships = [];
    private readonly Dictionary<string, HullSectionDef> _sections = [];

    private ContentDb(
        IReadOnlyList<PieceDef> pieces,
        IReadOnlyList<CardDef> cards,
        IReadOnlyList<EnemyDef> enemies,
        IReadOnlyList<ShipClassDef> ships,
        IReadOnlyList<HullSectionDef> sections)
    {
        Pieces = pieces;
        Cards = cards;
        Enemies = enemies;
        Ships = ships;
        Sections = sections;

        // First definition wins; the validator reports duplicates.
        foreach (var p in pieces.Where(p => p?.Id is not null))
        {
            _pieces.TryAdd(p.Id, p);
        }

        foreach (var c in cards.Where(c => c?.Id is not null))
        {
            if (_cards.TryAdd(c.Id, c))
            {
                _upgradedCards.Add(c.Id, c.Upgraded());
            }
        }

        foreach (var e in enemies.Where(e => e?.Id is not null))
        {
            _enemies.TryAdd(e.Id, e);
        }

        foreach (var s in ships.Where(s => s?.Id is not null))
        {
            _ships.TryAdd(s.Id, s);
        }

        foreach (var s in sections.Where(s => s?.Id is not null))
        {
            _sections.TryAdd(s.Id, s);
        }
    }

    public IReadOnlyList<PieceDef> Pieces { get; }

    public IReadOnlyList<CardDef> Cards { get; }

    public IReadOnlyList<EnemyDef> Enemies { get; }

    public IReadOnlyList<ShipClassDef> Ships { get; }

    public IReadOnlyList<HullSectionDef> Sections { get; }

    public static ContentDb Create(
        IReadOnlyList<PieceDef> pieces,
        IReadOnlyList<CardDef> cards,
        IReadOnlyList<EnemyDef> enemies,
        IReadOnlyList<ShipClassDef> ships,
        IReadOnlyList<HullSectionDef> sections)
    {
        var db = new ContentDb(pieces, cards, enemies, ships, sections);
        var errors = ContentValidator.Validate(db);
        return errors.Count == 0 ? db : throw new ContentException(errors);
    }

    public static ContentDb FromJson(string piecesJson, string cardsJson, string enemiesJson, string shipsJson, string sectionsJson)
    {
        var errors = new List<string>();
        var pieces = Parse(piecesJson, BloomJsonContext.Default.ListPieceDef, "pieces.json", errors);
        var cards = Parse(cardsJson, BloomJsonContext.Default.ListCardDef, "cards.json", errors);
        var enemies = Parse(enemiesJson, BloomJsonContext.Default.ListEnemyDef, "enemies.json", errors);
        var ships = Parse(shipsJson, BloomJsonContext.Default.ListShipClassDef, "ships.json", errors);
        var sections = Parse(sectionsJson, BloomJsonContext.Default.ListHullSectionDef, "sections.json", errors);
        return errors.Count == 0 ? Create(pieces, cards, enemies, ships, sections) : throw new ContentException(errors);
    }

    public PieceDef Piece(string id) => _pieces.TryGetValue(id, out var def) ? def : throw Unknown("piece", id);

    public CardDef Card(string id) => _cards.TryGetValue(id, out var def) ? def : throw Unknown("card", id);

    /// <summary>A card as it plays: its upgrade applied when <paramref name="upgraded"/> (see <see cref="CardDef.Upgraded"/>).</summary>
    public CardDef Card(string id, bool upgraded) =>
        upgraded ? _upgradedCards.TryGetValue(id, out var def) ? def : throw Unknown("card", id) : Card(id);

    public CardDef Card(CardInstance card) => Card(card.CardId, card.Upgraded);

    public EnemyDef Enemy(string id) => _enemies.TryGetValue(id, out var def) ? def : throw Unknown("enemy", id);

    public ShipClassDef Ship(string id) => _ships.TryGetValue(id, out var def) ? def : throw Unknown("ship class", id);

    public HullSectionDef Section(string id) => _sections.TryGetValue(id, out var def) ? def : throw Unknown("hull section", id);

    public bool HasPiece(string id) => _pieces.ContainsKey(id);

    public bool HasCard(string id) => _cards.ContainsKey(id);

    private static List<T> Parse<T>(string json, JsonTypeInfo<List<T>> typeInfo, string fileName, List<string> errors)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo) ?? [];
        }
        catch (JsonException e)
        {
            errors.Add($"{fileName}: {e.Message}");
            return [];
        }
        catch (NotSupportedException e)
        {
            // System.Text.Json throws this, not JsonException, when an effect's "type" is not its first property.
            errors.Add($"{fileName}: {e.Message} Check that \"type\" is the first property of every effect.");
            return [];
        }
    }

    private static KeyNotFoundException Unknown(string kind, string id) => new($"Unknown {kind} '{id}'.");
}
