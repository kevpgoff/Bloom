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
| Source code | Public on GitHub at https://github.com/kevpgoff/Bloom. Decided 2026-11-22. There is no license file, so default copyright applies: people can read the code but have no permission to reuse it. |
| CI | A very minimal check on pull requests that keeps GitHub spending low. Decided 2026-11-22 while the repo was private and Actions time was billed per minute. Now that the repo is public it costs nothing, and it stays cheap if the repo goes private again. Taken as defaults: one job that builds and tests on the 1-vCPU `ubuntu-slim` runner, which costs a third of `ubuntu-latest`; pull requests only; no run when only Markdown files or `docs/` change; a newer push cancels the run it replaces; a 10-minute limit; no NuGet cache. Details in `.github/workflows/ci.yml`. |

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
| Placeholder run | 4 enemies in a fixed order of rising difficulty, the order of `enemies.json`. Winning the 4th wins the run. Replaced by the sector map on 2026-11-22. |
| Hull between fights | Damage carries over with no healing, as the spec says, until groves and docks exist. |
| Reward pool | 8 placeholder pieces (5 equipment, 3 crew) using only existing effects, plus every spec piece except the bridge. |
| Salvage | Left out until docks exist. Composting removes a piece for nothing for now. Since the sector map (2026-11-22), fights and composting pay salvage. |
| Run start | A new run opens on the Fitting screen, so the player can refit before the first fight. Since the sector map (2026-11-22), it opens on the map, and the ship can be refitted from there. |
| Taps when fitting | The first tap selects a piece and shows its cards and bonus; a tap on the selected piece turns it clockwise. Dragging works at any time. |

Taken as defaults, not raised by the owner:
- Undo steps back through every refit action (move, turn, fit, unfit, compost), one per press, up to 50 steps. It is saved with the run and cleared when the next fight starts.
- A piece turns in place, keeping its center; if it doesn't fit there, the nearest spot within one cell is used, and if none fits the turn is refused.
- A piece in the cargo hold turns on screen only, since it has no position yet; that turn is not saved.
- The Fitting screen names the next enemy and marks the lanes of its heavy attacks.

## Combat feedback decided on 2026-11-21

The owner decided these for the feedback on abilities in combat.

| Topic | Decision |
| --- | --- |
| Scope | Combat only, for now: every card effect, every enemy action, and winning or losing a fight. |
| Kinds of feedback | Particles and screen shake, plus haptics on phones. |
| Intensity | Punchy. An attack fires a bolt from the card's source piece on the grid to the enemy. The screen shakes on big hits and on any hull damage. Every effect has its own particles. |
| Settings | Shake strength (0 to 1) and haptics on or off, saved in `user://settings.cfg` apart from the run. No settings screen yet. |
| Ending a turn | Staggered: the screen updates at once, and the effects are timed so the enemy's hit plays first and the new hand deals in about 0.4 s later. Input is never blocked. |
| Winning a fight | The combat screen stays up about 1 s, so the killing blow and the enemy's shatter play before the Reward screen. |
| Double taps on End Turn | End Turn ignores a second press within 0.3 s, so a double tap can't end two turns while the new hand deals in. Nothing else is guarded. |

Taken as defaults, not raised by the owner:
- The Defeat and Voyage complete overlays appear about 0.9 s after the last hit.
- New cards can be tapped while they deal in.
- The opening hand of a fight deals in too.
- Resuming a saved run replays nothing: the events of an action are not saved.
- Effects are drawn in code over the combat screen, not with Godot's particle nodes.
- The shake moves the screen's content, not the backdrop. Strength 1 moves it at most 14 px.
- All timings and strengths are placeholders, in `CombatFeedback.Tuning` and the code next to it.

## Android debug build decided on 2026-11-22

The owner decided these while setting up the first Android build, a debug build installed on their phone over USB.

| Topic | Decision |
| --- | --- |
| Merged branches | The branches of merged pull requests are deleted, and GitHub now deletes a pull request's branch when it is merged. |
| .NET SDK | The repo moves to the .NET 10 SDK, which is supported until November 2028. Godot 4.7 exports C# to Android only with .NET 9 or later, and .NET 8 and 9 went out of support on 2026-11-10. `Bloom.Core` and the game still target .NET 8, Godot's minimum. The tests target .NET 10. Android builds of the game target .NET 9, as Godot's own project template does. |
| SDK install | On the owner's machine, the .NET 10 SDK comes from Ubuntu's apt package, beside SDK 8, so the terminal and the Godot editor find it without changes to PATH. |
| JDK | The installed OpenJDK 25, rather than the JDK 17 that Godot recommends. It works because the build doesn't use Gradle. |

Taken as defaults, not raised by the owner:
- The package id is `com.kevpgoff.bloom` and the app name is Bloom. Changing the id later makes a new app on the phone, which loses its save.
- arm64 only, and no Gradle build, so nothing in `game/android/` is committed.
- Debug builds are signed with the Godot editor's debug keystore, which stays outside the repo.
- The only permission is `VIBRATE`. There is no `INTERNET` permission, since the game plays offline.
- The game runs full screen (Godot's immersive mode). Min SDK 24 and target SDK 36 are the template's defaults.
- `rendering/textures/vram_compression/import_etc2_astc` is on, because Godot won't export to Android without it. Every texture is imported lossless, so nothing looks different.
- The preset is named `Android`, and the APK goes to `build/android/bloom-debug.apk`, which is ignored.

## Phone fixes decided on 2026-11-22

The owner decided these after the first test on their phone, a Pixel with Android 16.

| Topic | Decision |
| --- | --- |
| Screen edges | The game stays full screen, and the layout keeps clear of the camera cutout and the rounded corners. The backdrop still fills the screen. The other option was to keep Android's status and navigation bars on screen. |
| Follow-ups from the phone test | Fixed now: small text, the missing app icon and the cold start. The back gesture was tested by the owner: it quits the game, and reopening it resumes the fight. |

Taken as defaults, not raised by the owner:
- Android reports the camera cutout as the safe area. Godot can't read the rounded corners, so on a phone the layout also keeps 28 dp clear at the top and the bottom. With the 16 px side margin, that clears a corner of up to about 55 dp radius. Desktop windows keep the 16 px margin on every side.
- After a swipe from the edge, the 3-button navigation bar still shows over the bottom of the screen for a few seconds.
- Text is at least 19 px on the 720 px wide layout: about 11 sp on the owner's phone, which is 411 dp wide. Where a word can't fit, it shrinks: piece names to 16 px on one line, or up to 18 px on two lines; card names in a hand of 6 or more cards; and captions on cards with two effects, which share half a card's width each (14 to 19 px).
- The app icon is a placeholder: a hologram hex cell with a sprout growing in it, in the Holo Grove colors. It has Android's adaptive layers and a monochrome layer for themed icons.
- The engine's boot splash shows no image, only the backdrop's darkest color. Android's own launch splash shows the icon on the phone's dark system color, because Godot sets that color only in a Gradle build.
- The cold start was about 1.3 s from the tap to the fight on the owner's phone. About 0.5 s of it was .NET starting up, and 0.37 s the game's own startup, mostly code being compiled on first use. The owner then chose ahead-of-time compilation (next section).

## Ahead-of-time compilation decided on 2026-11-22

| Topic | Decision |
| --- | --- |
| C# on Android | Compiled ahead of time with Godot's experimental NativeAOT option for Android (`dotnet/android_use_linux_bionic`), to cut the cold start. |

Taken as defaults, not raised by the owner:
- The Android NDK r29 (`29.0.14206865`), the version Godot 4.7.2 pins, installed in the SDK folder.
- The NativeAOT build targets .NET 10, which is supported until November 2028. It uses none of the .NET 9 jars in Godot's export template. A Mono build (the option off) still targets .NET 9.
- Debug builds leave out debug symbols (`dotnet/include_debug_symbols`), which would add 31 MB to the APK.
- On the owner's phone, a cold start now reaches the fight in about 0.8 s instead of 1.25 s, and the APK is 60 MB instead of 105 MB. The FX demo plays the same fight, event for event, as with Mono, at 56 to 61 fps.
- Godot logs a `libmonosgen-2.0.so` error at every start, because it tries Mono first. GodotSharp produces trim and AOT warnings at export, as it does for iOS. The game's own code produces none.

## Sector map decided on 2026-11-22

The owner decided these in a round of questions about adding progression to a run. The work comes in 5 pull requests, each playable, with a stop after each one so the owner can play it on the phone: the map and its fights; docks, groves and upgrades; shipyards; events; the boss's parts.

| Topic | Decision |
| --- | --- |
| Scope | Progression inside a run: the sector map and its nodes. Renown, unlocks and the logbook come later. |
| Run length | One sector of about 8 nodes that ends in a boss, as in the spec's first playable. The code handles more sectors (`Rules.SectorsPerRun`), so going to 3 is mostly content. |
| Nodes | Skirmish, hard fight, boss, dock, shipyard, grove and event. |
| Enemy reveal | The map shows node types only. Choosing a fight node commits to it; the Fitting screen then shows its enemy and heavy lanes, so the player can refit before the fight. |
| Boss | The spec's parts: 2 or 3 of them, and the body and each working part act every turn with their own patterns. A part whose hull reaches 0 stops acting. The fight is won when the body's hull reaches 0. |
| Targeting | Tap the body or a part to aim. Every attack hits it until the player aims elsewhere. It starts on the body. |
| Upgrades | Written for each card in `cards.json`: an upgraded cost, effects or both. Placeholders are written for every card. |
| Shipyard | Free: pick 1 of 3 hull section shapes and attach it, one per visit. The spec's economy table priced sections at 100 salvage; the node table, which made them free, wins. |
| Content | 8 normal enemies, 3 hard ones, 1 boss and 6 events, all placeholders. Pieces stay at 16 for now. |
| Text on screens | No instruction text: screens show information (names, numbers, card and piece text, whether a piece fits and which bonuses it turns on) and short flavor lines, never how to play them. The owner asked for this after seeing the map's hint panel: "the gameplay should be obvious and not need explainers at every screen". The map lost its info panel, and the Set course button now names the chosen node. The Fitting, Reward, Grove and Combat screens lost their hints. |
| Pull request #4 | Merged into `main` before this work, which branches from it. It held the phone fixes and #5's NativeAOT build, which had been merged into #4's branch, not into `main`. |

Taken as defaults, not raised by the owner (all numbers are in `Rules` and are placeholders):
- The map has 7 rows of choices and the boss row. It is made from 4 paths that walk up 4 columns, one column left or right at most per row, never crossing. That gives 2 to 4 nodes per row, and the whole map fits on one phone screen without scrolling. The first row is at the bottom.
- Rows 1 and 2 are skirmishes, and the row just before the boss is all groves. The rows between them are rolled: skirmish 40, event 20, hard fight 15, dock 10, shipyard 10, grove 5. Hard fights, docks, shipyards and groves never follow themselves on a path. A map has at least one dock and one shipyard. A type gets its weight only when it is built, so the first pull request rolls only skirmishes, hard fights and groves.
- Enemies come in pools: early (skirmishes on the first 3 rows), late, hard and boss. Each enemy has a pool and a sector in `enemies.json`. The enemy is rolled when the node is chosen, and it repeats in a run only once the rest of its pool has been met.
- A run starts with 0 salvage. A skirmish pays 20 to 30, a hard fight 40 to 55 (60 to 80 since the shipyards' pull request, below), and a boss before the last sector 60 to 80. Composting pays 10. Undo takes back composting's salvage with the piece. The salvage is paid when the fight is won, even if the piece is skipped.
- After a hard fight or a boss, the first of the 3 offered pieces is always rare.
- Refitting is allowed on the map (the Ship button) and after choosing a fight. Undo is cleared when a node is entered. The cargo limit blocks only the start of a fight.
- Groves come with the first pull request, with only the repair option (30% of the maximum hull, rounded up, never past the maximum), so a sector of about 7 fights has some healing. The upgrade option joins it with upgrades. It did, in the second pull request (below).
- A boss before the last sector gives a reward with a rare piece, then the next sector's map. A free hull section joins that reward with shipyards. With one sector, beating the boss wins the run.
- Whether the Fitting screen is open over the map is not saved: a resumed run opens on the map.
- Enemy icons for the 8 new enemies and the node icons are new art in the Holo Grove style.
- A simple bot that fights with the starting ship and never takes a piece wins 399 of 400 runs, losing on average 0.4 hull per early fight, 2.4 per late fight and 8 per hard fight. The placeholder enemies are too weak, and need a balance pass once docks and upgrades exist.

## Docks, groves and upgrades decided on 2026-11-22

The second of the sector map's 5 pull requests. The owner had already decided that upgrades are written for each card (see above). The owner merged the first pull request and asked to go ahead with this one.

Taken as defaults, not raised by the owner (all numbers are in `Rules` or `cards.json` and are placeholders):
- **Upgrades.** As the spec says, each piece can be upgraded once, and the upgrade improves every card the piece adds. A card's upgrade in `cards.json` gives a new cost, a new list of effects that replaces the card's own, or both. Every shipped card has one: an attack or block card gets about 40% more, Patch, Rewire and Sunstrike cost 1 less, Unfurl also draws 1, Chorus and Glide draw 1 more. A piece can be upgraded if at least one of its cards has an upgrade. Fitted and held pieces can both be upgraded.
- **How upgrades show.** An upgraded piece has "+" after its name everywhere: on the grid, in the cargo hold and in its cards' names ("Lance+"). Where an upgrade is offered, the numbers it changes are green. Bonuses, focus and growth add to the upgraded numbers.
- **Groves.** Repair (30% of the maximum hull) or upgrade one piece. Each upgradable piece has its own card showing its cards as they would be. Repair starts selected unless the hull is already whole.
- **Docks.** 3 pieces for sale, rolled by the reward rules (rarity odds, then the ship's tags) when the node is chosen, with no rare guaranteed. Prices are the spec's: 40, 70 or 120 by rarity. The dock buys any piece but the bridge, fitted or held, for half its price, rounded down; an upgraded piece sells for the same. A repair restores 10 hull for 20 salvage. A bought piece goes to the cargo hold, even past its capacity, as a taken reward does. The player leaves when done. At first a dock sold as many repairs as the player could pay for; the owner limited them the next day (below).
- **Refitting at a dock.** Allowed: the Ship button opens the Fitting screen, whose main button goes back to the dock. There, the selected piece's button sells it instead of composting it. Each trade clears Undo, since undoing a refit step from before a trade would take back the trade's salvage or pieces.
- **Docks on the map.** Docks now have their weight (10) on the rolled rows, and every map has at least one.
- **Save.** The save schema goes to 5 (a card in a fight remembers that it is upgraded, and a run remembers the dock it is at), so a run saved by the first pull request is set aside and a new run starts. The limit on repairs (below) takes it to 6.
- **Balance.** The bot from the first pull request, which now repairs at docks and groves and upgrades a piece when its hull is above 70%, still won 399 of 400 runs, which led to the difficulty pass below.

## Difficulty decided on 2026-11-23

The owner asked to make the game harder: "only allow a variable amount of repairs at docks, and make enemies more difficult and anything else you recommend".

| Topic | Decision |
| --- | --- |
| Dock repairs | Each dock has 1 to 3 repairs (`Rules.DockRepairs`), rolled with its pieces when the node is chosen. The dock shows how many are left. Each still restores 10 hull for 20 salvage. |
| Enemies | Every enemy hits harder: attacks and heavy attacks went up by about half (early enemies by 3 to 6, late by 5 to 6, hard by 5, the boss by 4). Hull went down a little for early, late and hard enemies, and from 140 to 120 for the boss, so fights stay about as long while costing hull. The numbers are in `game/data/README.md`. |

Taken as defaults, not raised by the owner:
- **Target.** About half of the runs won by a bot that plays sensibly. The game was tuned with two bots that play every card by fixed priorities (repair, free cards, a killing blow, draw, block when hit, focus, damage). The plain bot never takes a piece; it repairs at groves and docks when hurt and otherwise upgrades. The smart bot also takes, buys and fits every piece that fits, and picks hard fights while its hull is above 75%. Over 400 runs each: the plain bot wins 55% and the smart one 44%. A skirmish costs about 2 hull early and 11 later, a hard fight 22. About two thirds of the losses are to the boss, the rest on the way. Before, both bots won 399 of 400 runs.
- **What is left alone.** The grove's repair (30%), the starting hull (60), salvage and prices. Shipyards, events and the boss's parts will each change the balance, so another pass will follow them.
- **Why the smart bot wins less.** Hard fights cost about 22 hull, and their rare piece usually has no room on the starting ship, which has 3 free cells. Hard fights should become worth their risk once shipyards add hull to fit pieces on; if not, their reward needs a look.

## Shipyards decided on 2026-11-23

The third of the sector map's 5 pull requests. The owner had already decided that a shipyard is free and attaches 1 of 3 sections per visit (see above). The owner merged the second pull request and asked to go ahead with this one.

Taken as defaults, not raised by the owner (all numbers are in `Rules` or `sections.json` and are placeholders):
- **Sections.** `game/data/sections.json` holds 10 placeholder shapes in the spec's range of 3 to 5 cells: two of 3 cells, five of 4 and three of 5. A section adds cells only, not hull points, since the spec gives sections none. The validator checks that each is one group of 3 to 5 cells and that there are at least 3.
- **The offer.** 3 different sections, all equally likely, rolled when the node is chosen, from those that can still attach somewhere. If fewer fit, fewer are offered; if none fits, the shipyard offers only Leave.
- **Attaching.** A section can go anywhere it stays inside the 6×8 grid, covers no hull and shares an edge with the hull, in any of its 4 turns. The player may leave without one. The ship cannot be refitted at a shipyard; the new cells can be used from the map.
- **The Shipyard screen.** The whole grid with its free cells, and the sections as 3 cards under it. The chosen section shows on the grid as a ghost, green where it attaches and red where it does not, starting where it shares the most edges with the hull. Dragging moves it, floating above the finger as on the Fitting screen; a tap on it turns it; a tap elsewhere moves it to the nearest place it fits. The main button names the section ("Attach · Keel"). There is no instruction text.
- **A boss's free section.** As the spec's node table says, a boss before the last sector also gives a section: the same offer, after its reward and before the next sector's first node, on the same screen titled "Hull section". With one sector it cannot happen yet; tests cover it.
- **Shipyards on the map.** Shipyards now have their weight (10) on the rolled rows, and every map has at least one.
- **Save.** The save schema goes to 7 (a run remembers the sections on offer), so a run saved by the second pull request is set aside and a new run starts.
- **Balance, before the owner's changes below.** The same two bots, with the smart one now choosing a shipyard over a skirmish and attaching the first section where it shares the most edges, won 59% (plain, was 55%) and 49% (smart, was 44%) of 400 runs. Shipyards take the place of some skirmishes, so a path has a few fewer fights. The smart bot attached 0.7 sections a run, about 3 cells, which did not make hard fights worth their risk. And the seedling's hull leaves lane L1 (lane 0 in the data) empty, where no enemy aimed a heavy attack, so pieces on a section there could never be hit by one.

After seeing that, the owner decided:

| Topic | Decision |
| --- | --- |
| Hard fights | Their reward goes up. Taken as the way to do it: a hard fight gives a free hull section after its piece, as a boss does, so its rare piece has room; and it pays 60 to 80 salvage instead of 40 to 55. |
| The leftmost lane | Enemies attack it once it exists. Taken as the way to do it: a heavy attack aimed at a lane the hull does not reach hits the nearest lane it does reach. The three heavy attacks that aimed at L2 (Static Moths, Rogue Tiller, Strip Miner) now aim at L1, so they hit L2 as before until a section grows into L1, and L1 after. The Fitting and Combat screens mark the lane that will be hit. |

- **The reward screen** says "+ Hull section" under the salvage when a section follows. The section's screen is titled "Hull section" there, as after a boss.
- **Balance after them.** A third bot now picks hard fights when healthy and takes only rare pieces, fitting them once the section is attached. Over 400 runs: plain 62%, rare-only 59%, smart 44%. A hard fight now comes out about even with a skirmish for the rare-only bot instead of clearly behind. It still costs about 23 hull against 11 for a late skirmish. The smart bot loses more because it takes every piece that fits, which waters down its deck (it reaches the boss with the same hull and loses there). The bots spend almost no salvage, so the higher salvage does not show in these numbers; for a player it buys pieces and repairs at docks.

## Events decided on 2026-11-23

The fourth of the sector map's 5 pull requests. The owner merged the third and asked to go ahead with events, sketching them together.

Decided by the owner:

| Topic | Decision |
| --- | --- |
| The events | The 10 sketched together (see `game/data/README.md`, "Placeholder events"), all placeholders. The spec's first playable asked for 6; with about 1 to 3 events a run, 10 keeps back-to-back runs from repeating. |
| More events | 10 more, written by another agent from a writer prompt the owner gave it. The owner found that it "got the instructions wrong": its stories listed what each choice does and stated the odds ("about half the time"). Their stories were rewritten to hint instead, keeping the agent's choices and results, so there are 20. |
| Outcomes | Hidden. A choice shows only its label, plus its salvage cost and the tag it needs. In the owner's words: "just because the game is on the phone doesn't mean it should be handholding experience. I'm much more interested in old-school design where reading matters." The story text carries the hints, such as "if it still listens to them" for a code that works 3 times in 5. |

Taken as defaults, not raised by the owner (numbers are in `Rules` or `events.json` and are placeholders):
- **Shape of an event.** 2 to 4 choices. A choice can cost salvage, paid when it is chosen, and can need a fitted piece with a tag. It has 1 or more results, one picked by weight. A result can change salvage, hull and max hull, give a piece to the cargo hold (named, or random by rarity or kind), upgrade a random piece, give a free hull section, or start a fight.
- **Tagged choices** appear only when a fitted piece carries the tag, as FTL's blue options do, and show the tag. A choice the ship cannot afford is shown with its price in red and cannot be taken. Every event has a choice with no cost and no tag.
- **After a choice** the screen shows the result's text, then what actually changed (numbers, and pieces by name). The button names where it leads: "Continue", "Fight · Feral Harvester" or "Hull section".
- **Limits.** Event damage never takes the hull below 1, so a story choice cannot end the run. No event takes a piece away; whether crew can be lost stays an open question.
- **Fights from events** are against the named enemy, with that enemy's normal reward: a hard enemy gives a rare piece and a hull section. An event cannot start a boss fight. The event's node is done after the fight's reward.
- **Rolling.** An event is rolled when its node is chosen, all of the sector's events equally likely, and one repeats only once the others have been met in the run.
- **Events on the map.** Event nodes have their weight (20) on the rolled rows.
- **No refitting** during an event, as at a shipyard.
- **Save.** The save schema goes to 8 (a run remembers its event and the events it has met), so a run saved by the third pull request is set aside and a new run starts.
- **Revising the agent's events.** Besides the stories, a story no longer names a choice that needs a tag, since a player without the tag never sees that choice. Marooned Choir's elder costs only her 40 salvage, not also 5 max hull, because Humming Wreck already gives the same crew member for max hull.
- **Balance.** The three bots, which take an event only when nothing else they want is reachable and then the first open choice, met 0.6 events a run and won 67% (plain, was 62%), 52% (smart, was 44%) and 64% (rare-only, was 59%) of 400 runs with the first 10 events, and 65%, 46% and 61% with all 20 (some of the new choices they take first cost hull or max hull). Event nodes take the place of some fights (about 4.6 fights won a run), so a path costs less hull. Players who chase fights and gambles in events will see something different from the bots.

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