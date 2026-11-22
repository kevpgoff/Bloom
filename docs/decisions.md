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
| Art direction | Option A, vector art in the "Holo Grove" style: a dark teal night, the ship drawn as a lit hologram (glowing outlines in each piece's tag color, scanlines, corner brackets, root traces on the deck), and the Saira typeface. The owner picked it over Option B (pixel art) and over two other takes on A. Decided 2026-11-21. Conventions are in `game/art/README.md`. |
| CI | A very minimal check on pull requests that keeps GitHub spending low, since the repo is private and Actions time is billed per minute. Decided 2026-11-22. Taken as defaults: one job that builds and tests on the 1-vCPU `ubuntu-slim` runner, which costs a third of `ubuntu-latest`; pull requests only; no run when only Markdown files or `docs/` change; a newer push cancels the run it replaces; a 10-minute limit; no NuGet cache. Details in `.github/workflows/ci.yml`. |

## Combat rules decided on 2026-11-21

The spec left these open. The owner decided them for the first fight on the Combat screen.

| Topic | Decision |
| --- | --- |
| Focus | Focus adds up into one bonus: two Focus cards give the next attack +8. It lasts until an attack (a card with a damage effect) uses it or the fight ends, and is used up even if enemy block absorbs all the damage. |
| Repair: which piece | The player taps the disabled piece to bring back. A card that repairs N pieces needs N picks, or all the disabled pieces if fewer are disabled. |
| Repair: the cards | The repaired piece's cards go to the discard pile and keep any growth. |
| Repair with nothing disabled | A card that repairs cannot be played while no piece is disabled, even if it has other effects. |
| Spore Singer disabled mid-fight | The starting growth it gave stays. A disabled piece only stops giving bonuses from then on. |
| Starting ship (placeholder) | A 5×5 hull with all 7 pieces from the spec, plus a placeholder 1-cell crew, Tinker, whose Patch card repairs 1 piece. |

Taken as defaults, not raised by the owner: grow raises only block, counts separately for each copy of a card, and resets each fight. A card's damage shown in the hand includes pending Focus. If a card ever has two damage effects, bonuses and Focus add to each one.

## Rewards and refitting decided on 2026-11-21

The owner decided these for the loop of fight, reward, refit and next fight, before the sector map exists.

| Topic | Decision |
| --- | --- |
| Reward offers | After a won fight the player picks 1 of 3 pieces or skips. Each pick rolls a rarity (common 60%, uncommon 30%, rare 10%, placeholder odds), then a piece of that rarity. A piece is 3× as likely if it shares a tag with a fitted piece; crew count the tag their bonus targets. No piece appears twice in one offer, and the bridge is never offered. |
| Full cargo hold | The hold may go over its 3 pieces while refitting, but the next fight cannot start until fitting or composting brings it back to 3. Skipping a reward is always allowed. |
| Placeholder run | 4 enemies in a fixed order of rising difficulty, the order of `enemies.json`. Winning the 4th wins the run. Replaced by the sector map. |
| Hull between fights | Damage carries over with no healing, as the spec says, until groves and docks exist. |
| Reward pool | 8 placeholder pieces (5 equipment, 3 crew) using only existing effects, plus every spec piece except the bridge. |
| Salvage | Left out until docks exist. Composting removes a piece for nothing for now. |
| Run start | A new run opens on the Fitting screen, so the player can refit before the first fight. |
| Taps when fitting | The first tap selects a piece and shows its cards and bonus; a tap on the selected piece turns it clockwise. Dragging works at any time. |

Taken as defaults, not raised by the owner:
- Undo steps back through every refit action (move, turn, fit, unfit, compost), one per press, up to 50 steps. It is saved with the run and cleared when the next fight starts.
- A piece turns in place, keeping its center; if it doesn't fit there, the nearest spot within one cell is used, and if none fits the turn is refused.
- A piece in the cargo hold turns on screen only, since it has no position yet; that turn is not saved.
- The Fitting screen names the next enemy and marks the lanes of its heavy attacks.

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