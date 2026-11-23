# Bloom

A paid, single-player card roguelite for phones, where the ship is the deck (working title). Every piece fitted into the ship's grid adds cards to the combat deck. The full design is in the [design spec](https://claude.ai/artifact/8WTEGCWRg47LQEfRJWbJC1), and the decisions the owner has made so far are in [`docs/decisions.md`](docs/decisions.md).

## Prerequisites

- .NET SDK 10.0 (pinned in `global.json`). The rules and the game target .NET 8, Godot's minimum. Android builds of the game target .NET 9, which Godot requires there.
- Godot 4.7.2, **.NET build**, only to open and run the game. It must match the `Godot.NET.Sdk` version in `game/Bloom.csproj`.
- For Android builds, the tools in [Android debug build](#android-debug-build).

## Commands

```sh
dotnet build Bloom.sln          # builds the rules, the tests and the Godot game assembly; no editor needed
dotnet test                     # runs the rule tests, including validation of game/data/*.json
godot --path game               # runs the game (portrait window, 405×720)
godot -e --path game            # opens the project in the editor
godot --path game --headless --quit-after 60   # smoke test: should print "GameRoot: Content loaded: pieces 16, …" and a screen line ("FittingScreen: …" on a new run)
godot --path game -- --fx-demo                 # debug builds: plays the first fight by itself to show the combat feedback; add =lose to show a defeat. The run stays in memory, so the save is untouched
tools/android-debug.sh                         # exports a debug APK, installs it on the phone connected by USB and starts it; --log follows its log, --no-install only exports
```

CI (`.github/workflows/ci.yml`) runs the first two commands on every pull request, on GitHub's 1-vCPU runner to keep the cost low. Pull requests that change only Markdown files or `docs/` are skipped, and the Godot editor is never run.

## Android debug build

`tools/android-debug.sh` exports the `Android` preset in `game/export_presets.cfg` to `build/android/bloom-debug.apk`, then installs and starts it with adb. The build is arm64 only and doesn't use Godot's Gradle build. One-time setup on a new machine:

1. A JDK, 17 or newer. Godot recommends 17; Ubuntu's OpenJDK 25 works too.
2. The Android SDK in `~/Android/Sdk`: unzip the [command-line tools](https://developer.android.com/studio#command-line-tools-only) to `cmdline-tools/latest`, then run `sdkmanager --licenses` and `sdkmanager "platform-tools" "build-tools;35.0.1"`. The NDK, CMake and SDK platforms are only needed for a Gradle build.
3. The Godot 4.7.2 .NET export templates in `~/.local/share/godot/export_templates/4.7.2.stable.mono/`. The editor's Export dialog can download them.
4. In the editor, Editor Settings > Export > Android: set Java SDK Path and Android SDK Path.
5. A debug keystore at the path in Editor Settings > Export > Android > Debug Keystore. The editor makes one when the Android settings are first set in the editor; from a terminal, run `keytool -genkey -keystore <that path> -storepass android -alias androiddebugkey -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname "cn=Godot, ou=Godot Engine, o=Stichting Godot, c=NL"`.
6. On the phone, turn on Developer options and USB debugging, connect it by USB and allow this computer.

Keystores and passwords never go in the repo. Godot keeps preset passwords in `game/.godot/export_credentials.cfg`, which is ignored.

On the phone, the game logs to `adb logcat` under the tag `godot`, and its files (`run.json`, `settings.cfg`) can be read with `adb shell run-as com.kevpgoff.bloom ls files`. Launch arguments such as `--fx-demo` can't be passed to the installed game: Godot ignores them. Only the preset's `command_line/extra_args` adds arguments, and it is baked into the APK.

## Layout

```
Bloom.sln                     solution; also the one Godot builds (dotnet/project/solution_directory="../")
src/Bloom.Core/               the game rules in plain C#, with no Godot dependency
  Rng/ Grid/ Pieces/ Cards/ Combat/ Map/ Run/ Save/ Content/
tests/Bloom.Core.Tests/       xUnit tests for the rules and the shipped data
game/                         the Godot project (res://)
  Bloom.csproj                game assembly; references Bloom.Core
  data/                       content as JSON; see game/data/README.md
  scenes/ scripts/ art/
docs/decisions.md             decisions made so far
```

## Architecture in one paragraph

`Bloom.Core` owns every rule and all state. A run is one `RunState` object that serializes to JSON and holds its own random number generator (`Pcg32`). Saving after every action therefore needs only one thing: every player action goes through `RunSession.Do`, which writes the state through an `ISaveStore`. The combat rules also report what happened, in order, as `CombatEvent`s (`RunSession.LastEvents`); these are not saved, and the game uses them only for feedback. The Godot project is a thin layer on top. `GameRoot` (an autoload) loads content from `res://data`, resumes or starts a run, and saves to `user://run.json`. Screens draw the state and forward input. Rules stay out of them, so that the tests cover everything that matters.

## Status

The loop of a run, before the sector map: a new run opens on the Fitting screen; the player fights, picks 1 of 3 pieces after a win, refits the ship, and fights the next enemy. A placeholder run is the 4 enemies in `enemies.json`, in order, and winning the 4th wins it. The game saves after every action and resumes at the same point. Saves are written to `user://run.json` (on Linux, `~/.local/share/godot/app_userdata/Bloom/run.json`); a save from an older schema is copied to `user://run.unreadable.json` and a new run starts.

| Area | State |
| --- | --- |
| Grid, shapes, rotation, hull sections | Done, tested |
| Fitting, adjacency, bonuses, lanes, cargo | Done, tested |
| Deck from fitted pieces | Done, tested |
| Combat turn cycle (energy, draw, block, damage, heavy attacks that disable pieces) | Done, tested |
| Card effects `repair`, `focus` and `grow`, and adjacency bonuses (`damageBonus`, `startGrown`) in combat | Done, tested; rulings in `docs/decisions.md` |
| Rewards (1 of 3 pieces) and refitting (move, turn, fit, unfit, compost, undo) between fights | Done, tested; rulings in `docs/decisions.md` |
| A run is 4 fights in a fixed order (`RunState.Encounters`) | Done; replaced when the map exists |
| Sector map | Types only; no generation |
| Docks, shipyards, groves, events, salvage, renown | Not started |
| Art | Option A, "Holo Grove" (see `docs/decisions.md` and `game/art/README.md`). Done: palette, Saira font, ship drawing, icons, card, panel and button frames, backdrop, and the enemy shown large in a target-lock reticle |
| Combat screen | Done: enemy and intent, ship grid, hand, End Turn, defeat or voyage complete and New Run |
| Reward screen | Done: 3 offered pieces with their shapes and cards, Take and Skip |
| Fitting screen | Done: drag and drop with the piece floating above the finger, fit and bonus preview, tap to select and tap again to turn, cargo hold, Compost, Undo, next enemy's heavy lanes |
| Combat feedback | Done: particles, screen shake and haptics for every card effect, enemy action and the end of a fight (`game/scripts/Feedback/`). Shake strength and haptics are set in `user://settings.cfg`; there is no settings screen yet. Timings are placeholders in `CombatFeedback.Tuning` |
| Map screen | Placeholder, not shown |
| Mobile export presets | Android: a debug preset (arm64, `VIBRATE` only, `data/*.json` included) and `tools/android-debug.sh`; see [Android debug build](#android-debug-build). Tested on a Pixel phone with Android 16 on 2026-11-22: the game runs at 60 fps, saves and resumes, haptics work, and the back gesture quits with the run saved. The layout keeps clear of the camera cutout and the rounded corners (`Platform/ScreenInsets.cs`). The app icon is a placeholder (`art/app_icon/`). iOS is not set up. |
