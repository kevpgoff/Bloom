# Bloom

A paid, single-player card roguelite for phones, where the ship is the deck (working title). Every piece fitted into the ship's grid adds cards to the combat deck. The full design is in the [design spec](https://claude.ai/artifact/8WTEGCWRg47LQEfRJWbJC1), and the decisions the owner has made so far are in [`docs/decisions.md`](docs/decisions.md).

## Prerequisites

- .NET SDK 10.0 (pinned in `global.json`). The rules and the game target .NET 8, Godot's minimum. Android builds compile the game ahead of time on .NET 10.
- Godot 4.7.2, **.NET build**, only to open and run the game. It must match the `Godot.NET.Sdk` version in `game/Bloom.csproj`.
- For Android builds, the tools in [Android debug build](#android-debug-build).
- For the music, ffmpeg (with libvorbis) and unzip, and the pack's zips; see [Music](#music).

## Commands

```sh
dotnet build Bloom.sln          # builds the rules, the tests and the Godot game assembly; no editor needed
dotnet test                     # runs the rule tests, including validation of game/data/*.json
godot --path game               # runs the game (portrait window, 405×720)
godot -e --path game            # opens the project in the editor
godot --path game --headless --quit-after 60   # smoke test: should print "GameRoot: Content loaded: pieces 16, …" and a screen line ("MapScreen: …" on a new run)
godot --path game -- --fx-demo                 # debug builds: plays the first fight by itself to show the combat feedback; add =lose to show a defeat. The run stays in memory, so the save is untouched
tools/import-music.sh                          # builds the game's music from the zips in music/; see Music below
tools/android-debug.sh                         # exports a debug APK, installs it on the phone connected by USB and starts it; --log follows its log, --no-install only exports
```

CI (`.github/workflows/ci.yml`) runs the first two commands on every pull request, on GitHub's 1-vCPU runner to keep the cost low. Pull requests that change only Markdown files or `docs/` are skipped, and the Godot editor is never run.

## Android debug build

`tools/android-debug.sh` exports the `Android` preset in `game/export_presets.cfg` to `build/android/bloom-debug.apk`, then installs and starts it with adb. The build is arm64 only and doesn't use Godot's Gradle build. The game's C# is compiled ahead of time (NativeAOT) into `libBloom.so`, so the phone starts no .NET runtime. One-time setup on a new machine:

1. A JDK, 17 or newer. Godot recommends 17; Ubuntu's OpenJDK 25 works too.
2. The Android SDK in `~/Android/Sdk`: unzip the [command-line tools](https://developer.android.com/studio#command-line-tools-only) to `cmdline-tools/latest`, then run `sdkmanager --licenses` and `sdkmanager "platform-tools" "build-tools;35.0.1" "ndk;29.0.14206865"`. The NDK (a 784 MB download, 2.4 GB installed) links the compiled game; the script uses the newest one in `~/Android/Sdk/ndk`, or `ANDROID_NDK_ROOT` if it is set. CMake and the SDK platforms are only needed for a Gradle build.
3. The Godot 4.7.2 .NET export templates in `~/.local/share/godot/export_templates/4.7.2.stable.mono/`. The editor's Export dialog can download them.
4. In the editor, Editor Settings > Export > Android: set Java SDK Path and Android SDK Path.
5. A debug keystore at the path in Editor Settings > Export > Android > Debug Keystore. The editor makes one when the Android settings are first set in the editor; from a terminal, run `keytool -genkey -keystore <that path> -storepass android -alias androiddebugkey -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname "cn=Godot, ou=Godot Engine, o=Stichting Godot, c=NL"`.
6. On the phone, turn on Developer options and USB debugging, connect it by USB and allow this computer.

Keystores and passwords never go in the repo. Godot keeps preset passwords in `game/.godot/export_credentials.cfg`, which is ignored.

On the phone, the game logs to `adb logcat` under the tag `godot`, and its files (`run.json`, `settings.cfg`) can be read with `adb shell run-as com.kevpgoff.bloom ls files`. At startup Godot logs `Can't open dynamic library: libmonosgen-2.0.so`: it always tries the Mono runtime first, then loads the compiled game. That error is expected. To build with Mono instead, set `dotnet/android_use_linux_bionic` to `false` in the preset. Launch arguments such as `--fx-demo` can't be passed to the installed game: Godot ignores them. Only the preset's `command_line/extra_args` adds arguments, and it is baked into the APK.

## Music

The music is Voltz Supreme's ["Analog Ambience"](https://voltzsupreme.itch.io/analog-ambience), a paid pack. Its license doesn't cover sharing the files, and this repo is public, so the repo holds neither the pack nor the music built from it. Without them, the game runs in silence and logs `MusicDirector: no music in res://audio/music`. To add the music:

1. Download the pack's three zips from itch.io into `music/` at the repo root.
2. Run `tools/import-music.sh`. It cuts and encodes the tracks listed in `tools/music-tracks.tsv` into `game/audio/music/<mood>/`, brings each to the same loudness, and then lets Godot import them. An Android export made afterwards includes them.

To change which track plays where, or to cut a track, edit `tools/music-tracks.tsv` and run the script again. The moods are voyage (every screen outside a fight), fight, boss, and two stings for a run that is won or lost. `game/scripts/Audio/MusicDirector.cs` plays them. The credit the game owes is in `game/audio/CREDITS.md`.

## Layout

```
Bloom.sln                     solution; also the one Godot builds (dotnet/project/solution_directory="../")
src/Bloom.Core/               the game rules in plain C#, with no Godot dependency
  Rng/ Grid/ Pieces/ Cards/ Combat/ Map/ Run/ Save/ Content/
tests/Bloom.Core.Tests/       xUnit tests for the rules and the shipped data
game/                         the Godot project (res://)
  Bloom.csproj                game assembly; references Bloom.Core
  data/                       content as JSON; see game/data/README.md
  audio/                      the music, built by tools/import-music.sh and not committed; its credits
  scenes/ scripts/ art/
docs/decisions.md             decisions made so far
```

## Architecture in one paragraph

`Bloom.Core` owns every rule and all state. A run is one `RunState` object that serializes to JSON and holds its own random number generator (`Pcg32`). Saving after every action therefore needs only one thing: every player action goes through `RunSession.Do`, which writes the state through an `ISaveStore`. The combat rules also report what happened, in order, as `CombatEvent`s (`RunSession.LastEvents`); these are not saved, and the game uses them only for feedback. The Godot project is a thin layer on top. `GameRoot` (an autoload) loads content from `res://data`, resumes or starts a run, and saves to `user://run.json`. Screens draw the state and forward input. Rules stay out of them, so that the tests cover everything that matters.

## Status

A run is one sector: a branching map of about 20 nodes in 7 rows, ending in a boss. A new run opens on the Map screen. The player picks a node in the next row. Choosing a fight commits to it and shows its enemy on the Fitting screen, so the ship can be refitted against its heavy lanes before the fight starts. A won fight pays salvage and offers 1 of 3 pieces, and a hard fight's offer always includes a rare piece and is followed by a free hull section. A grove repairs 30% of the hull or upgrades a piece, a dock trades pieces and sells repairs, a shipyard attaches one of 3 hull sections wherever the player puts it, and an event tells a short story with 2 to 4 choices whose outcomes are found only by choosing. The boss has 3 parts that each act every turn; the player taps the body or a part to aim, and beating the body wins the run. The ship can be refitted from the map at any time. The game saves after every action and resumes at the same point. Saves are written to `user://run.json` (on Linux, `~/.local/share/godot/app_userdata/Bloom/run.json`); a save from an older schema is copied to `user://run.unreadable.json` and a new run starts.

| Area | State |
| --- | --- |
| Grid, shapes, rotation, hull sections | Done, tested |
| Fitting, adjacency, bonuses, lanes, cargo | Done, tested |
| Deck from fitted pieces | Done, tested |
| Combat turn cycle (energy, draw, block, damage, heavy attacks that disable pieces) | Done, tested |
| Card effects `repair`, `focus` and `grow`, and adjacency bonuses (`damageBonus`, `startGrown`) in combat | Done, tested; rulings in `docs/decisions.md` |
| Rewards (1 of 3 pieces) and refitting (move, turn, fit, unfit, compost, undo) between fights | Done, tested; rulings in `docs/decisions.md` |
| Sector map: generation, choosing nodes, skirmishes, hard fights, the boss, groves (repair), salvage from fights and composting | Done, tested; rulings in `docs/decisions.md`. One sector per run (`Rules.SectorsPerRun`) |
| Docks (buy, sell, repair) and piece upgrades at groves, with an upgrade written for every card | Done, tested; rulings in `docs/decisions.md` |
| Shipyards: 1 of 3 hull sections, attached where the player chooses. Hard fights and bosses give a free section after their reward. Heavy attacks aimed past the hull's edge hit its edge lane | Done, tested; rulings in `docs/decisions.md` |
| Events: 20 placeholder stories, each with 2 to 4 choices. A choice can cost salvage or need a fitted piece with a tag, and can give or take salvage, hull, max hull, pieces, an upgrade or a hull section, or start a fight | Done, tested; rulings in `docs/decisions.md` |
| The boss's parts: the body and each working part act every turn with their own patterns, a part at 0 hull is disabled, and the player aims at the body or a part | Done, tested; rulings in `docs/decisions.md` |
| Renown, unlocks, logbook | Not started |
| Art | Option A, "Holo Grove" (see `docs/decisions.md` and `game/art/README.md`). Done: palette, Saira font, ship drawing, icons, card, panel and button frames, backdrop, and the enemy shown large in a target-lock reticle |
| Combat screen | Done: enemy and intent, a boss's parts (hull, intent, tap to aim; the aim wears the reticle), ship grid, hand, End Turn, defeat or voyage complete and New Run |
| Reward screen | Done: 3 offered pieces with their shapes and cards, Take and Skip |
| Fitting screen | Done: drag and drop with the piece floating above the finger, fit and bonus preview, tap to select and tap again to turn, cargo hold, Compost (for salvage), Undo. Opened from the map or a dock it goes back there; after choosing a fight it names the enemy, marks its heavy lanes and starts the fight. Upgraded pieces have "+" after their names |
| Combat feedback | Done: particles, screen shake and haptics for every card effect, enemy action and the end of a fight (`game/scripts/Feedback/`). Shake strength and haptics are set in `user://settings.cfg`; there is no settings screen yet. Timings are placeholders in `CombatFeedback.Tuning` |
| Music | Done: Voltz Supreme's "Analog Ambience" in four moods: voyage, fight, boss, and a short sting when a run is won or lost (see [Music](#music)). The voyage music pauses during a fight and resumes after it, and the music pauses while the phone shows another app. The volume is set in `user://settings.cfg`, and the fade times are placeholders in `MusicDirector.Tuning`. Before release, the game must show players the credit "Music by Voltz Supreme", but there is no credits screen yet |
| Map screen | Done: the whole sector, with the path taken lit and the reachable nodes pulsing; tap a reachable node, then Set course (which names it) to go there, and Ship to refit. Node icons are in `art/icons/nodes/` |
| Grove screen | Done: repair the hull, or upgrade one piece, with its cards shown as they would be |
| Dock screen | Done: 3 pieces for sale, hull repair, and the cargo hold's pieces to sell. Ship opens the Fitting screen, where the selected piece can be sold too |
| Shipyard screen | Done: the whole 6×8 grid and 3 hull sections; the chosen one shows as a ghost, green where it attaches, to drag, tap to turn, or move by tapping where it should go. Attach names it |
| Event screen | Done: the event's story and its choices, which show only their labels, with a cost in salvage or the tag a choice needs. A choice needing a tag the ship lacks is not shown. After a choice: what happened and what changed, then Continue (or "Fight · …" or "Hull section") |
| Mobile export presets | Android: a debug preset (arm64, `VIBRATE` only, `data/*.json` included) and `tools/android-debug.sh`; see [Android debug build](#android-debug-build). Tested on a Pixel phone with Android 16 on 2026-11-22: the game runs at 60 fps, saves and resumes, haptics work, and the back gesture quits with the run saved. The layout keeps clear of the camera cutout and the rounded corners (`Platform/ScreenInsets.cs`). The C# is compiled ahead of time: a cold start reaches the fight in about 0.8 s, down from 1.25 s with Mono. The app icon is a placeholder (`art/app_icon/`). iOS is not set up. |
