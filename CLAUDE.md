# Bloom

Card roguelite for phones, made with Godot 4.7.2 (.NET) and C#. Read `README.md` for the layout and `docs/decisions.md` for what the owner has decided. The design spec is a Claude doc: https://claude.ai/artifact/8WTEGCWRg47LQEfRJWbJC1

## Commands

- `dotnet build Bloom.sln`: builds everything, including the Godot assembly, without the editor.
- `dotnet test`: must pass before you call any change done. It also validates `game/data/*.json`.

## Rules for code

- `src/Bloom.Core` must never reference Godot. All game rules and state go there, with tests in `tests/Bloom.Core.Tests`.
- Keep `game/` thin. Scripts there draw state and forward input to `RunSession`.
- Rules are deterministic. Don't use `System.Random`, `DateTime.Now` or Dictionary iteration order in rules. Randomness comes only from `RunState.Rng`.
- Every player action goes through `RunSession.Do(...)`, which saves after it. Rule methods validate before they change state, so an action that throws leaves the state unchanged.
- New types that are serialized as roots go in `Save/BloomJsonContext.cs` (saves and the profile) or `Content/BloomContentJsonContext.cs` (the content files, which are read strictly: an unknown property is an error). JSON uses source generation only, because iOS builds with NativeAOT. Bump `RunState.CurrentSchemaVersion` whenever the save shape changes. The profile (`user://profile.json`) is a separate file with its own version, `ProfileState.CurrentSchemaVersion`.
- `Bloom.Core` and the tests build with warnings as errors.
- Rule tests use the small inline content in `TestContent.cs`, not the shipped data, so that rebalancing doesn't break them.

## Content

`game/data/*.json` must be strict JSON with no comments. In an effect, `"type"` must be the first property. Conventions are in `game/data/README.md`. All numbers there are placeholders for testing. Adding an entry needs no change to a test: `dotnet test` checks the structure of the content and runs `ContentLint` on it, and neither pins a count.
