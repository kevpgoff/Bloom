using System.Reflection;
using Bloom.Core.Content;
using Bloom.Core.Grid;
using Bloom.Core.Pieces;
using Bloom.Core.Run;

namespace Bloom.Core.Tests;

/// <summary>
/// Small fixed content for rule tests, so they don't break when the shipped data in game/data is
/// rebalanced. The test ship is a 5x5 hull at the top-left of the grid (x 0-4, y 0-4) with the
/// bridge on cells (0,4) and (1,4).
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
            "shape": ["###", ".#."], "cards": [{ "card": "unfurl", "count": 1 }] }
        ]
        """;

    private const string Cards = """
        [
          { "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "type": "damage", "amount": 5 }] },
          { "id": "brace", "name": "Brace", "cost": 1, "effects": [{ "type": "block", "amount": 5 }] },
          { "id": "lance", "name": "Lance", "cost": 2, "effects": [{ "type": "damage", "amount": 12 }] },
          { "id": "deflect", "name": "Deflect", "cost": 1, "effects": [{ "type": "block", "amount": 8 }] },
          { "id": "focus", "name": "Focus", "cost": 1, "effects": [{ "type": "focus", "amount": 4 }] },
          { "id": "unfurl", "name": "Unfurl", "cost": 0, "effects": [{ "type": "gainEnergy", "amount": 2 }] }
        ]
        """;

    private const string Enemies = """
        [
          { "id": "dummy", "name": "Dummy", "hull": 30, "pattern": [{ "kind": "Attack", "amount": 6 }] },
          { "id": "heavy", "name": "Heavy", "hull": 100, "pattern": [{ "kind": "HeavyAttack", "amount": 10, "lane": 0 }] },
          { "id": "blocker", "name": "Blocker", "hull": 30, "pattern": [{ "kind": "Block", "amount": 5 }] }
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

    public static int BridgeId(RunState run) =>
        run.Ship.Fitted.Single(f => Db.Piece(f.Piece.DefId).IsBridge).Piece.Id;

    public static PieceInstance Fit(RunState run, string defId, int x, int y, Rotation rotation = Rotation.R0)
    {
        var piece = run.NewPiece(defId);
        new ShipFitter(Db).Place(run.Ship, piece, new Cell(x, y), rotation);
        return piece;
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
