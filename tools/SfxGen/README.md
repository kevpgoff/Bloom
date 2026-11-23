# SfxGen: the game's sound effects

Every sound effect in the game is made here, from a recipe. A recipe is a small JSON file in `recipes/` that describes a sound as a few synth layers. This tool renders each recipe to a WAV file in `game/audio/sfx/`, which the game plays. The recipes and the rendered files are both kept in the repo, because both are ours.

Nobody tuning a recipe has to hear it to know that it is safe to ship: the tool measures each sound and checks it. Whether a sound is *good* is decided by ear, on the sound board.

## Commands

Run the tool through `tools/sfx.sh` from anywhere in the repo.

```sh
tools/sfx.sh render             # write every sound into game/audio/sfx and print its numbers
tools/sfx.sh render card-play   # only the named sounds
tools/sfx.sh stats [id...]      # print the numbers without writing anything; --spectra also draws build/sfx/spectra/<name>.png
tools/sfx.sh play card-play     # render one sound into build/sfx/preview and play it; --ab plays the saved file first, --times 3 repeats
tools/sfx.sh check              # every check on every recipe and take, and on the files in game/audio/sfx
tools/sfx.sh board              # write build/sfx/board.html, one page with every sound and take to listen to
```

`stats` and `play` also take the sounds of a take (see [Takes](#takes)): `tools/sfx.sh stats heliotrope/card-play`, or `heliotrope/` for all of them. `render` refuses them, because a take never goes into the game.

After `render` adds a new file, let Godot import it once: `godot --headless --path game --import`. That writes the `.wav.import` file that is committed beside the sound. `dotnet test` runs the same checks as `check`, so CI fails when a recipe breaks a rule or a rendered file no longer matches its recipe.

## A recipe

```json
{
  "id": "card-play",
  "family": "cards",
  "description": "A card from a crew piece is played: a warm, plain pluck.",
  "level": -6,
  "layers": [
    { "wave": "triangle", "note": "D5", "attack": 3, "decay": 220,
      "unison": { "voices": 3, "detune": 10 },
      "filter": { "cutoff": 4200, "to": 900, "time": 120, "q": 1.1 } },
    { "wave": "sine", "note": "A6", "gain": -14, "decay": 90,
      "pitch": { "from": -2, "time": 30 } },
    { "wave": "noise", "gain": -20, "attack": 1, "decay": 25,
      "filter": { "type": "highpass", "cutoff": 2500 } }
  ],
  "master": { "highpass": 200, "lowpass": 8000, "reverb": { "decay": 250, "mix": -18 } }
}
```

The file must be strict JSON with no comments. A field the tool does not know is an error that names the file and the field, so a typo never passes silently. Times are in milliseconds, levels in dB, pitch offsets in semitones, detune in cents. A pitch is a note name (`"D5"`, `"F#4"`, `"Bb3"`; A4 is 440 Hz) or a number of Hz.

### The sound

| Field | Default | Meaning |
| --- | --- | --- |
| `id` | required | The file name without `.json`, in lowercase words joined by hyphens. The rendered file and the game's cue have the same name |
| `family` | required | `ui`, `cards`, `attack`, `ship`, `flow`, `fitting` or `reward`. It groups the sound board and sets the longest allowed length |
| `description` | required | One line: what the sound is and when it plays. The sound board shows it |
| `level` | required | How loud the sound is on a phone, in dB relative to the music. See [Level](#level) |
| `layers` | required | 1 to 6 layers, added together |
| `master` | see below | What happens to the sum of the layers |
| `variants`, `vary` | 1, none | See [Variants](#variants) |
| `seed` | 0 | Changes the noise and the order of the variants, and nothing else |

### A layer

Each layer is one sound source through its filters and one envelope.

| Field | Default | Meaning |
| --- | --- | --- |
| `wave` | required | A plain oscillator: `sine`, `triangle`, `saw` or `square`. A source without pitch: `noise` or `crackle`. A plucked `string`. A struck object: `wood`, `tine`, `metal`, `glass` or `skin`. See [Strings and struck objects](#strings-and-struck-objects) |
| `note` | required unless noise or crackle | The pitch |
| `notes`, `step` | none, 80 | A run of notes in place of `note`. Each one plays the whole layer, `step` after the one before. A `step` of 0 is a chord |
| `gain` | 0 | The layer's level against the other layers |
| `at` | 0 | When the layer starts |
| `attack` | 2 | The rise from silence, at least 1 |
| `hold` | 0 | How long the layer stays at full level |
| `decay` | required | The fall back to silence |
| `curve` | 5 | The shape of the fall. Above 0 it falls fast at first, like a plucked string; 0 is a straight line; below 0 it starts slowly |
| `pitch` | none | `{ "from", "to", "time", "curve" }`: the pitch glides from `from` to `to` semitones away from the note. Both default to 0 |
| `fm` | none | `{ "ratio", "index", "to", "time", "curve" }`, on a sine only. It makes bell and glass tones; see below |
| `filter` | none | `{ "type", "cutoff", "to", "time", "curve", "q" }`: `lowpass` (the default), `highpass` or `bandpass`, with the cutoff moving from `cutoff` to `to` Hz. `q` above 0.707 rings at the cutoff |
| `unison` | none | `{ "voices", "detune" }`: 2 to 7 copies of the oscillator spread across `detune` cents, for a warmer tone |
| `hardness` | 0.5 | For a string or a struck object: from 0 (a felt mallet, a fingertip) to 1 (a hard mallet, a pick). Harder is brighter, with more of a click |
| `density` | `{ "from": 200 }` | For a crackle: `{ "from", "to", "time", "curve" }`, the clicks per second |
| `drift` | none | `{ "cents", "rate" }`: the pitch wanders slowly and at random by up to `cents`, like an old analog synth. Each unison voice wanders on its own. `rate` (4) is about how often it turns |
| `lfo` | none | `{ "rate", "to", "time", "curve", "pitch", "amp", "cutoff" }`: a wobble `rate` times a second (moving to `to`), of the pitch by `pitch` cents (vibrato), of the volume by `amp` from 0 to 1 (tremolo), and of the filter's cutoff by `cutoff` octaves. Above about 30 a second, a wobble of the volume is heard as a buzz |
| `ring` | none | `{ "ratio", "amount" }`: multiplies the sound by a sine at `ratio` times its pitch. This adds partials out of tune with the note: hollow, metallic, alien |
| `formant` | none | `{ "from", "to", "time", "curve", "shift" }`: shapes the sound like a voice singing a vowel (`a`, `e`, `i`, `o` or `u`), moving from one vowel to another. `shift` moves the voice up in semitones, as a smaller throat would |
| `body` | none | `{ "material", "note", "decay", "dry" }`: plays the layer on an object of that material tuned to `note`, which rings for `decay` after the layer ends. Noise becomes a bowed glass; a crackle becomes seeds in a gourd. `dry` mixes back some of the sound without the object, in dB |

`time` in `fm`, `filter`, `density`, `lfo` and `formant` defaults to the layer's `decay`. A filter's `type` can also be `ladder`: a 24 dB per octave low-pass with a soft saturation, after the old analog synths. Its `q` runs from 0.707 (no resonance) to 12 (nearly sings by itself).

### Strings and struck objects

These waves ring on by themselves, as the real things do, so for them `decay` is how long the note rings (until it is 60 dB down) and `curve` does nothing. `attack` is still the rise, so a long one makes a bowed or swelled sound.

| Wave | What it is |
| --- | --- |
| `string` | A plucked string (the Karplus-Strong method). A soft pluck is a warm gut string; a hard one is a steel string under a pick |
| `wood` | A wooden bar, tuned like a marimba's: its overtones fall near 4 and 10 times the note and die quickly |
| `tine` | The steel tongue of a thumb piano: a pure note with a high, quick overtone |
| `metal` | A free bar or a wind chime, with the clangy overtones of a tubular bell |
| `glass` | A wine glass: clear, with overtones that ring a long time |
| `skin` | A drum head, a seed pod or a gourd: many overtones close together, so the pitch is vague |

A real object's overtones are not whole multiples of its note, so these sound like things and not like a synth. Two or three `unison` voices a few cents apart make the slow beating of a real bell or bowl.


**Bells.** With `fm`, a second sine at `ratio` times the pitch bends the first one's phase. The depth falls from `index` to `to`, so the tone starts bright and ends pure. A `ratio` that is not a whole number, such as 3.5 or 1.41, gives the partials of glass or metal. An `index` near 1 is a soft bell; near 3 it is a clang.

### The master section

These run in the order of the table.

| Field | Default | Meaning |
| --- | --- | --- |
| `drive` | 0 | Gain into a soft saturation, which thickens a hit. 0 turns it off |
| `highpass` | 150 | Removes everything below this many Hz |
| `lowpass` | 10000 | Removes everything above this many Hz |
| `echo` | none | `{ "time", "feedback", "mix", "damp" }`: repeats, each quieter and duller. `mix` is the level of the repeats in dB below the sound |
| `reverb` | none | `{ "decay", "mix", "size", "damp", "predelay" }`: a room. `decay` is how long the tail takes to fall 60 dB; `size` below 1 is a smaller room; `predelay` is how many ms the room waits before it answers, which makes the space sound bigger and the sound further away |
| `fadeOut` | 10 | The fade at the very end of the file |

The length of the file is worked out from the recipe: the end of the last layer (with its body's ring), plus the reverb's predelay and decay or the echo's tail.

## Level

`level` says how loud the sound is against the music, on a phone. 0 is as loud as the music. The sounds so far run from -12 for the old hand being set aside, through -10 for card draws, -8 for the turns and -6 for card plays and gains, to -4 to -2 for hits and -3 for the end of a fight. A sound keeps its level in every ship's set.

The usual loudness measure needs at least 0.4 seconds of sound, and most of these are shorter. So the tool measures the loudest 100 ms instead: it weights the sound the way the ear hears (the K-weighting of ITU-R BS.1770), removes what a phone speaker cannot play (everything below 350 Hz), and scales the file until that reading is `-21 + level`. The game's music reads -21 on the same measure (the median of the 20 built tracks).

The tool never limits a sound quietly. If the level asked for would push a sample past -1 dBFS, the check fails; lower the level or make the sound less spiky.

## What a phone can play

A phone speaker plays little below 350 Hz. A sound that depends on a low thump is nearly silent on the device the game is for, so:

- Give every sound most of its energy above 350 Hz. A heavy hit gets its weight from a buzz with overtones and a crack of noise, with only a little low sine under it.
- The check fails a sound that loses more than 6 dB on the phone model, and warns above 3 dB.

## Pitch

The music is in many keys, so a tuned sound can clash with it. Three rules keep that rare:

- Use only the notes D, A and G. They are the most common notes across the 20 tracks.
- Use fifths (D with A, G with D) and never thirds, so a sound is neither major nor minor.
- Keep the main pitch at D5 (587 Hz) or higher.

Hits are an exception: they may sit lower, as long as the phone check passes. So are the struck objects, whose overtones are out of tune with their note: their pitch is vague, so they clash less.

## Variants

A sound that plays many times a minute gets several files:

```json
"variants": 3,
"vary": { "pitch": 0.6, "time": 0.1, "tone": 0.2 }
```

`pitch` is in semitones, `time` is a fraction of the decays and sweep times, and `tone` is octaves of filter cutoff. Each is the distance to the farthest variant. The variants are spread evenly across each range, so no two are alike. The files are named `card-draw_1.wav`, `card-draw_2.wav`, and so on.

The game adds its own small variation on top (`SfxPlayer.Tuning`): it never plays the same variant twice running, and it moves each play a little in pitch and volume.

## Reading the numbers

`render` and `stats` print one line per sound:

```
sound                    ms    peak   loud  phone  centre  <150/400/1k/2.5k/6k/+   rise  fall20/40  pitch     shape
card-play                475   -15.2  -27.0   -0.5     618  0/0/97/3/0/0               2   120/184   D5 -3     █▇▇▆▅▃▂
```

| Column | Meaning |
| --- | --- |
| `ms` | The length of the file |
| `peak` | The highest sample, in dBFS |
| `loud` | The loudness measure above; it equals `-21 + level` |
| `phone` | How many dB the sound loses on a phone speaker |
| `centre` | The mean frequency, weighted by energy: higher is brighter |
| `<150/…/+` | The share of the energy, in percent, below 150 Hz, up to 400 Hz, 1 kHz, 2.5 kHz and 6 kHz, and above |
| `rise` | When the sound is loudest, in ms from its start |
| `fall20/40` | How long it then takes to fall by 20 dB and by 40 dB |
| `pitch` | The strongest frequency as a note and the cents away from it. For a sound made of noise it means little |
| `shape` | The loudness over time in 16 steps of 6 dB |

When `render` changes a file, it prints the old file's line under the new one, marked `was`, so the effect of an edit can be read off.

## From a note to a number

The owner listens and says what is wrong in plain words. This table turns the usual words into the fields to change.

| The note | Change |
| --- | --- |
| Too loud, too quiet | `level` |
| Too long, rings on | A shorter `decay`; a shorter reverb `decay` or a lower reverb `mix` |
| Too short, too dry | A longer `decay`; add or raise the reverb |
| Too harsh, too bright | A lower filter `cutoff` or master `lowpass`; `triangle` in place of `saw`; a lower `fm.index`; less noise `gain` |
| Too dull, too soft | The opposite; or a shorter `attack` |
| Too clicky | A longer `attack`; less of the noise layer |
| Too thin | A `unison`; a second layer a fifth away; a little `drive` |
| Too heavy, too boomy | A higher `note`; a higher master `highpass`; less of the low sine |
| Too high, too low | `note`, moving only between D, A and G |
| Sounds like a toy | Less pitch glide; a longer `decay`; add reverb; `triangle` or `sine` in place of `square` |
| Too much like a bell | A lower `fm.index`, or a whole-number `fm.ratio` |
| Tiring when repeated | Lower `level`; more `variants`; a softer start |
| Sounds cheap, like a toy or a slot machine | A struck object or a string in place of a plain oscillator; `drift` on every tuned layer; fewer notes running upward; a darker reverb with a little `predelay` |
| Too clean, too digital | `drift`; a `ladder` filter; a little `drive`; a crackle or noise layer under it |
| Not weird enough | `ring`; a `formant`; an `lfo` on the volume fast enough to buzz; a `body` of an unexpected material |
| Too much like wood, glass, … | Another material; a lower `hardness`; less `body` and more `dry` |

## Takes

A take is a trial version of some sounds in one character, for the owner to compare with the game's own sounds before anything changes. Each take is a folder in `takes/` that holds an `about.json` (`{ "title", "description" }`) and recipes named like the game's. A take may also hold sounds that the game does not have yet.

Takes never go into the game and have no rendered files, but they must pass the same checks as the recipes (`dotnet test` runs them). When the owner chooses a take, or a sound from one, move its recipe into `recipes/`, render it, and delete what was not chosen.

## Ships' sets

Each ship class is to get a set of sounds in the character of what it is grown from. The recipes in `recipes/` are the Seedling's set, in the character called Wood and water, and they are also the base set: a ship uses them for every sound that is not its own. Only a ship's own sounds change with the ship:

- the card plays (`card-play` and one per piece tag) and `card-draw`;
- its hits on the enemy (`hit-light`, `hit-medium`, `hit-heavy`, `hit-blocked`, `focus-spend`);
- what it gains (`block-gain`, `energy-gain`, `focus-gain`, `grow`, `repair`);
- the hits it takes (`ship-hit`, `ship-hit-heavy`, `ship-blocked`, `piece-offline`).

The turns, the enemy's sounds, the end of a fight and every other screen stay shared. A ship's version of a sound keeps the base version's `family`, `level` and rough length, so that a fight reads the same on any ship.

The other ships' sets start as takes: `takes/heliotrope/` (warm circuits) and `takes/fairy-ring/` (the living ship). The game cannot play them yet; that waits until a second ship class is in `game/data/ships.json`.

## The sound board

`tools/sfx.sh board` writes `build/sfx/board.html`: one page that holds every sound, with a play button for each, a button that plays it five times as the game would vary it, an OK mark and a note. When there are takes, each sound lists the game's version ("Now") and each take's version, and the top of the page lists the takes, each with a button that plays a short fight in that take and a note on the whole take. "Copy notes" collects the marks as text. The page is `board.template.html` with the sounds filled in, and it follows the rules for a Claude artifact, so it can be published as a private page and opened on the phone. Only our own sounds go on it, never the music.

## Adding a sound

1. Write `recipes/<id>.json`, then run `tools/sfx.sh render <id>` and read its line.
2. Run `godot --headless --path game --import`, then `tools/sfx.sh check`.
3. Add a member to `SfxCue` in `game/scripts/Audio/SfxCue.cs`. `CardPlayWeapon` is the cue for `card-play-weapon`.
4. Call `Sfx.Play(SfxCue.…)` where the sound should start, beside the visual effect of the same moment.
5. If the cue needs its own volume or pitch range, give it a line in `SfxPlayer.Tuning`.

At startup the game logs how many cues it loaded and which cues have no file. `godot --path game -- --fx-demo` plays a fight by itself and logs every cue as it is triggered.

## How it works

- `Recipe.cs`: the format, the strict loader and the limits on every field.
- `Synth.cs`: turns a recipe into samples. It works at twice the output rate and halves it at the end, so bright tones cannot alias.
- `Sources.cs`: the voices (plain oscillators, band-limited with polyBLEP; struck objects, as one decaying sine per mode; the plucked string), the materials' modes, the ladder filter, the resonators of a body, the vowel filter and the random drift.
- `Dsp.cs`: the envelope curve, the filter (a state-variable filter that stays stable while its cutoff sweeps), the echo, the reverb (eight delay lines feeding each other, whose lengths wobble slightly so the tail does not ring) and the rate halving.
- `Takes.cs`: the takes.
- `RenderCache.cs`: renders each variant of a recipe once per run, so the checks, the check of the saved files and the board share one render.
- `Analysis.cs`: the loudness measure and the numbers in the table above.
- `Checks.cs`: the rules, with their limits.
- `Board.cs`, `board.template.html`: the sound board.

The noise comes from `Pcg32`, the game's own seeded generator, and nothing reads a clock. So one recipe always renders to the same samples on one machine. Another machine's math library can differ in the last bit of a sine, which can move a sample by one step of the 16-bit scale; the check that compares the saved files with a new render allows for that.

The output is 16-bit PCM WAV at 44.1 kHz, mono. `game/project.godot` tells Godot to keep WAV files as plain PCM (`[importer_defaults]`), which costs nothing to play.
