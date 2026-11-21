# Game content

The game loads these files at startup through `Bloom.Core.Content.ContentDb`. The test `ShippedContentTests` loads and validates them, so `dotnet test` catches mistakes in them. The files must be strict JSON, with no comments, because Godot's own JSON parser also reads files in the project.

All numbers are placeholders for testing, as the design spec says. Entries marked `"placeholder": true` are invented and not in the spec. For the 7 example pieces, the spec gives no rarity or commune, so the values here are guesses.

| File | Holds |
| --- | --- |
| `pieces.json` | Equipment and crew: shape, tags, the cards they add, an optional adjacency bonus |
| `cards.json` | Cards: cost and effects |
| `enemies.json` | Enemies: hull points and a repeating pattern of intents |
| `ships.json` | Ship classes: starting hull, hull points, starting pieces |

## Conventions

- **Shapes** are rows of text: `#` is a cell and `.` is empty. `["###", ".#."]` is a T. Pieces rotate in quarter turns (`R0`, `R90`, `R180`, `R270`, clockwise) and never mirror.
- **Positions** are absolute grid cells `{ "x": column, "y": row }` in the 6×8 grid. Row 0 is the front, which faces the enemy. A position is the top-left corner of the rotated shape.
- **Lanes** are grid columns, counted from 0 on the left.
- **Effects** are objects whose first property must be `"type"` (a .NET 8 rule): `{ "type": "damage", "amount": 5 }`. Card effects are `damage`, `block`, `gainEnergy`, `draw`, `repair`, `focus` and `grow`. Bonus-only effects are `damageBonus` and `startGrown`.
- **Enums** are written as in code: `"Equipment"`, `"Weapon"`, `"HeavyAttack"`.

## The placeholder starting ship (`seedling`)

```
        x: 0 1 2 3 4 5
   y0      . . . . . .
   y1      . P P P . .     P  Hull Plating (front row, protects lanes 1–3)
   y2      . L L L . .     L  Light Lance
   y3      . S S S . .     S  Sun Sail (T shape)
   y4      . . S B . .     B  Bridge (rotated to stand vertically)
   y5      . . . B . .
   y6      . . . . . .
   y7      . . . . . .
```

The hull covers x 1–4 and y 1–5. That is 20 cells, 8 of them free. The starting deck has 2 + 2 + 1 + 4 = 9 cards.
