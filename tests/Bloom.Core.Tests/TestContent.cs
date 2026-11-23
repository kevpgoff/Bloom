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
/// rebalanced. The test ship is a 5x5 hull at the top-left of the grid (x 0-4, y 0-4) with the
/// bridge on cells (0,4) and (1,4). The enemies "heavy" and "mixed" make heavy attacks on lane 0.
/// All pieces are common except "drone" (uncommon) and "spear" (rare), for reward tests.
/// No piece adds the card "chorus" (draw 2); tests put it in the hand themselves, so the reward pool stays the same.
/// Enemy pools: "dummy" and "blocker" are early, "heavy" and "mixed" late, "brute" hard and "titan" the boss.
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
          { "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "type": "damage", "amount": 5 }] },
          { "id": "brace", "name": "Brace", "cost": 1, "effects": [{ "type": "block", "amount": 5 }] },
          { "id": "lance", "name": "Lance", "cost": 2, "effects": [{ "type": "damage", "amount": 12 }] },
          { "id": "deflect", "name": "Deflect", "cost": 1, "effects": [{ "type": "block", "amount": 8 }] },
          { "id": "focus", "name": "Focus", "cost": 1, "effects": [{ "type": "focus", "amount": 4 }] },
          { "id": "unfurl", "name": "Unfurl", "cost": 0, "effects": [{ "type": "gainEnergy", "amount": 2 }] },
          { "id": "sprout", "name": "Sprout", "cost": 1, "effects": [{ "type": "block", "amount": 4 }, { "type": "grow", "amount": 2 }] },
          { "id": "patch", "name": "Patch", "cost": 1, "effects": [{ "type": "repair", "count": 1 }] },
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
          { "id": "titan", "name": "Titan", "pool": "Boss", "hull": 40, "pattern": [{ "kind": "Attack", "amount": 5 }] }
        ]
        """;

    private const string Ships = """
        [
          { "id": "test", "name": "Test", "hull": 60,
            "hullShape": ["#####", "#####", "#####", "#####", "#####"], "hullOrigin": { "x": 0, "y": 0 },
            "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 4 } }] }
        ]
        """;

    public static ContentDb Db { get; } = ContentDb.FromJson(Pieces, Cards, Enemies, Ships);

    public static RunState NewRun(ulong seed = 1) => RunFactory.NewRun(Db, "test", seed);

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
    /// (node 0), a grove on row 1 (node 1), a hard fight on row 2 (node 2), then the boss (node 3). Returns the run.
    /// </summary>
    public static RunState UseTinyMap(RunState run)
    {
        run.Map = new SectorMap
        {
            Sector = run.Sector,
            Nodes =
            [
                new MapNode(0, NodeType.Skirmish, 0, 0),
                new MapNode(1, NodeType.Grove, 1, 0),
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
    /// id. Pass <paramref name="skip"/> to take a later copy.
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

                pile.Remove(card);
                combat.Hand.Add(card);
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
        return ContentDb.FromJson(Read("pieces.json"), Read("cards.json"), Read("enemies.json"), Read("ships.json"));
    }
}
