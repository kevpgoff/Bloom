# Design decisions: solarpunk ship roguelite (working title)

Recorded on 2026-11-20. The full design spec is a doc at https://claude.ai/artifact/8WTEGCWRg47LQEfRJWbJC1. This file lists only the decisions that the owner made, so that later sessions start from them.

## Decided

| Topic | Decision |
| --- | --- |
| Abilities in combat | Each fitted piece (equipment or crew) adds cards to a deck. The player draws a hand each turn. |
| Collection | Pieces are found during runs only. Every run starts from a basic ship. |
| Run length | 60 minutes or more, played over many short sittings, with saving after every action. |
| Carryover between runs | Unlocks only: new ships, pieces and events. No added power. |
| Refitting | Free at any time outside combat. The layout locks when a fight starts. |
| Adjacency | Some pieces give bonuses to the pieces that touch them. |
| Damage | Attacks reduce hull points. Heavy attacks also disable a piece, which removes its cards until it is repaired. |
| Ship growth | The player adds hull sections that add cells to the grid. |
| Map | Each sector is a branching map of nodes that ends in a boss. |
| Business model | Paid up front. No ads and no purchases inside the game. |
| Orientation | Portrait, for play with one hand. |
| Tone | Hopeful and strange solarpunk. Threats are anomalies and feral machines. |
| Engine | Godot 4 (.NET build) with C#. Game rules live in a plain C# library, `Bloom.Core`, with no Godot dependency. Decided 2026-11-20. |

## Proposed in the spec, not yet confirmed

- Three sectors for each run, about 8 nodes and 25 minutes for each sector.
- Grid of at most 6 columns by 8 rows. About 20 cells at the start and about 40 at the end of a run.
- Each column is a lane. A heavy attack disables the frontmost working piece in its lane.
- 3 energy and 5 cards each turn. 60 hull points at the start of a run.
- Disabled pieces come back at no cost after a fight.
- Renown fills an unlock track. 4 ship classes and 10 voyage levels for each ship at launch.
- Setting names: the Bloom, Sunwrights, Tide Orchard, Mycelial Choir, Tinker Hives.

## Open questions

- Price of the game.
- Final name of the game and of things in the setting.
- Whether some shields protect a single lane.
- Whether disabled pieces stay disabled after a fight.
- Whether normal enemies have parts to target.
- Whether crew can be lost for the rest of a run.
- Team and schedule for the first playable version.