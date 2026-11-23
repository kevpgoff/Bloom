# Game content

The game loads these files at startup through `Bloom.Core.Content.ContentDb`. The test `ShippedContentTests` loads and validates them, so `dotnet test` catches mistakes in them. The files must be strict JSON, with no comments, because Godot's own JSON parser also reads files in the project.

All numbers are placeholders for testing, as the design spec says. Entries marked `"placeholder": true` are invented and not in the spec. For the 7 example pieces, the spec gives no rarity or commune, so the values here are guesses.

| File | Holds |
| --- | --- |
| `pieces.json` | Equipment and crew: shape, tags, the cards they add, an optional adjacency bonus |
| `cards.json` | Cards: cost, effects and an `upgrade` |
| `enemies.json` | Enemies: hull points, a repeating pattern of intents, a `pool` and a `sector`. The pool says which fights of the sector map draw the enemy (see below). |
| `ships.json` | Ship classes: starting hull, hull points, starting pieces |

## Conventions

- **Shapes** are rows of text: `#` is a cell and `.` is empty. `["###", ".#."]` is a T. Pieces rotate in quarter turns (`R0`, `R90`, `R180`, `R270`, clockwise) and never mirror.
- **Positions** are absolute grid cells `{ "x": column, "y": row }` in the 6×8 grid. Row 0 is the front, which faces the enemy. A position is the top-left corner of the rotated shape.
- **Lanes** are grid columns, counted from 0 on the left.
- **Effects** are objects whose first property must be `"type"` (a .NET 8 rule): `{ "type": "damage", "amount": 5 }`. Card effects are `damage`, `block`, `gainEnergy`, `draw`, `repair`, `focus` and `grow`. Bonus-only effects are `damageBonus` and `startGrown`.
- **Upgrades.** A card's `"upgrade"` is what it becomes when its piece is upgraded: `{ "cost": 0 }`, `{ "effects": [...] }` or both. Upgraded effects replace the card's own list, so write all of them. Every shipped card has an upgrade, and a test checks it. An upgraded card is named with "+" after it.
- **Enums** are written as in code: `"Equipment"`, `"Weapon"`, `"HeavyAttack"`.
- **Enemy pools** are `"Early"` (skirmishes on the map's first 3 rows), `"Late"` (skirmishes on later rows), `"Hard"` (hard fights) and `"Boss"`. Every enemy needs one. `"sector"` defaults to 1. Each sector needs at least one enemy in every pool, and the validator checks it. When a fight node is chosen, its enemy is drawn from the pool, and an enemy repeats in a run only after the rest of its pool has been met.

## The placeholder starting ship (`seedling`)

The owner chose on 2026-11-21 to fit all of the spec's pieces, so that focus, grow and both adjacency bonuses show up in the game. That needs a 5×5 hull, larger than the spec's "about 20 cells". The `tinker` crew and its `patch` card (repair 1) are placeholders, there so that repair can be tried in the game.

```
        x: 0 1 2 3 4 5
   y0      . . . . . .
   y1      . P P P G G     P  Hull Plating (front row, protects lanes 1–3)
   y2      . T L L L .     G  Lens Grinder: +2 damage to the Lance it touches
   y3      . M M S S S     T  Tinker (placeholder crew, adds Patch)
   y4      . M M Y S .     L  Light Lance
   y5      . . Y Y B B     M  Moss Reactor
   y6      . . . . . .     S  Sun Sail (T shape)
   y7      . . . . . .     Y  Spore Singer (R270): Moss cards start grown once
                           B  Bridge
```

The hull covers x 1–5 and y 1–5. That is 25 cells, 3 of them free. The starting deck has 2 + 1 + 1 + 2 + 2 + 1 + 1 + 4 = 14 cards. The first enemy's heavy attack hits lane 2, so it disables the Hull Plating first, then the Lance, then the Moss Reactor.

## Placeholder upgrades

Added with docks and groves on 2026-11-22. All of them are placeholders.

| Card | Card as it is | Upgraded |
| --- | --- | --- |
| Ram | 1: deal 5 | deal 8 |
| Brace | 1: block 5 | block 8 |
| Lance | 2: deal 12 | deal 16 |
| Deflect | 1: block 8 | block 11 |
| Unfurl | 0: gain 2 energy | gain 2 energy, draw 1 |
| Bloom | 1: block 4, grows +2 | block 6, grows +3 |
| Focus | 1: next attack +4 | next attack +7 |
| Chorus | 1: draw 2 | draw 3 |
| Patch | 1: repair 1 | costs 0 |
| Cut | 1: deal 6 | deal 9 |
| Ping | 0: deal 3 | deal 5 |
| Screen | 1: block 6 | block 9 |
| Glide | 0: gain 1 energy, draw 1 | gain 1 energy, draw 2 |
| Sunstrike | 3: deal 26 | costs 2 |
| Tend | 1: block 3, grows +2 | block 5, grows +3 |
| Rewire | 1: repair 1, draw 1 | costs 0 |
| Hymn | 1: next attack +6 | next attack +9 |

## Placeholder rewards and enemies

The owner approved these on 2026-11-21 so that rewards and refitting can be tried before the 30-piece set exists. All of them are `"placeholder": true` and use only existing effects.

| Piece | Kind, tag, rarity | Shape | Cards | Bonus |
| --- | --- | --- | --- | --- |
| Arc Cutter | Equipment, Weapon, Common | `##` | 2 Cut (1: deal 6) | none |
| Seed Drone | Equipment, Drone, Common | `#.` / `##` | 3 Ping (0: deal 3) | none |
| Reed Screen | Equipment, Shield, Common | `.##` / `##.` | 3 Screen (1: block 6) | none |
| Mirror Kite | Equipment, Solar, Uncommon | `#.#` / `###` | 2 Glide (0: gain 1 energy, draw 1) | none |
| Sunspear | Equipment, Weapon, Rare | `####` / `.#..` | 2 Sunstrike (3: deal 26) | none |
| Orchard Keeper | Crew, Common | `#` | 1 Tend (1: block 3, grows +2) | adjacent Garden pieces start grown once |
| Drone Wrangler | Crew, Uncommon | `##` | 1 Rewire (1: repair 1, draw 1) | adjacent Drone pieces deal 2 more damage |
| Choir Elder | Crew, Rare | `###` | 1 Hymn (1: next attack +6) | adjacent Weapon pieces deal 3 more damage |

The enemies of sector 1, by pool. Lanes are 0-based; the seedling's hull covers lanes 1–5. The first four were the old placeholder run of four fights; the rest were added with the sector map on 2026-11-22. The boss is a big normal enemy until boss parts exist.

| Pool | Enemy | Hull | Pattern |
| --- | --- | --- | --- |
| Early | Feral Harvester | 40 | Attack 6, heavy 10 on lane 2, block 8 |
| Early | Pollen Squall | 42 | Attack 5, heavy 9 on lane 4, attack 5 |
| Early | Rust Gleaner | 36 | Attack 5, attack 5, heavy 8 on lane 3 |
| Early | Static Moths | 34 | Attack 4, block 6, attack 4, heavy 9 on lane 1 |
| Late | Rogue Tiller | 50 | Block 10, heavy 12 on lane 1, attack 8 |
| Late | Singing Comet | 60 | Attack 7, heavy 12 on lane 3, block 8, heavy 12 on lane 5 |
| Late | Toll Beacon | 54 | Block 12, attack 9, heavy 13 on lane 4 |
| Late | Bramble Drift | 58 | Attack 6, attack 6, heavy 11 on lane 2, block 10 |
| Hard | Strip Miner | 80 | Heavy 14 on lane 3, attack 10, block 12, heavy 14 on lane 1 |
| Hard | Aurora Knot | 72 | Attack 8, attack 8, heavy 16 on lane 5, block 10 |
| Hard | Salvage Hound | 76 | Attack 12, block 8, heavy 15 on lane 2 |
| Boss | Grand Combine | 140 | Attack 10, heavy 16 on lane 2, block 15, attack 12, heavy 16 on lane 4 |
