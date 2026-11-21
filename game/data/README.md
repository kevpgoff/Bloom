# Game content

The game loads these files at startup through `Bloom.Core.Content.ContentDb`. The test `ShippedContentTests` loads and validates them, so `dotnet test` catches mistakes in them. The files must be strict JSON, with no comments, because Godot's own JSON parser also reads files in the project.

All numbers are placeholders for testing, as the design spec says. Entries marked `"placeholder": true` are invented and not in the spec. For the 7 example pieces, the spec gives no rarity or commune, so the values here are guesses.

| File | Holds |
| --- | --- |
| `pieces.json` | Equipment and crew: shape, tags, the cards they add, an optional adjacency bonus |
| `cards.json` | Cards: cost and effects |
| `enemies.json` | Enemies: hull points and a repeating pattern of intents. Until the sector map exists, a run fights them all in file order, so keep them in order of difficulty. |
| `ships.json` | Ship classes: starting hull, hull points, starting pieces |

## Conventions

- **Shapes** are rows of text: `#` is a cell and `.` is empty. `["###", ".#."]` is a T. Pieces rotate in quarter turns (`R0`, `R90`, `R180`, `R270`, clockwise) and never mirror.
- **Positions** are absolute grid cells `{ "x": column, "y": row }` in the 6×8 grid. Row 0 is the front, which faces the enemy. A position is the top-left corner of the rotated shape.
- **Lanes** are grid columns, counted from 0 on the left.
- **Effects** are objects whose first property must be `"type"` (a .NET 8 rule): `{ "type": "damage", "amount": 5 }`. Card effects are `damage`, `block`, `gainEnergy`, `draw`, `repair`, `focus` and `grow`. Bonus-only effects are `damageBonus` and `startGrown`.
- **Enums** are written as in code: `"Equipment"`, `"Weapon"`, `"HeavyAttack"`.

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

The run's enemies, in order. Lanes are 0-based; the seedling's hull covers lanes 1–5.

| Order | Enemy | Hull | Pattern |
| --- | --- | --- | --- |
| 1 | Feral Harvester | 40 | Attack 6, heavy 10 on lane 2, block 8 |
| 2 | Pollen Squall | 42 | Attack 5, heavy 9 on lane 4, attack 5 |
| 3 | Rogue Tiller | 50 | Block 10, heavy 12 on lane 1, attack 8 |
| 4 | Singing Comet | 60 | Attack 7, heavy 12 on lane 3, block 8, heavy 12 on lane 5 |
