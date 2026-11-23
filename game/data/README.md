# Game content

The game loads these files at startup through `Bloom.Core.Content.ContentDb`. The test `ShippedContentTests` loads and validates them, so `dotnet test` catches mistakes in them. The files must be strict JSON, with no comments, because Godot's own JSON parser also reads files in the project.

All numbers are placeholders for testing, as the design spec says. Entries marked `"placeholder": true` are invented and not in the spec. For the 7 example pieces, the spec gives no rarity or commune, so the values here are guesses.

| File | Holds |
| --- | --- |
| `pieces.json` | Equipment and crew: shape, tags, the cards they add, an optional adjacency bonus |
| `cards.json` | Cards: cost, effects and an `upgrade` |
| `enemies.json` | Enemies: hull points, a repeating pattern of intents, a `pool` and a `sector`, and for a boss its `parts`. The pool says which fights of the sector map draw the enemy (see below). |
| `ships.json` | Ship classes: starting hull, hull points, starting pieces |
| `sections.json` | Hull sections that shipyards offer: a name and a shape |
| `events.json` | Events: a story, 2 to 4 choices, and what each choice can come to |

## Conventions

- **Shapes** are rows of text: `#` is a cell and `.` is empty. `["###", ".#."]` is a T. Pieces rotate in quarter turns (`R0`, `R90`, `R180`, `R270`, clockwise) and never mirror.
- **Positions** are absolute grid cells `{ "x": column, "y": row }` in the 6×8 grid. Row 0 is the front, which faces the enemy. A position is the top-left corner of the rotated shape.
- **Lanes** are grid columns, counted from 0 on the left (the game shows them as L1 to L6). A heavy attack aimed at a lane the hull doesn't reach hits the nearest lane it does reach, so on the seedling an attack on lane 0 hits lane 1 until a hull section grows into lane 0.
- **Effects** are objects whose first property must be `"type"` (a .NET 8 rule): `{ "type": "damage", "amount": 5 }`. Card effects are `damage`, `block`, `gainEnergy`, `draw`, `repair`, `focus` and `grow`. Bonus-only effects are `damageBonus` and `startGrown`.
- **Upgrades.** A card's `"upgrade"` is what it becomes when its piece is upgraded: `{ "cost": 0 }`, `{ "effects": [...] }` or both. Upgraded effects replace the card's own list, so write all of them. Every shipped card has an upgrade, and a test checks it. An upgraded card is named with "+" after it.
- **Hull sections** cover 3 to 5 cells joined by edges, and there must be at least 3 of them, since a shipyard offers 3 (hard fights and bosses offer 3 more after their reward). A section adds cells to the hull, not hull points. It turns like a piece when attached.
- **Events.** An event has an `id`, a `name`, its `text` and 2 to 4 `choices`. A choice has a `label`, an optional `cost` in salvage and an optional `needsTag` (one of the tags), and 1 or more `results`. One result is picked by `weight` (default 1), so a single result is certain. A result has its `text` and a list of `outcomes`, each an object whose first property is `"type"`:
  - `salvage`, `hull` and `maxHull` take an `amount`, which can be negative. Salvage never drops below 0, and hull never drops below 1 from an event.
  - `piece` takes an `id`, or a `rarity` and/or a `kind` to pick at random (never the bridge). The piece goes to the cargo hold.
  - `upgrade` upgrades a random piece that can still be upgraded.
  - `section` offers a free hull section after the event.
  - `fight` takes an `enemy` (not a boss) and starts that fight after the event, with that enemy's normal reward.

  The game shows only a choice's label, its cost and its tag, never its outcomes, so the text must carry the hints (the owner, 2026-11-23: "reading matters"). A choice whose tag no fitted piece carries is not shown. Every event needs one choice with no cost and no tag, so the player can always go on. The validator checks all of this, and every sector needs at least one event.
- **Enemy parts** (`"parts"`, a boss's) each have a `name`, a `hull` and their own `pattern`, checked like the body's. An enemy has at most 3; every boss needs 2 or 3. The body acts first, then each working part in order, and all of them add to the enemy's one block.
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

## Placeholder hull sections

Added with shipyards on 2026-11-23. All of them are placeholders, named for grown hull. Every one can attach to the starting seedling, and a test checks it.

| Section | Shape | Cells |
| --- | --- | --- |
| Rib | `###` | 3 |
| Knuckle | `##` / `#.` | 3 |
| Keel | `####` | 4 |
| Seed Pod | `##` / `##` | 4 |
| Spur | `###` / `.#.` | 4 |
| Crook | `#..` / `###` | 4 |
| Twist | `.##` / `##.` | 4 |
| Long Keel | `#####` | 5 |
| Cradle | `#.#` / `###` | 5 |
| Bough | `##` / `##` / `#.` | 5 |

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

The enemies of sector 1, by pool. Lanes are 0-based; the seedling's hull covers lanes 1–5, so an attack on lane 0 hits lane 1 until a section opens lane 0. The three attacks on lane 0 aimed at lane 1 until the owner asked, on 2026-11-23, for the leftmost lane to be attacked once it exists. The first four were the old placeholder run of four fights; the rest were added with the sector map on 2026-11-22. On 2026-11-23 the owner asked for a harder game, and every enemy now hits about half again as hard (see `docs/decisions.md`, "Difficulty"). The boss got its parts on 2026-11-23; each part is listed under it.

| Pool | Enemy | Hull | Pattern |
| --- | --- | --- | --- |
| Early | Feral Harvester | 36 | Attack 10, heavy 13 on lane 2, block 8 |
| Early | Pollen Squall | 38 | Attack 9, heavy 13 on lane 4, attack 9 |
| Early | Rust Gleaner | 34 | Attack 9, attack 9, heavy 13 on lane 3 |
| Early | Static Moths | 32 | Attack 8, block 6, attack 10, heavy 13 on lane 0 |
| Late | Rogue Tiller | 50 | Block 10, heavy 18 on lane 0, attack 14 |
| Late | Singing Comet | 56 | Attack 13, heavy 17 on lane 3, block 8, heavy 17 on lane 5 |
| Late | Toll Beacon | 52 | Block 12, attack 15, heavy 18 on lane 4 |
| Late | Bramble Drift | 54 | Attack 12, attack 12, heavy 17 on lane 2, block 10 |
| Hard | Strip Miner | 70 | Heavy 19 on lane 3, attack 15, block 12, heavy 19 on lane 0 |
| Hard | Aurora Knot | 64 | Attack 13, attack 13, heavy 21 on lane 5, block 10 |
| Hard | Salvage Hound | 68 | Attack 17, block 8, heavy 20 on lane 2 |
| Boss | Grand Combine | 56 | Attack 9, heavy 13 on lane 2, block 6 |
| Boss part | Thresher Arm | 15 | Attack 5, attack 6, attack 7 |
| Boss part | Chaff Screen | 12 | Block 3 |
| Boss part | Baling Ram | 16 | Heavy 10 on lane 4, attack 4, heavy 10 on lane 3 |

## Placeholder events

The first 10 were sketched with the owner on 2026-11-23. The other 10 were written by another agent from the owner's writer prompt the same day, then revised so their stories hint at outcomes instead of listing them. All of them are placeholders. A choice marked with a tag appears only when a fitted piece carries that tag. The text of each is in `events.json`.

| Event | Choices and what they come to |
| --- | --- |
| Vault Sprout | Let it grow: a hull section. Prune it back: +25 salvage |
| Toll Beacon | Pay in scrap (25 salvage): an uncommon piece. Send a drone (Drone): an uncommon piece. Refuse to pay: fight the Toll Beacon. Slip past: even odds of nothing or −8 hull |
| Drifting Orchard | Harvest the fruit: +12 hull. Offer the keeper a berth: Orchard Keeper. Strip the glass: +30 salvage |
| Humming Wreck | Listen a while: an upgrade. Invite the choir aboard: Choir Elder and −6 max hull. Scrape the hull: +35 salvage, and even odds of −6 hull |
| Mirror Field | Realign the array (Solar): Mirror Kite. Bask in its focus: +10 hull. Pry loose a mirror: +30 salvage |
| Stalled Harvester | Broadcast the shutdown code: 3 in 5 +45 salvage, else fight the Feral Harvester. Calm it by force: fight it. Back away: −5 hull |
| Lost Swarm | Lend them your drone's call sign (Drone): Drone Wrangler. Let them tinker: +12 hull. Shoo them off: nothing |
| Comet's Wake | Gather the singing ice: +6 max hull. Chase the comet: fight the Singing Comet. Sail on: nothing |
| Extraction Rig | Shut it down: fight the Strip Miner (a hard fight's reward). Siphon its reactor: an upgrade and −8 hull. Leave it be: nothing |
| Drift Market | Buy a sealed crate (40 salvage): a common piece (6 in 10), uncommon (3 in 10) or rare (1 in 10). Trade stories: +15 salvage. Moor for a meal: +8 hull |
| Kiln Buoy | Hold a fitting in the glare: an upgrade and −8 hull. Shade the jammed shutters (Shield): +30 salvage. Give the buoy wide berth: nothing |
| Lace Coral | Graft a sprig aboard: a hull section and −6 max hull. Scrape resin off the marker: 2 in 3 +30 salvage, else +20 salvage and −4 max hull. Leave the coral untouched: nothing |
| Sleeping Miner | Slip a drone inside (Drone): an uncommon equipment piece. Grapple the loose coils: 1 in 3 +45 salvage, else fight the Strip Miner. Wake and calm the giant: fight the Strip Miner. Drift past in silence: nothing |
| Marooned Choir | Rig her cradle aboard (40 salvage): Choir Elder. Tow the pod into sunlight: 2 in 3 +10 hull, else +8 hull and Spore Singer. Chime farewell and sail on: nothing |
| Tinker Barge | Trade for a seed drone (30 salvage): Seed Drone. Let the new bots tinker: 2 in 3 an upgrade, else −9 hull. Share your drone flight charts (Drone): +30 salvage and +8 hull. Wave and keep course: nothing |
| Tangled Nursery | Hack the main knot loose: Orchard Keeper, then fight the Bramble Drift. Snip the outer runners (Weapon): +10 hull. Leave the raft behind: nothing |
| Sun Well | Bask deep in the warm ring: 3 in 5 +6 max hull, else fight the Pollen Squall. Skim the rim on solar sails (Solar): +10 hull. Steer clear of the clouds: nothing |
| Lantern Tiller | Answer with living vines (Garden): +4 max hull. Snatch the crate between turns: even odds of a common equipment piece and +15 salvage, or a fight with the Rogue Tiller. Calm the tiller head-on: fight it. Leave it to its furrow: nothing |
| Moth Lighthouse | Coast in dark to fetch him: 3 in 5 Lens Grinder, else fight the Static Moths. Pluck the loose prism shards: +25 salvage and −6 hull. Log the drift and sail on: nothing |
| Hound's Cache | Toss bait and open one (35 salvage): even odds of Sunspear or a fight with the Salvage Hound. Pry a crate straight away: +20 salvage, then fight the Salvage Hound. Slip away from the shadow: nothing |
