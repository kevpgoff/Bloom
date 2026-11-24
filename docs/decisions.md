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
| Music | Voltz Supreme's "Analog Ambience" pack, which the owner bought on itch.io. The music follows four moods: voyage (every screen outside a fight), fight, boss, and a short sting when a run is won or lost. The voyage music pauses during a fight and resumes after it. Every track that fits the tone ships at full length (20 files, 65 minutes, about 38 MB). The boss music is the most driving of the pack: Simple Synth and Black Energy were too calm for it, so Darkness and Long Highway replaced them. Creepy is left out as too dark. Trimming tracks is fine. The license doesn't cover sharing the files, and the repo is public, so git ignores both the zips and the files built from them. `tools/import-music.sh` builds the game's files from the zips. Which track plays where is a first guess, to change by ear in `tools/music-tracks.tsv`. Decided 2026-11-22. |
| Sound effects | Our own synth sounds, not bought or downloaded samples. Each sound is a recipe that `tools/SfxGen` renders to a file; the recipes and the files are both in the repo. The first character, warm analog with glass accents, was heard and rejected: it sounded like a cheap mobile casino game. Instead, each ship class gets a sound set in the character of what the ship is grown from. The Seedling's set is Wood and water, which is made of things grown and made by hand: wooden bars, a thumb piano, plucked strings, seed pods, leaves and a glass bowl of water. Taken as defaults, to confirm: the Seedling's set is also the base set, which every ship uses for the sounds that are not its own. Only a ship's own sounds change with the ship: card plays, its hits on the enemy, block, energy, focus, growth and repair, and hits on the ship. The turns, the enemy's sounds, the end of a fight and every other screen are shared. Each sound keeps its length and level in every set, so a fight reads the same on any ship. Warm circuits (an old analog synth) is the Heliotrope's set, which the game plays for that ship since 2026-11-23. The owner heard its 22 sounds on the sound board and approved them the same day (see The Heliotrope below). The living ship (beetles, creaks, breath and a voice) is the start of the Fairy Ring's. The Comb has no set yet. Everything gets a sound, in two steps: first combat, then the interface and every other screen. Every button gets a very soft tick, and a tap that commits something gets a slightly fuller sound. A card's play sound depends on the tag of the piece it comes from. Not wanted: a swell when a boss arrives, a sound on every change of screen, ticks while a piece is dragged. The owner judges every sound by ear on a private sound board page, then in the game. Decided 2026-11-23. Taken as defaults, to confirm by ear: tuned sounds use only the notes D, A and G, and each sound's level is set against the music as a phone speaker plays it. |
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

## The boss's parts, built on 2026-11-23

The last of the sector map's 5 pull requests. The owner had decided the rules in the round of questions on 2026-11-22 (see "Sector map decided on 2026-11-22"), then said to go ahead after playing the events.

Decided by the owner:

| Topic | Decision |
| --- | --- |
| Boss | 2 or 3 parts. The body and each working part act every turn, each with its own pattern. A part whose hull reaches 0 is disabled and stops acting. The fight is won when the body's hull reaches 0. |
| Targeting | Tap the body or a part to aim. Every attack hits it until the aim is changed. A fight starts aimed at the body. |

Taken as defaults, not raised by the owner (numbers are placeholders):
- **Order.** The body acts first, then each working part from left to right. The enemy's turn stops as soon as the ship's hull is gone.
- **One block.** The enemy has a single block, as the ship does. The body's and the parts' block go into it, and it protects whatever is hit. A part that blocks therefore shields the whole enemy, which makes it a target.
- **Damage past a part's hull is lost.** It does not carry on to the body. The aim goes back to the body when its part is disabled, and the next damage, even from the same card, hits the body.
- **A part's pattern** moves on only while it works.
- **Heavy attacks.** Every heavy attack of the turn marks its lane and the piece it would disable. When two hit the same lane, the second marks the piece behind the first. The Fitting screen marks the lanes of the body's and the parts' heavy attacks before the fight.
- **Saving.** The aim is part of the fight and is saved: aiming is a player action (`CombatEngine.SetTarget`). The save schema goes to 9, so a run saved by the events pull request is set aside and a new run starts.
- **The screen.** Each part has a panel under the enemy with its name, hull and what it does this turn. The aimed part, or the body, wears the reticle's corner brackets. Tapping a part's panel aims at it, and tapping the enemy's panel aims back at the body. Per the no-instruction-text rule, nothing says so.
- **Content.** Any enemy may have up to 3 parts (`Rules.MaxEnemyParts`, what fits across the screen), and the validator checks them like the body. The tests require 2 or 3 on every shipped boss. Whether normal enemies get parts is still an open question below.
- **The Grand Combine.** The body has 56 hull: attack 9, heavy 13 on lane 2, block 6. Its parts are the Thresher Arm (15 hull: attacks of 5, 6 and 7), the Chaff Screen (12 hull: block 3 every turn) and the Baling Ram (16 hull: heavy 10 on lane 4, attack 4, heavy 10 on lane 3). Together that is 99 hull, against the old boss's 120, and up to about 20 damage a turn while every part works, against 14.
- **Balance.** Over 400 runs, with bots that aim at the part that hits hardest until it is disabled, then the next: plain 66% (was 65%), smart 48% (was 46%), rare-only 61% (unchanged). The boss fight lasts about 13 turns (was 14). Bots that only hit the body win 17%, 8% and 17%, and bots that aim at the part with the least hull left win 44%, 21% and 39%. Aiming matters: rushing the body leaves every part hitting until the end.

## Low hull warning decided on 2026-11-23

Before this, only the small white "Hull X / Y" numbers showed that the hull was low.

| Topic | Decision |
| --- | --- |
| Feel | Subtle and subconscious, not in your face: felt rather than read, with no text, nothing sudden and nothing over the screen's content. The first version's glow on the screen's edges was "way too much", so it was toned down to about a third. The owner approved the toned-down version. |

Taken as defaults, not raised by the owner (all numbers are placeholders, in `Visuals/HullWarning.cs`, `Visuals/BackdropMotes.cs` and `Screens/ShipGridView.cs`):
- The warning starts below half of the maximum hull and is at full strength at a fifth. Every part of it eases in and out over about 2.5 s.
- The hull's numbers turn from white to amber, then coral, on every screen that shows them. Only the hull is colored: the combat screen shows Block in a label of its own, and the status lines are now rich text labels.
- Behind every screen, the horizon and the screen's edges warm faintly and breathe slowly, once every 5.5 s at first and every 3.4 s at full strength.
- In a fight, the ship's open deck darkens toward red, like emergency lighting. Below about a third of the hull, the hologram dims for a moment every 4 to 9 s.

## The Heliotrope, built on 2026-11-23

The second ship class (Sunwrights), built before the owner has reviewed its design. Nothing here is confirmed by the owner yet, except the sounds of its sound set.

Taken as defaults, to confirm (all numbers are placeholders):
- **Hull.** A 6×3 rectangle (18 cells) at `hullOrigin` `{ "x": 0, "y": 2 }` (rows `y2`–`y4`), leaving 2 rows free in front and 3 behind so every hull section can attach on either side, matching the Seedling's slight forward bias. Because all 6 columns are covered, heavy attacks aimed at lane 0 hit lane 0 directly.
- **Starting layout and deck.** 6 Common pieces using 16 of 18 cells (2 free) and adding 11 cards: `Burning Glass` #1 (`R90`, front lane 0), `Sun Sail` (front lanes 1–3), `Burning Glass` #2 (front lanes 4–5), `Lens Grinder` (`R90`, lane 1, giving `+2` damage to `Burning Glass` #1), `Bridge` (middle lanes 4–5), and `Sun Shutter` (back lanes 2–5). Its non-Core tags are strictly `Solar` and `Weapon`, keeping reward and dock affinity on Sunwrights pieces, leaving key Solar and Weapon pieces exposed across all 6 front lanes, and splitting its heavy attack across two separate pieces so losing any one piece never leaves the ship with only Rams.
- **Two new placeholder Sunwrights pieces.** Added so the Heliotrope stays `Solar` + `Weapon` without borrowing `Hull Plating` or duplicating the Seedling's `Light Lance` attack, while keeping early fights in the spec's 4–6 turn range and letting the ship spend carried energy on 3-cost shots: `Sun Shutter` (`####`, Solar, Common, 3× `Shutter` at 1 energy: block 7, upgrading to 10) and `Burning Glass` (`##`, Weapon, Common, 1× `Sear` at 3 energy: deal 14, upgrading to 20). Both are marked `"placeholder": true`.
- **Special rule in data.** Declared on `ShipClassDef` as an optional polymorphic `"rule"` object whose first property is `"type"`, matching `Effect` and `EventOutcome`: `{ "type": "keepEnergy" }` (`KeepEnergyRule`). Future classes can add parameterized rules under `ShipRule` without changing `ShipClassDef`. The save shape does not change (`RunState` already saves `ShipClassId` and `CombatState.Energy`), so schema version stays at `9`.
- **Edge cases of `keepEnergy`.** Energy gained from cards (`Unfurl`, `Glide`) and not spent stays for the next turn just like base turn energy. Stored energy has no cap (matching `Block` and `PendingFocus`). Every new fight still starts at `Rules.EnergyPerTurn` (3) with no carryover between fights. A disabled bridge reduces draw by 1 and does not stop energy carryover.
- **Testing launch flag.** `GameRoot` accepts `-- --ship=<id>` in debug builds. If a saved run belongs to another class, `GameRoot` copies `user://run.json` to `user://run.<class id>.json` (named after the replaced run's class) and logs where the copy is before starting the requested ship. An unknown id logs an error and uses the default ship (`seedling`). Resumed runs without `--ship=` keep their own class. With `--fx-demo`, the flag chooses the ship class that the demo plays; the demo's run is still kept in memory and never saved. Since the hangar (below), the flag only chooses the demo's ship: in the game itself the ship is chosen in the hangar, and the copy of the replaced save is gone.
- **Balance.** Measured over 400 runs per bot (`TARGET=threat`): Heliotrope wins 59% (plain, Seedling 66%), 50% (smart, Seedling 47%) and 57% (rare, Seedling 61%), with early fights averaging 4.1 turns (Seedling 4.7; early duels 3.8–4.5) and 2.0–2.1 hull lost. In single-fight duels from full hull with the starting layout, it wins 82%–100% against every hard enemy and 60% against the Grand Combine (avg 19.1 turns, p90 26), with every late and hard enemy's p90 within 3 turns of the Seedling's. Because the bots never refit before a fight and never save energy on purpose, these numbers understate the ship in a player's hands in both ways: on an earlier single-weapon layout, simply refitting against Strip Miner (turning `Burning Glass` upright in lane 5, `Lens Grinder` at `(3,3)`, `Bridge` upright at `(0,3)`, and `Sun Shutter` at `(1,4)`) raised its 200-fight duel win rate from 22% to 99% (9.4 turns avg). The owner's own play has to settle whether the ship is fair.
- **Sound set.** The Heliotrope names the sound set `heliotrope` (`"soundSet"` in `ships.json`), which is Warm circuits: an old analog synthesizer in a greenhouse. The game plays the set's version of each of a ship's own sounds and the base set for the rest (the turns, the enemy's sounds, the end of a fight). The set holds all 22 of a ship's own sounds. The owner heard 7 of them among the first 8 Warm circuits sounds. The other 15 were written afterwards to match those 7 and the Seedling's sounds in level and length. The owner heard them on the sound board and approved them on 2026-11-23 ("The takes are good"). Still a default, to confirm: the Heliotrope's version of the end of a won fight is not in the set, because every ship shares that sound; it is kept aside as the take `heliotrope-fight-won`.

Questions for the owner:
- Confirm adding the two placeholder Sunwrights pieces (`Sun Shutter` / `Shutter` at 1: block 7 → 10 and `Burning Glass` / `Sear` at 3: deal 14 → 20), or prefer a different starting mix?
- Confirm `hullOrigin.y = 2` (2 rows free in front, 3 behind), or prefer `y = 3` (3 in front, 2 behind)?
- Confirm no cap on stored energy, or should `KeepEnergyRule` enforce a maximum?
- Confirm that card-gained energy (`Unfurl`, `Glide`) carries over just like base turn energy?
- Is `-- --ship=heliotrope` (desktop debug flag; phone requires baking into `command_line/extra_args`) enough for testing until a ship choice screen exists, or do you want a minimal in-game switcher on the phone? Answered on 2026-11-23: the owner asked for the hangar (below).
- `Sun Shutter` and `Burning Glass` currently reuse the `Sun Sail` and `Light Lance` fallback icons; confirm leaving icons out of scope for now?
- Should the end of a fight also change with the ship? The Heliotrope's version of a won fight exists as a take; a lost fight has none.

## The hangar, decided on 2026-11-23

The owner asked for a ship select screen "so we can better design and test new ships", one that supports "current and potential future gameplay options". The owner decided these in three rounds of questions. The work comes in 3 pull requests, each playable, with a stop after each for the owner to play it on the phone: the hangar with ship, loadout and seed; the test panel for debug builds; the profile with records, locks and feats.

| Topic | Decision |
| --- | --- |
| Flow | The hangar shows before a new run only. A launch of the game with a voyage in progress still goes straight back into it, so a short sitting costs no extra tap. There is no title screen. |
| Layout | A "bay rail": the chosen ship large at the top as a hologram with its starting pieces, its details under it, a row of hull silhouettes at the bottom to switch ships with one tap, then the main button. |
| Voyages | One at a time. The map has an Abandon button that needs a second press. Abandoning ends the voyage and opens the hangar. The other option was one saved voyage for each ship. |
| Run options | Built now: the ship, loadout variants and seeded runs. The design leaves room for voyage levels. Optional rules for a run were not wanted. |
| Loadouts | A loadout is a named set of starting pieces. It changes only the pieces: the hull, the hull points and the rule stay with the ship class. The Seedling gets a placeholder second loadout from existing pieces, around Garden and Shield. |
| Records and locks | Built in the third pull request: a profile kept between runs, with voyages launched and won for each ship and loadout and the furthest point reached, and ships that open through feats. The first lock is the spec's example: the Heliotrope opens after a won voyage with the Seedling. |
| Locked ships | Everything visible, dimmed: a locked ship can be selected, and its hologram, pieces, deck and rule are shown. One line states the feat as a fact. The main button is disabled and reads Locked. |
| Typed seeds | Practice only. A voyage from a typed seed is a normal saved voyage, but the profile ignores it: no record, no feat. |
| Test panel | Built in the second pull request, for debug builds only: start a run at a chosen node (a boss, a hard fight, a shipyard, a dock, a grove or a named event). Test runs go to a save of their own, so they never replace the real voyage. Not wanted for now: fighting a chosen enemy, starting extras, and a way to hide unfinished ships. |

Taken as defaults, not raised by the owner (all numbers are placeholders):
- **The first pull request** has the hangar, loadouts, seeds, the Abandon button and the new launch flow. Without a saved run the game opens on the hangar and no longer starts a Seedling run by itself.
- **What the hangar shows.** The ship's name and commune; its loadouts, by letter and name, even when there is only one, so that the rows below do not move from ship to ship; hull points, cells, free cells and cards; the ship's rule as a sentence; what the ship is grown from; and three tabs. Pieces lists the starting pieces, and a tap on a piece of the hologram shows that piece's cards and bonus. Deck lists the starting deck. Voyage holds the seed; records and, later, voyage levels go there. Per the no-instruction-text rule, nothing says how to use it. Launch names the ship.
- **Ship text in the data.** `ships.json` gains `commune` and `grownFrom` for each ship, with the spec's wording. The rule's sentence is written in the game (`ShipText`), as bonuses are.
- **The Orchard loadout.** The example given to the owner was two Moss Reactors, Hull Plating, Reed Screen, Orchard Keeper, Spore Singer and the bridge. That deck's only damage would be the bridge's two Rams, so one Moss Reactor was swapped for a Light Lance and an Arc Cutter. It has 22 of 25 cells used and 17 cards. Its balance has not been measured; the owner's play and, later, its records are to judge it. See `game/data/README.md`.
- **Seeds.** A seed is text. The hangar offers a random code of 8 characters, from letters and digits that are hard to mistake for each other (no I, L, O or U), and the player may type any text of up to 16 characters in its place. Capital and small letters are the same. The text is hashed to the run's 64-bit seed (FNV-1a), so the same text always gives the same voyage. The same seed with another loadout gives the same map. The map shows the seed, and a tap copies it; the end of a voyage shows it too.
- **Abandon.** The first press turns the button coral and makes it read "Abandon voyage?" for 3 seconds; only a second press in that time abandons. The save is deleted. The end of a voyage has a Hangar button in place of New Run, which deletes the finished run's save too, so the next launch opens on the hangar.
- **Remembered choice.** The hangar opens on the ship and loadout launched last, kept in `user://settings.cfg` under `[hangar]`.
- **Music and sound.** The voyage music plays in the hangar and carries on into the voyage. When a run is left for the hangar, the music starts again on a new shuffle, as a new run did before. The hangar has no sounds yet, since interface sounds are a later step of the sound effects.
- **Save.** The save schema goes to 10 (a run remembers its loadout and its seed text), so a run saved before is set aside and the hangar opens. This is the only change to the run's save in the 3 pull requests. The profile will be a file of its own with its own version.
- **The `--ship=` flag** now only chooses the ship of the FX demo.
- **The second pull request** has the test panel. Its defaults:
  - **Where it is.** A debug build has a fourth tab in the hangar, Test, with one button for each start: Off, Map, Skirmish, Hard fight, Boss, Dock, Shipyard, Grove and Event. With Event chosen, a list under them names every event, after "Any event". While a start is chosen, the main button reads, for example, "Test · Heliotrope · Boss" and launches a test run. Off returns it to a voyage. The hangar keeps the chosen start while the game runs, so the same test can be launched again with one press. A release build has none of this and never reads the test save.
  - **What a test run is.** The same run that the ship, loadout and seed would give, with the ship put on the map's first node of the chosen type and that node entered. The path behind it is a real way across the map, so the run carries on from there like any other. The first skirmish is on the first row, so a skirmish test meets an early enemy. When the map has no node of the type, one node of the middle row becomes that type (never the map's dock or shipyard while the row has another node). The ship is the starting ship with no salvage, whatever the row, since starting extras were not wanted. A dock test can therefore show the dock but not buy from it, and a paid choice of an event cannot be taken.
  - **Its save.** A test run is saved after every action to `user://run.test.json`, apart from the voyage. A launch of the game resumes a test run before the voyage. A test save that cannot be read is deleted.
  - **The voyage waits.** On the map of a voyage, a debug build has a Hangar button beside Abandon. It sets the voyage aside without ending it and opens the hangar. While a voyage waits, the Test tab has no Off, so the hangar launches only test runs, and a "Back to voyage" button at the top returns to the voyage as it was.
  - **Ending a test.** During a test run a row above every screen reads "Test run" with the ship's name, and its "End test" button ends the run with one press, also in the middle of a fight. The test's save is deleted and the hangar opens. The map of a test run has no Abandon button for that reason.
  - **No change to the save.** A test run is an ordinary `RunState`; only the file it is saved to marks it as a test. The save schema stays at 10.
- **The third pull request** has the profile, with records and locks. Its defaults:
  - **The file.** `user://profile.json` holds what stays between runs. It has a schema version of its own, which starts at 1, and the run's save does not change (schema 10). A profile that cannot be read is copied to `user://profile.unreadable.json` and a new one starts.
  - **What is recorded.** For each ship class and loadout: the voyages launched, won, lost and abandoned, and the furthest that any of them came (the sector, and the rows of its map that the ship entered). A voyage is counted when it is launched and closed when it is won or lost, or when it is left in progress, which abandons it. Leaving the game does not abandon a voyage.
  - **Practice.** A voyage is recorded only when it is launched from a seed code that the hangar offered. Any other text in the seed field was typed by the player, so the voyage is practice: the main button reads "Practice" and the ship's name, the Voyage tab says so, and the map and the end of the voyage show "practice" after the seed. A test run is never recorded.
  - **Locks.** A ship class in `ships.json` may have an `"unlock"`. The only kind is `{ "type": "winWith", "ship": "seedling" }`: a recorded voyage was won with that ship class, with any loadout. Whether a ship is open is worked out from the records each time, so nothing else is stored for it. The Heliotrope has that lock. The data is refused when a lock names an unknown ship or the ship itself, when no ship is open from the start, or when locks lead back to each other.
  - **How a locked ship looks.** It can be chosen in the rail, and its hologram, pieces, deck and rule show. Its rail tile and its hologram are dimmed, an amber line under the hull numbers reads "Locked · a won voyage with the Seedling", and the main button is disabled and reads "Locked". The end of the winning voyage adds the line "Heliotrope unlocked".
  - **What the Voyage tab shows.** Under the seed: "Voyages 3 · Won 0 · Lost 2 · Abandoned 1" for the chosen ship and loadout, then "Furthest · row 5 of 7" (or "the boss") until the first win with them, or "No voyages recorded".
  - **Locks off.** A debug build has a "Locks off" switch beside the hangar's title, lit while it is on. It is on each time a debug build starts, so that every ship can be tested, and a locked ship then shows undimmed with "locks off" after its lock line. Pressing it shows the ships as a release build does. It was planned for the Test tab, but a locked Heliotrope has one more line of text, which left no room there for a third row of buttons. Voyages launched with the locks off are recorded like any other.
  - **When the two files disagree.** The run and the profile are separate files, so the game can stop between the two being written. At each start the profile is brought in line with the saved voyage: a recorded voyage whose run is already won or lost is closed with that end, and one whose save is gone is closed as abandoned. The profile also keeps the voyage that ended last, so that "Heliotrope unlocked" still shows when the game restarts on the end of that voyage.
  - **Not built.** Locks on loadouts, a way to reset the profile, and records across all loadouts of a ship.
- **Room for later.** A voyage level would be a field of `RunSetup`, a number in the profile's record for a ship, and a control on the Voyage tab. No field exists for it yet. The rail shares the screen's width among the ships, which works for the spec's 4 classes; more would need it to scroll.

## Content expansion, decided on 2026-11-23

The owner played a full run and found the game fun but short on content: too few pieces, pieces that play alike, and a run that ends too soon. The owner decided:

- **Items are passive pieces.** A passive piece adds no cards and gives a standing effect while it is fitted. It takes room on the ship like any piece. There are no relics that sit outside the grid and no single-use items.
- **Scope.** The expansion includes sectors 2 and 3 and ship classes 3 and 4 (the Fairy Ring and the Comb).
- **Order.** A quick set of pieces that use today's effects, then passive pieces, then the three-sector run, then cards whose numbers count things on the grid. Each is a pull request with a stop for the owner to play.
- **Hull between sectors.** Damage carries over, and each boss before the last repairs half of the maximum hull. The number is a placeholder.
- **Difficulty.** A full run of three sectors should be about as winnable as a run of one sector was. Sector 1 becomes an easier opening act and the difficulty climbs through sectors 2 and 3.
- **Testing later sectors.** In debug builds, the Test tab may start at sector 2 or 3 with a preset ship. This is a narrow exception to "no starting extras" in the hangar's rulings.

### Content guard and piece set A, built on 2026-11-23

The first step. Nothing in it changes the rules of play or the save.

Taken as defaults, not raised by the owner:

- **Strict content files.** The content files are read through their own JSON context, which refuses a property that no definition has, a comment and a trailing comma. Saves and the profile keep the lenient context, so that an old save still reads as "wrong version" and not as "broken".
- **A lint for the content.** `ContentLint` reports content that loads but is very likely a mistake; `game/data/README.md` lists what it checks. It is separate from the validator because the small test content breaks its checks on purpose.
- **Tests check structure, not counts.** The tests that pinned 18 pieces, 19 cards, 12 enemies and so on now ask for minimums per tag, rarity, pool and sector. The owner's sizes for sector 1 (4 early, 4 late and 3 hard enemies and a boss) are no longer pinned: the test asks every sector for at least 3, 3, 2 and 1, and for at least 4 events. Every event choice is now played on every ship, not only on the Seedling.

Taken as defaults, to confirm (all numbers and names are placeholders):

- **Piece set A.** 15 pieces and their 15 cards, listed in `game/data/README.md`. They use only today's effects. The pieces that can be offered go from 13 common, 2 uncommon and 2 rare to 20, 6 and 6, so the rare slot after a hard fight no longer shows the same two pieces. Garden goes from 1 piece to 4, Drone from 1 to 5, Shield from 2 to 5 and Solar from 3 to 4.
- **Stacking bonuses.** The three new crew bonuses (Drone Minder's, Reed Piper's and Root Warden's) have new names, so they add to the existing bonuses on the same piece. A Weapon piece that touches Lens Grinder, Choir Elder and Reed Piper deals 6 more damage with each card.
- **Cards at no cost.** Shell, Nip, Sting, Corona, Signal and Shelter cost 0. A ship that collects them plays most of its hand every turn. Whether that is too strong is for the owner's play to settle; no balance tool exists in the repository yet.
- **No card of the set draws.** Found while writing the set, and measured with small programs that play the real rules: the discard pile is shuffled back into the draw pile in the middle of a turn (`CombatEngine.Draw`), so a card that was played can be drawn and played again in the same turn. The game as it was before this set already has a ship that wins on its first turn:
  - On a ship stripped down to the bridge and a Mirror Kite, the kite's two Glide cards (0: gain 1 energy, draw 1) draw each other without end, so the energy has no limit. That ship still plays at most 3 Rams in a turn, because a Glide draws one card and each Ram that is played stays out of the hand.
  - With a card that draws two (the Spore Singer's Chorus, or the upgraded kite's Glide), every card that is played comes back. A ship of the bridge, the Spore Singer and a Mirror Kite destroyed the Feral Harvester, the Strip Miner and the Grand Combine on turn 1 in 500 of 500 tries each.
  - Such a ship needs a Mirror Kite, which the Mirror Field event gives to any ship with a Solar piece, and it costs the pieces that are composted to strip the ship: at least 4 on the Seedling's first loadout, since the cargo hold keeps 3.
  - A first draft of the set made this easier: a card that draws 1 for 1 energy (Nip, Bulwark and Shelter in that draft) pays for itself next to a Glide, and the bridge, a Mirror Kite and the common Mite Swarm won most fights on turn 1 with nothing upgraded. So no card of the set draws now. The rule itself is a question for the owner (below).
- **Commune of the Shield pieces.** Bark Plate, Tide Wall and Storm Shell are marked `tide_orchard`, since the spec gives the Seedling Garden and Shield as main tags. The two older Shield pieces have no commune. The rules do not read it.
- **No icons.** The new equipment shows the icon of its tag, and the new crew show the Spore Singer's. Icons come in a later batch.

## Proposed in the spec, not yet confirmed

- About 8 nodes and 25 minutes for each sector. (Three sectors for each run was decided on 2026-11-23; see "Content expansion".)
- Grid of at most 6 columns by 8 rows. About 20 cells at the start and about 40 at the end of a run.
- Each column is a lane. A heavy attack disables the frontmost working piece in its lane.
- 3 energy and 5 cards each turn. 60 hull points at the start of a run.
- Disabled pieces come back at no cost after a fight.
- Renown fills an unlock track. 4 ship classes and 10 voyage levels for each ship at launch. (Ship classes 3 and 4 are in the scope of the content expansion of 2026-11-23; "at launch" and the voyage levels are not confirmed.)
- Setting names: the Bloom, Sunwrights, Tide Orchard, Mycelial Choir, Tinker Hives.

## Open questions

- Price of the game.
- Final name of the game and of things in the setting.
- Whether some shields protect a single lane.
- Whether disabled pieces stay disabled after a fight.
- Whether normal enemies have parts to target.
- Whether crew can be lost for the rest of a run.
- Whether a card that was played this turn can be drawn again in the same turn. Today it can, and a small ship with a Mirror Kite and the Spore Singer wins any fight of sector 1 on its first turn (see "Content guard and piece set A"). Options: keep played cards out of the shuffle until the turn ends, which ends every such loop because a card is then played at most once a turn; or keep the rule and take the draw or the energy off Glide, which leaves other pairs of cards to watch for; or treat the loop as a build that the player has earned.
- Team and schedule for the first playable version.