# Bloom

A paid, single-player card roguelite for phones, where the ship is the deck (working title). Every piece fitted into the ship's grid adds cards to the combat deck. The full design is in the [design spec](https://claude.ai/artifact/8WTEGCWRg47LQEfRJWbJC1), and the decisions the owner has made so far are in [`docs/decisions.md`](docs/decisions.md).

## Prerequisites

- .NET SDK 8.0 (pinned in `global.json`).
- Godot 4.7.2, **.NET build**, only to open and run the game. It must match the `Godot.NET.Sdk` version in `game/Bloom.csproj`.

## Commands

```sh
dotnet build Bloom.sln          # builds the rules, the tests and the Godot game assembly; no editor needed
dotnet test                     # runs the rule tests, including validation of game/data/*.json
godot --path game               # runs the game (portrait window, 405×720)
godot -e --path game            # opens the project in the editor
godot --path game --headless --quit-after 60   # smoke test: should print "GameRoot: Content loaded: pieces 16, …" and a screen line ("FittingScreen: …" on a new run)
godot --path game -- --fx-demo                 # debug builds: plays the first fight by itself to show the combat feedback; add =lose to show a defeat. The run stays in memory, so the save is untouched
```

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
| Mobile export presets | Not set up. When they are, check that `data/*.json` is included in the export, and turn on the Android `VIBRATE` permission (`permissions/vibrate`), without which haptics do nothing. |
