using System.Reflection;
using Bloom.Core.Combat;
using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Map;
using Bloom.Core.Pieces;
using Bloom.Core.Run;

namespace Bloom.Core.Tests;

/// <summary>
/// Small fixed content for rule tests, so they don't break when the shipped data in game/data is
/// rebalanced. The test ship ("test") is a 5x5 hull at the top-left of the grid (x 0-4, y 0-4) with the
/// bridge on cells (0,4) and (1,4). A second test ship ("solar") is a 6x3 hull at (0,2) with the
/// <c>keepEnergy</c> rule and the bridge on cells (0,4) and (1,4). The enemies "heavy" and "mixed" make
/// heavy attacks on lane 0.
/// All pieces are common except "drone" (uncommon) and "spear" (rare), for reward tests.
/// No piece adds the card "chorus" (draw 2); tests put it in the hand themselves, so the reward pool stays the same.
/// Enemy pools: "dummy" and "blocker" are early, "heavy" and "mixed" late, "brute" hard and "titan" the boss.
/// The titan has 3 parts: an arm that attacks for 3, a shell that blocks 4, and a ram that alternates a heavy
/// attack of 4 on lane 0 with an attack of 2.
/// Upgrades: "ram" deals 8, "lance" costs 1, "sprout" blocks 6 and grows 3, "patch" costs 0; the other cards
/// have none, so "plating" (deflect) and "singer" (brace) cannot be upgraded.
/// Hull sections: "rib" (line of 3), "pod" (square of 4), "crook" (L of 4) and "keel" (line of 5). The test hull
/// leaves column 5 and rows 5 to 7 free for them.
/// Events (all sector 1; choice 0 first): "cache" (+20 salvage; 30 salvage for the spear; leave), "storm" (−100 hull;
/// +15 hull; leave), "graft" (+6 max hull; −10 max hull; leave), "gift" (the uncommon piece, so the drone; a random
/// crew; an upgrade; leave), "sprout" (a hull section; leave), "gamble" (3 in 4 +50 salvage, 1 in 4 a fight with the
/// brute; a fight with the dummy; leave) and "signal" (+10 salvage, needs a Drone piece; leave).
/// A new run gets a generated map; <see cref="UseTinyMap"/> swaps in a short fixed one.
/// </summary>
internal static class TestContent
{
    private const string Pieces = """
        [
          { "id": "bridge", "name": "Bridge", "kind": "Equipment", "tags": ["Core"], "rarity": "Common",
            "shape": ["##"], "cards": [{ "card": "ram", "count": 2 }, { "card": "brace", "count": 2 }] },
          { "id": "lance", "name": "Lance", "kind": "Equipment", "tags": ["Weapon"], "rarity": "Common",
            "shape": ["###"], "cards": [{ "card": "lance", "count": 2 }] },
          { "id": "plating", "name": "Plating", "kind": "Equipment", "tags": ["Shield"], "rarity": "Common",
            "shape": ["###"], "cards": [{ "card": "deflect", "count": 2 }] },
          { "id": "grinder", "name": "Grinder", "kind": "Crew", "tags": [], "rarity": "Common",
            "shape": ["##"], "cards": [{ "card": "focus", "count": 1 }],
            "bonus": { "name": "lens_focus", "targetTag": "Weapon", "effect": { "type": "damageBonus", "amount": 2 } } },
          { "id": "sail", "name": "Sail", "kind": "Equipment", "tags": ["Solar"], "rarity": "Common",
            "shape": ["###", ".#."], "cards": [{ "card": "unfurl", "count": 1 }] },
          { "id": "garden", "name": "Garden", "kind": "Equipment", "tags": ["Garden"], "rarity": "Common",
            "shape": ["##"], "cards": [{ "card": "sprout", "count": 2 }] },
          { "id": "singer", "name": "Singer", "kind": "Crew", "tags": [], "rarity": "Common",
            "shape": ["#"], "cards": [{ "card": "brace", "count": 1 }],
            "bonus": { "name": "spore_song", "targetTag": "Garden", "effect": { "type": "startGrown", "times": 1 } } },
          { "id": "tinker", "name": "Tinker", "kind": "Crew", "tags": [], "rarity": "Common",
            "shape": ["#"], "cards": [{ "card": "patch", "count": 1 }] },
          { "id": "fixer", "name": "Fixer", "kind": "Crew", "tags": [], "rarity": "Common",
            "shape": ["#"], "cards": [{ "card": "overhaul", "count": 1 }] },
          { "id": "drone", "name": "Drone", "kind": "Equipment", "tags": ["Drone"], "rarity": "Uncommon",
            "shape": ["#.", "##"], "cards": [{ "card": "ram", "count": 1 }] },
          { "id": "spear", "name": "Spear", "kind": "Equipment", "tags": ["Weapon"], "rarity": "Rare",
            "shape": ["####"], "cards": [{ "card": "lance", "count": 1 }] }
        ]
        """;

    private const string Cards = """
        [
          { "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "type": "damage", "amount": 5 }],
            "upgrade": { "effects": [{ "type": "damage", "amount": 8 }] } },
          { "id": "brace", "name": "Brace", "cost": 1, "effects": [{ "type": "block", "amount": 5 }] },
          { "id": "lance", "name": "Lance", "cost": 2, "effects": [{ "type": "damage", "amount": 12 }], "upgrade": { "cost": 1 } },
          { "id": "deflect", "name": "Deflect", "cost": 1, "effects": [{ "type": "block", "amount": 8 }] },
          { "id": "focus", "name": "Focus", "cost": 1, "effects": [{ "type": "focus", "amount": 4 }] },
          { "id": "unfurl", "name": "Unfurl", "cost": 0, "effects": [{ "type": "gainEnergy", "amount": 2 }] },
          { "id": "sprout", "name": "Sprout", "cost": 1, "effects": [{ "type": "block", "amount": 4 }, { "type": "grow", "amount": 2 }],
            "upgrade": { "effects": [{ "type": "block", "amount": 6 }, { "type": "grow", "amount": 3 }] } },
          { "id": "patch", "name": "Patch", "cost": 1, "effects": [{ "type": "repair", "count": 1 }], "upgrade": { "cost": 0 } },
          { "id": "overhaul", "name": "Overhaul", "cost": 0, "effects": [{ "type": "repair", "count": 2 }] },
          { "id": "chorus", "name": "Chorus", "cost": 1, "effects": [{ "type": "draw", "count": 2 }] }
        ]
        """;

    private const string Enemies = """
        [
          { "id": "dummy", "name": "Dummy", "pool": "Early", "hull": 30, "pattern": [{ "kind": "Attack", "amount": 6 }] },
          { "id": "heavy", "name": "Heavy", "pool": "Late", "hull": 100, "pattern": [{ "kind": "HeavyAttack", "amount": 10, "lane": 0 }] },
          { "id": "blocker", "name": "Blocker", "pool": "Early", "hull": 30, "pattern": [{ "kind": "Block", "amount": 5 }] },
          { "id": "mixed", "name": "Mixed", "pool": "Late", "hull": 40, "pattern": [
            { "kind": "Attack", "amount": 6 }, { "kind": "HeavyAttack", "amount": 20, "lane": 0 }, { "kind": "Block", "amount": 5 }] },
          { "id": "brute", "name": "Brute", "pool": "Hard", "hull": 45, "pattern": [{ "kind": "Attack", "amount": 7 }] },
          { "id": "titan", "name": "Titan", "pool": "Boss", "hull": 40, "pattern": [{ "kind": "Attack", "amount": 5 }], "parts": [
            { "name": "Arm", "hull": 10, "pattern": [{ "kind": "Attack", "amount": 3 }] },
            { "name": "Shell", "hull": 8, "pattern": [{ "kind": "Block", "amount": 4 }] },
            { "name": "Ram", "hull": 12, "pattern": [{ "kind": "HeavyAttack", "amount": 4, "lane": 0 }, { "kind": "Attack", "amount": 2 }] }] }
        ]
        """;

    private const string Ships = """
        [
          { "id": "test", "name": "Test", "hull": 60,
            "hullShape": ["#####", "#####", "#####", "#####", "#####"], "hullOrigin": { "x": 0, "y": 0 },
            "loadouts": [
              { "id": "a", "name": "Bare", "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 4 } }] },
              { "id": "armed", "name": "Armed", "startingPieces": [
                { "piece": "bridge", "origin": { "x": 0, "y": 4 } },
                { "piece": "lance", "origin": { "x": 0, "y": 0 } }] }] },
          { "id": "solar", "name": "Solar", "hull": 60,
            "hullShape": ["######", "######", "######"], "hullOrigin": { "x": 0, "y": 2 },
            "rule": { "type": "keepEnergy" }, "unlock": { "type": "winWith", "ship": "test" },
            "loadouts": [{ "id": "a", "name": "Bare", "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 4 } }] }] }
        ]
        """;

    /// <summary>Hull sections as JSON, for tests that build their own content.</summary>
    public const string SectionsJson = """
        [
          { "id": "rib", "name": "Rib", "shape": ["###"] },
          { "id": "pod", "name": "Pod", "shape": ["##", "##"] },
          { "id": "crook", "name": "Crook", "shape": ["#..", "###"] },
          { "id": "keel", "name": "Keel", "shape": ["#####"] }
        ]
        """;

    /// <summary>Events as JSON, for tests that build their own content from the test pieces and enemies.</summary>
    public const string EventsJson = """
        [
          { "id": "cache", "name": "Cache", "text": "A cache.", "choices": [
            { "label": "Open it", "results": [{ "text": "Scrap.", "outcomes": [{ "type": "salvage", "amount": 20 }] }] },
            { "label": "Buy the spear", "cost": 30, "results": [{ "text": "A spear.", "outcomes": [{ "type": "piece", "id": "spear" }] }] },
            { "label": "Leave", "results": [{ "text": "Gone." }] } ] },
          { "id": "storm", "name": "Storm", "text": "A storm.", "choices": [
            { "label": "Ride it", "results": [{ "text": "Ouch.", "outcomes": [{ "type": "hull", "amount": -100 }] }] },
            { "label": "Shelter", "results": [{ "text": "Rest.", "outcomes": [{ "type": "hull", "amount": 15 }] }] },
            { "label": "Leave", "results": [{ "text": "Gone." }] } ] },
          { "id": "graft", "name": "Graft", "text": "A graft.", "choices": [
            { "label": "Graft", "results": [{ "text": "Bigger.", "outcomes": [{ "type": "maxHull", "amount": 6 }] }] },
            { "label": "Prune", "results": [{ "text": "Smaller.", "outcomes": [{ "type": "maxHull", "amount": -10 }] }] },
            { "label": "Leave", "results": [{ "text": "Gone." }] } ] },
          { "id": "gift", "name": "Gift", "text": "A gift.", "choices": [
            { "label": "Uncommon", "results": [{ "text": "Rare-ish.", "outcomes": [{ "type": "piece", "rarity": "Uncommon" }] }] },
            { "label": "Crew", "results": [{ "text": "Hello.", "outcomes": [{ "type": "piece", "kind": "Crew" }] }] },
            { "label": "Upgrade", "results": [{ "text": "Better.", "outcomes": [{ "type": "upgrade" }] }] },
            { "label": "Leave", "results": [{ "text": "Gone." }] } ] },
          { "id": "sprout", "name": "Sprout", "text": "A sprout.", "choices": [
            { "label": "Grow", "results": [{ "text": "Hull.", "outcomes": [{ "type": "section" }] }] },
            { "label": "Leave", "results": [{ "text": "Gone." }] } ] },
          { "id": "gamble", "name": "Gamble", "text": "A gamble.", "choices": [
            { "label": "Gamble", "results": [
              { "weight": 3, "text": "Won.", "outcomes": [{ "type": "salvage", "amount": 50 }] },
              { "weight": 1, "text": "Lost.", "outcomes": [{ "type": "fight", "enemy": "brute" }] } ] },
            { "label": "Fight", "results": [{ "text": "Fight.", "outcomes": [{ "type": "fight", "enemy": "dummy" }] }] },
            { "label": "Leave", "results": [{ "text": "Gone." }] } ] },
          { "id": "signal", "name": "Signal", "text": "A signal.", "choices": [
            { "label": "Answer", "needsTag": "Drone", "results": [{ "text": "Beep.", "outcomes": [{ "type": "salvage", "amount": 10 }] }] },
            { "label": "Leave", "results": [{ "text": "Gone." }] } ] }
        ]
        """;

    /// <summary>One event that only gives salvage, so it is valid with any pieces and enemies, for content tests.</summary>
    public const string PlainEventsJson = """
        [
          { "id": "plain", "name": "Plain", "text": "Plain.", "choices": [
            { "label": "Take", "results": [{ "text": "Taken.", "outcomes": [{ "type": "salvage", "amount": 5 }] }] },
            { "label": "Leave", "results": [{ "text": "Gone." }] } ] }
        ]
        """;

    public static ContentDb Db { get; } = ContentDb.FromJson(Pieces, Cards, Enemies, Ships, SectionsJson, EventsJson);

    /// <summary>The event of <see cref="PlainEventsJson"/>.</summary>
    public static IReadOnlyList<EventDef> PlainEvents { get; } =
        [new EventDef("plain", "Plain", "Plain.", [
            new EventChoice("Take", [new EventResult("Taken.", [new SalvageOutcome(5)])]),
            new EventChoice("Leave", [new EventResult("Gone.")])])];

    public static RunState NewRun(ulong seed = 1) => RunFactory.NewRun(Db, "test", seed);

    public static RunState NewRun(string shipClassId, ulong seed = 1) => RunFactory.NewRun(Db, shipClassId, seed);

    /// <summary>
    /// Test setup: commits the run to a fight against <paramref name="enemyId"/>, as choosing a fight node
    /// on the map does, without moving on the map. Returns the run.
    /// </summary>
    public static RunState Prepare(RunState run, string enemyId = "dummy")
    {
        run.Encounter = new Encounter(enemyId);
        return run;
    }

    /// <summary>
    /// Test setup: replaces the run's map with a fixed one that is a straight line: a skirmish on row 0
    /// (node 0), a grove on row 1 (node 1, or <paramref name="middle"/>), a hard fight on row 2 (node 2), then
    /// the boss (node 3). Returns the run.
    /// </summary>
    public static RunState UseTinyMap(RunState run, NodeType middle = NodeType.Grove)
    {
        run.Map = new SectorMap
        {
            Sector = run.Sector,
            Nodes =
            [
                new MapNode(0, NodeType.Skirmish, 0, 0),
                new MapNode(1, middle, 1, 0),
                new MapNode(2, NodeType.HardFight, 2, 0),
                new MapNode(3, NodeType.Boss, 3, 0),
            ],
            Edges = [new MapEdge(0, 1), new MapEdge(1, 2), new MapEdge(2, 3)],
        };
        run.Path.Clear();
        return run;
    }

    public static int BridgeId(RunState run) =>
        run.Ship.Fitted.Single(f => Db.Piece(f.Piece.DefId).IsBridge).Piece.Id;

    public static PieceInstance Fit(RunState run, string defId, int x, int y, Rotation rotation = Rotation.R0)
    {
        var piece = run.NewPiece(defId);
        new ShipFitter(Db).Place(run.Ship, piece, new Cell(x, y), rotation);
        return piece;
    }

    /// <summary>
    /// Test setup: moves a copy of the card from wherever it is in the deck into the hand and returns its
    /// id. Pass <paramref name="skip"/> to take a later copy. Copies are counted in the hand, then the draw
    /// pile, then the discard pile, and a copy already in the hand stays where it is, so taking copy 0 and
    /// then copy 1 always gives two different cards.
    /// </summary>
    public static int Take(CombatState combat, string cardId, int skip = 0)
    {
        foreach (var pile in new[] { combat.Hand, combat.DrawPile, combat.DiscardPile })
        {
            foreach (var card in pile.Where(c => c.CardId == cardId))
            {
                if (skip-- > 0)
                {
                    continue;
                }

                if (pile != combat.Hand)
                {
                    pile.Remove(card);
                    combat.Hand.Add(card);
                }

                return card.Id;
            }
        }

        throw new InvalidOperationException($"No copy of '{cardId}' is left in the deck.");
    }

    /// <summary>game/data, as passed in by the test project file.</summary>
    public static string ShippedDataDir =>
        typeof(TestContent).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "BloomDataDir").Value!;

    public static ContentDb LoadShipped()
    {
        string Read(string name) => File.ReadAllText(Path.Combine(ShippedDataDir, name));
        return ContentDb.FromJson(Read("pieces.json"), Read("cards.json"), Read("enemies.json"), Read("ships.json"), Read("sections.json"), Read("events.json"));
    }
}
