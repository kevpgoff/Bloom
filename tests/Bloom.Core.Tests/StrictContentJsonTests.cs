using Bloom.Core.Content;

namespace Bloom.Core.Tests;

/// <summary>
/// The content files are read strictly: a property that no definition has is an error that names the file and
/// the property. Without this, a misspelled property is ignored and the value it meant to set keeps its
/// default, so <c>"ammount": 5</c> would give a card that deals 0 damage.
/// </summary>
public class StrictContentJsonTests
{
    private const string Pieces = """
        [
          { "id": "bridge", "name": "Bridge", "kind": "Equipment", "tags": ["Core"], "rarity": "Common",
            "shape": ["##"], "cards": [{ "card": "ram", "count": 2 }] },
          { "id": "grinder", "name": "Grinder", "kind": "Crew", "tags": [], "rarity": "Common",
            "shape": ["#"], "cards": [{ "card": "ram", "count": 1 }],
            "bonus": { "name": "lens_focus", "targetTag": "Core", "effect": { "type": "damageBonus", "amount": 2 } } }
        ]
        """;

    private const string Cards = """
        [{ "id": "ram", "name": "Ram", "cost": 1, "effects": [{ "type": "damage", "amount": 5 }],
           "upgrade": { "effects": [{ "type": "damage", "amount": 8 }] } }]
        """;

    private const string Enemies = """
        [{ "id": "a", "name": "A", "pool": "Early", "hull": 9, "pattern": [{ "kind": "HeavyAttack", "amount": 1, "lane": 0 }] },
         { "id": "b", "name": "B", "pool": "Late", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
         { "id": "c", "name": "C", "pool": "Hard", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }] },
         { "id": "d", "name": "D", "pool": "Boss", "hull": 9, "pattern": [{ "kind": "Attack", "amount": 1 }],
           "parts": [{ "name": "Arm", "hull": 5, "pattern": [{ "kind": "Block", "amount": 1 }] },
                     { "name": "Shell", "hull": 5, "pattern": [{ "kind": "Block", "amount": 1 }] }] }]
        """;

    private const string Ships = """
        [{ "id": "s", "name": "S", "hull": 60, "hullShape": ["###"], "hullOrigin": { "x": 0, "y": 0 },
           "rule": { "type": "keepEnergy" },
           "loadouts": [{ "id": "a", "name": "A", "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 0 }, "rotation": "R0" }] }] },
         { "id": "t", "name": "T", "hull": 60, "hullShape": ["###"], "hullOrigin": { "x": 0, "y": 0 },
           "unlock": { "type": "winWith", "ship": "s" },
           "loadouts": [{ "id": "a", "name": "A", "startingPieces": [{ "piece": "bridge", "origin": { "x": 0, "y": 0 } }] }] }]
        """;

    private const string Events = """
        [{ "id": "e", "name": "E", "text": "T", "choices": [
            { "label": "Take", "results": [{ "text": "Taken.", "weight": 2, "outcomes": [{ "type": "salvage", "amount": 5 }] }] },
            { "label": "Leave", "results": [{ "text": "Gone." }] } ] }]
        """;

    private static ContentDb Load(string? pieces = null, string? cards = null, string? enemies = null, string? ships = null, string? sections = null, string? events = null) =>
        ContentDb.FromJson(pieces ?? Pieces, cards ?? Cards, enemies ?? Enemies, ships ?? Ships, sections ?? TestContent.SectionsJson, events ?? Events);

    /// <summary>The content with one piece of text swapped for another. It fails the test if the text is not there exactly once.</summary>
    private static string Swap(string json, string old, string replacement)
    {
        int at = json.IndexOf(old, StringComparison.Ordinal);
        Assert.True(at >= 0 && json.IndexOf(old, at + 1, StringComparison.Ordinal) < 0, $"The test content should hold '{old}' exactly once.");
        return json.Replace(old, replacement, StringComparison.Ordinal);
    }

    private static void AssertRejected(ContentException e, string file, string property)
    {
        string error = Assert.Single(e.Errors);
        Assert.StartsWith(file + ":", error);
        Assert.Contains($"'{property}'", error, StringComparison.Ordinal);

        // The error also says where the property is, since a file can hold the same name many times.
        Assert.Contains($".{property} | LineNumber: ", error, StringComparison.Ordinal);
    }

    /// <summary>The content that the cases below break. It uses every polymorphic type: an effect, a bonus effect, an outcome, a ship rule and an unlock.</summary>
    [Fact]
    public void TheContentOfTheseTestsLoads() => Load();

    [Theory]
    [InlineData("\"rarity\": \"Common\",\n    \"shape\": [\"#\"]", "\"rarety\": \"Common\",\n    \"shape\": [\"#\"]", "rarety")]
    [InlineData("\"count\": 1", "\"cuont\": 1", "cuont")]
    [InlineData("\"targetTag\": \"Core\"", "\"targetTag\": \"Core\", \"target\": \"Core\"", "target")]
    [InlineData("\"amount\": 2", "\"ammount\": 2", "ammount")]
    public void AnUnknownPropertyInPiecesIsReported(string old, string replacement, string property)
    {
        var e = Assert.Throws<ContentException>(() => Load(pieces: Swap(Pieces.ReplaceLineEndings("\n"), old, replacement)));
        AssertRejected(e, "pieces.json", property);
    }

    [Theory]
    [InlineData("\"cost\": 1", "\"Cost\": 1", "Cost")]
    [InlineData("\"amount\": 5", "\"ammount\": 5", "ammount")]
    [InlineData("\"amount\": 8", "\"amount\": 8, \"note\": \"more\"", "note")]
    [InlineData("\"upgrade\": { ", "\"upgrade\": { \"price\": 0, ", "price")]
    public void AnUnknownPropertyInCardsIsReported(string old, string replacement, string property)
    {
        var e = Assert.Throws<ContentException>(() => Load(cards: Swap(Cards, old, replacement)));
        AssertRejected(e, "cards.json", property);
    }

    [Theory]
    [InlineData("\"lane\": 0", "\"lanes\": 0", "lanes")]
    [InlineData("\"pool\": \"Hard\"", "\"pool\": \"Hard\", \"sectr\": 2", "sectr")]
    [InlineData("\"name\": \"Arm\", \"hull\": 5", "\"name\": \"Arm\", \"hul\": 5", "hul")]
    public void AnUnknownPropertyInEnemiesIsReported(string old, string replacement, string property)
    {
        var e = Assert.Throws<ContentException>(() => Load(enemies: Swap(Enemies, old, replacement)));
        AssertRejected(e, "enemies.json", property);
    }

    [Theory]
    [InlineData("{ \"type\": \"keepEnergy\" }", "{ \"type\": \"keepEnergy\", \"cap\": 9 }", "cap")]
    [InlineData("\"ship\": \"s\"", "\"shipClass\": \"s\"", "shipClass")]
    [InlineData("\"rotation\": \"R0\"", "\"turn\": \"R0\"", "turn")]
    [InlineData("\"id\": \"t\", \"name\": \"T\", \"hull\": 60", "\"id\": \"t\", \"name\": \"T\", \"hull\": 60, \"soundset\": \"x\"", "soundset")]
    public void AnUnknownPropertyInShipsIsReported(string old, string replacement, string property)
    {
        var e = Assert.Throws<ContentException>(() => Load(ships: Swap(Ships, old, replacement)));
        AssertRejected(e, "ships.json", property);
    }

    [Theory]
    [InlineData("\"amount\": 5", "\"amout\": 5", "amout")]
    [InlineData("\"weight\": 2", "\"wieght\": 2", "wieght")]
    [InlineData("\"label\": \"Leave\"", "\"label\": \"Leave\", \"needTag\": \"Core\"", "needTag")]
    [InlineData("\"text\": \"T\"", "\"text\": \"T\", \"sectors\": 1", "sectors")]
    public void AnUnknownPropertyInEventsIsReported(string old, string replacement, string property)
    {
        var e = Assert.Throws<ContentException>(() => Load(events: Swap(Events, old, replacement)));
        AssertRejected(e, "events.json", property);
    }

    [Fact]
    public void AnUnknownPropertyInSectionsIsReported()
    {
        const string sections = """
            [{ "id": "rib", "name": "Rib", "shape": ["###"] }, { "id": "pod", "name": "Pod", "shape": ["##", "##"] },
             { "id": "keel", "name": "Keel", "shape": ["####"], "cells": 4 }]
            """;
        var e = Assert.Throws<ContentException>(() => Load(sections: sections));
        AssertRejected(e, "sections.json", "cells");
    }

    /// <summary>
    /// The reader cannot tell these four names from real properties, because each is a value that the code works
    /// out from other fields. They load and are ignored. game/data/README.md says so; this test holds it to that.
    /// </summary>
    [Fact]
    public void FourWorkedOutNamesAreAcceptedAndIgnored()
    {
        var db = Load(
            pieces: Swap(Pieces.ReplaceLineEndings("\n"), "\"id\": \"grinder\",", "\"id\": \"grinder\", \"isBridge\": true,"),
            enemies: Swap(Enemies, "\"id\": \"a\",", "\"id\": \"a\", \"allParts\": [], \"allIntents\": [],"),
            events: Swap(Events, "\"text\": \"Gone.\"", "\"text\": \"Gone.\", \"allOutcomes\": []"));
        Assert.False(db.Piece("grinder").IsBridge);
    }

    /// <summary>The content files are strict JSON (see game/data/README.md), because Godot's own parser reads them too.</summary>
    [Theory]
    [InlineData("[{ \"id\": \"ram\"", "[ // the basic attack\n{ \"id\": \"ram\"")]
    [InlineData("\"amount\": 5 }]", "\"amount\": 5 },]")]
    public void CommentsAndTrailingCommasAreReported(string old, string replacement)
    {
        var e = Assert.Throws<ContentException>(() => Load(cards: Swap(Cards, old, replacement)));
        Assert.StartsWith("cards.json:", Assert.Single(e.Errors));
    }
}
