# Bloom

A paid, single-player card roguelite for phones, where the ship is the deck (working title). Every piece fitted into the ship's grid adds cards to the combat deck. The full design is in the [design spec](https://claude.ai/artifact/8WTEGCWRg47LQEfRJWbJC1), and the decisions the owner has made so far are in [`docs/decisions.md`](docs/decisions.md).

## Prerequisites

- .NET SDK 8.0 (pinned in `global.json`).
- Godot 4.7.2, **.NET build**, only to open and run the game. It must match the `Godot.NET.Sdk` version in `game/Bloom.csproj`.

## Commands

```sh
dotnet build Bloom.sln          # builds the rules, the tests and the Godot game assembly; no editor needed
dotnet test                     # runs the rule tests, including validation of game/data/*.json
godot --path game               # opens the project in the editor (or use the Project Manager)
godot --path game --headless --quit-after 60   # smoke test: should print "GameRoot: Content loaded: pieces 7, …"
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

`Bloom.Core` owns every rule and all state. A run is one `RunState` object that serializes to JSON and holds its own random number generator (`Pcg32`). Saving after every action therefore needs only one thing: every player action goes through `RunSession.Do`, which writes the state through an `ISaveStore`. The Godot project is a thin layer on top. `GameRoot` (an autoload) loads content from `res://data`, resumes or starts a run, and saves to `user://run.json`. Screens draw the state and forward input. Rules stay out of them, so that the tests cover everything that matters.

## Status

This pass is scaffolding: the rule models and a basic turn cycle, plus placeholder screens.

| Area | State |
| --- | --- |
| Grid, shapes, rotation, hull sections | Done, tested |
| Fitting, adjacency, bonuses, lanes, cargo | Done, tested |
| Deck from fitted pieces | Done, tested |
| Combat turn cycle (energy, draw, block, damage, heavy attacks that disable pieces) | Done, tested |
| Card effects `repair`, `focus` and `grow`, and applying adjacency bonuses in combat | Not yet: playing such a card throws `NotSupportedException` |
| Sector map | Types only; no generation |
| Rewards, docks, shipyards, events, renown | Not started |
| Godot screens | Placeholders only |
| Mobile export presets | Not set up. When they are, check that `data/*.json` is included in the export. |
